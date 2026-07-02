select c.CompanyName, c.CompanyId,s.SubscriptionId,s.BalanceAmount,i.Status from ParkingSubscriptions s inner join ParkingCompanies c on s.CompanyId = c.CompanyId inner join ParkingInvoices I on i.SubscriptionId = s.SubscriptionId  where c.CompanyName = 'xyz' 


/*
    Purpose:
      Permanently remove selected mistaken subscriptions for one company, together
      with their linked invoices, invoice payments, and balance adjustments.

    Important:
      This is a destructive cleanup script. It is intentionally set to preview
      mode by default. Review the preview result first, then set @Execute = 1.

    Safety rules:
      - Only deletes subscriptions listed in @SubscriptionIds.
      - Only deletes them if they belong to @CompanyId.
      - Refuses to continue if any selected subscription has vehicle/barcode
        sessions linked to it. Those records are operational history and should
        not be erased by this cleanup.
      - Deletes linked ParkingPayments first, then ParkingInvoices, balance
        adjustments, and subscriptions.
      - Optional cleanup switches can stop auto-renew after deletion. Use this
        when the deleted rows are being regenerated on UI refresh.

    How to use:
      1. Set @CompanyId.
      2. Add the wrong SubscriptionId values into @SubscriptionIds.
      3. If these rows are auto-created again after refresh, set
         @DisableRemainingSubscriptionAutoRenewAfterCleanup = 1.
      4. Run with @Execute = 0 and review preview rows.
      5. Change @Execute = 1 only after confirming the preview is correct.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @CompanyId int = 0;      -- TODO: set company id here
DECLARE @Execute bit = 0;        -- 0 = preview only, 1 = permanently delete
DECLARE @DisableRemainingSubscriptionAutoRenewAfterCleanup bit = 0;
DECLARE @DisableCompanyAutoRenewAfterCleanup bit = 0;

DECLARE @SubscriptionIds TABLE
(
    SubscriptionId int NOT NULL PRIMARY KEY
);

-- TODO: put ONLY the wrong subscription ids here.
-- Example:
-- INSERT INTO @SubscriptionIds (SubscriptionId) VALUES (101), (102), (103);

IF @CompanyId <= 0
BEGIN
    THROW 51000, 'Set @CompanyId before running this script.', 1;
END;

IF NOT EXISTS (SELECT 1 FROM @SubscriptionIds)
BEGIN
    THROW 51001, 'Add at least one SubscriptionId into @SubscriptionIds before running this script.', 1;
END;

DECLARE @TargetSubscriptions TABLE
(
    SubscriptionId int NOT NULL PRIMARY KEY,
    SourceInvoiceId int NULL
);

INSERT INTO @TargetSubscriptions (SubscriptionId, SourceInvoiceId)
SELECT s.SubscriptionId, s.SourceInvoiceId
FROM dbo.ParkingSubscriptions s
INNER JOIN @SubscriptionIds ids ON ids.SubscriptionId = s.SubscriptionId
WHERE s.CompanyId = @CompanyId;

IF (SELECT COUNT(*) FROM @TargetSubscriptions) <> (SELECT COUNT(*) FROM @SubscriptionIds)
BEGIN
    SELECT
        ids.SubscriptionId,
        FoundForCompany =
            CASE WHEN s.SubscriptionId IS NULL THEN 0 ELSE 1 END,
        ActualCompanyId = s.CompanyId,
        s.PlanType,
        s.StartDate,
        s.EndDate,
        s.Status,
        s.TotalAmount,
        s.BalanceAmount
    FROM @SubscriptionIds ids
    LEFT JOIN dbo.ParkingSubscriptions s ON s.SubscriptionId = ids.SubscriptionId;

    THROW 51002, 'One or more SubscriptionId values were not found for the selected @CompanyId. Nothing was deleted.', 1;
END;

IF EXISTS (
    SELECT 1
    FROM dbo.ParkingSessions ps
    INNER JOIN @TargetSubscriptions ts ON ts.SubscriptionId = ps.SubscriptionId
)
BEGIN
    SELECT
        ps.SessionId,
        ps.BarcodeNo,
        ps.CompanyId,
        ps.SubscriptionId,
        ps.PlateNo,
        ps.Status,
        ps.BarcodeStatus,
        ps.EntryTime,
        ps.ExitTime
    FROM dbo.ParkingSessions ps
    INNER JOIN @TargetSubscriptions ts ON ts.SubscriptionId = ps.SubscriptionId
    ORDER BY ps.CreatedDate DESC, ps.SessionId DESC;

    THROW 51003, 'Selected subscription has vehicle/barcode sessions. Cleanup refused to protect operational history.', 1;
END;

DECLARE @TargetInvoices TABLE
(
    InvoiceId int NOT NULL PRIMARY KEY
);

INSERT INTO @TargetInvoices (InvoiceId)
SELECT DISTINCT i.InvoiceId
FROM dbo.ParkingInvoices i
LEFT JOIN @TargetSubscriptions tsBySubscription ON tsBySubscription.SubscriptionId = i.SubscriptionId
LEFT JOIN @TargetSubscriptions tsBySourceInvoice ON tsBySourceInvoice.SourceInvoiceId = i.InvoiceId
WHERE i.CompanyId = @CompanyId
  AND (tsBySubscription.SubscriptionId IS NOT NULL OR tsBySourceInvoice.SubscriptionId IS NOT NULL);

SELECT 'SubscriptionsToDelete' AS PreviewSection, s.*
FROM dbo.ParkingSubscriptions s
INNER JOIN @TargetSubscriptions ts ON ts.SubscriptionId = s.SubscriptionId
ORDER BY s.StartDate, s.SubscriptionId;

SELECT 'InvoicesToDelete' AS PreviewSection, i.*
FROM dbo.ParkingInvoices i
INNER JOIN @TargetInvoices ti ON ti.InvoiceId = i.InvoiceId
ORDER BY i.InvoiceDate, i.InvoiceId;

SELECT 'PaymentsToDelete' AS PreviewSection, p.*
FROM dbo.ParkingPayments p
INNER JOIN @TargetInvoices ti ON ti.InvoiceId = p.InvoiceId
ORDER BY p.PaymentDate, p.PaymentId;

SELECT 'BalanceAdjustmentsToDelete' AS PreviewSection, ba.*
FROM dbo.ParkingCompanyBalanceAdjustments ba
LEFT JOIN @TargetSubscriptions ts ON ts.SubscriptionId = ba.SubscriptionId
LEFT JOIN @TargetInvoices ti ON ti.InvoiceId = ba.InvoiceId
WHERE ba.CompanyId = @CompanyId
  AND (ts.SubscriptionId IS NOT NULL OR ti.InvoiceId IS NOT NULL)
ORDER BY ba.CreatedDate, ba.BalanceAdjustmentId;

SELECT
    Summary = CASE WHEN @Execute = 1 THEN 'READY_TO_DELETE' ELSE 'PREVIEW_ONLY' END,
    SubscriptionCount = (SELECT COUNT(*) FROM @TargetSubscriptions),
    InvoiceCount = (SELECT COUNT(*) FROM @TargetInvoices),
    PaymentCount = (
        SELECT COUNT(*)
        FROM dbo.ParkingPayments p
        INNER JOIN @TargetInvoices ti ON ti.InvoiceId = p.InvoiceId
    ),
    BalanceAdjustmentCount = (
        SELECT COUNT(*)
        FROM dbo.ParkingCompanyBalanceAdjustments ba
        LEFT JOIN @TargetSubscriptions ts ON ts.SubscriptionId = ba.SubscriptionId
        LEFT JOIN @TargetInvoices ti ON ti.InvoiceId = ba.InvoiceId
        WHERE ba.CompanyId = @CompanyId
          AND (ts.SubscriptionId IS NOT NULL OR ti.InvoiceId IS NOT NULL)
    ),
    TotalInvoiceBalanceRemoved = (
        SELECT ISNULL(SUM(i.BalanceAmount), 0)
        FROM dbo.ParkingInvoices i
        INNER JOIN @TargetInvoices ti ON ti.InvoiceId = i.InvoiceId
    ),
    RemainingAutoRenewSubscriptionsAfterDelete = (
        SELECT COUNT(*)
        FROM dbo.ParkingSubscriptions s
        WHERE s.CompanyId = @CompanyId
          AND s.SubscriptionId NOT IN (SELECT SubscriptionId FROM @TargetSubscriptions)
          AND s.Status = 'Active'
          AND ISNULL(s.IsExtraSlot, 0) = 0
          AND ISNULL(s.AutoRenew, 0) = 1
    ),
    CompanyAutoRenewCurrentlyEnabled = (
        SELECT CAST(ISNULL(MAX(CASE WHEN AutoRenewSubscriptions = 1 THEN 1 ELSE 0 END), 0) AS bit)
        FROM dbo.ParkingCompanies
        WHERE CompanyId = @CompanyId
    ),
    WillDisableRemainingSubscriptionAutoRenew = @DisableRemainingSubscriptionAutoRenewAfterCleanup,
    WillDisableCompanyAutoRenew = @DisableCompanyAutoRenewAfterCleanup;

IF @Execute = 0
BEGIN
    PRINT 'Preview only. No records were deleted. Set @Execute = 1 to permanently delete the previewed rows.';
    RETURN;
END;

BEGIN TRANSACTION;

DELETE p
FROM dbo.ParkingPayments p
INNER JOIN @TargetInvoices ti ON ti.InvoiceId = p.InvoiceId;

DELETE ba
FROM dbo.ParkingCompanyBalanceAdjustments ba
LEFT JOIN @TargetSubscriptions ts ON ts.SubscriptionId = ba.SubscriptionId
LEFT JOIN @TargetInvoices ti ON ti.InvoiceId = ba.InvoiceId
WHERE ba.CompanyId = @CompanyId
  AND (ts.SubscriptionId IS NOT NULL OR ti.InvoiceId IS NOT NULL);

DELETE i
FROM dbo.ParkingInvoices i
INNER JOIN @TargetInvoices ti ON ti.InvoiceId = i.InvoiceId;

DELETE s
FROM dbo.ParkingSubscriptions s
INNER JOIN @TargetSubscriptions ts ON ts.SubscriptionId = s.SubscriptionId;

IF @DisableRemainingSubscriptionAutoRenewAfterCleanup = 1
BEGIN
    UPDATE s
    SET
        AutoRenew = 0,
        ModifiedDate = SYSDATETIME()
    FROM dbo.ParkingSubscriptions s
    WHERE s.CompanyId = @CompanyId
      AND s.Status = 'Active'
      AND ISNULL(s.IsExtraSlot, 0) = 0
      AND ISNULL(s.AutoRenew, 0) = 1;
END;

IF @DisableCompanyAutoRenewAfterCleanup = 1
BEGIN
    UPDATE dbo.ParkingCompanies
    SET
        AutoRenewSubscriptions = 0,
        ModifiedDate = SYSDATETIME()
    WHERE CompanyId = @CompanyId
      AND ISNULL(AutoRenewSubscriptions, 0) = 1;
END;

COMMIT TRANSACTION;

PRINT 'Selected subscriptions and linked financial records were permanently deleted.';
IF @DisableRemainingSubscriptionAutoRenewAfterCleanup = 1
    PRINT 'Auto-renew was disabled on remaining active regular subscriptions for this company.';
IF @DisableCompanyAutoRenewAfterCleanup = 1
    PRINT 'Company-level auto-renew was disabled for this company.';
