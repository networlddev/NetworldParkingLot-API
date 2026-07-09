/*
    Ras Al Khor one-time data correction: 7 motors wrong collected payment.

    Scenario:
      The active subscription was corrected to 10 monthly slots from 01-Jul-2026 to 01-Aug-2026.
      The system still shows AED 7,800 collected, but the real collection was AED 3,000.

    What this script does when @Execute = 1:
      - Finds the exact active 7 motors subscription.
      - Finds the subscription invoice.
      - Verifies linked payment rows total AED 7,800.
      - Changes the subscription and invoice total to AED 3,000.
      - Changes the linked subscription payment row(s) to total AED 3,000.
      - Sets the subscription and invoice PaidAmount/BalanceAmount/Status from the corrected total/payment.
      - Removes unapplied customer-balance credit created for the same subscription/invoice, if that table exists.

    Safety:
      - Preview-only by default.
      - Requires the exact database name.
      - Refuses to run if more than one matching company/subscription/invoice is found.
      - Refuses to run if linked payments do not total the expected wrong amount.

    Run:
      1. Set @ExpectedDatabaseName to the exact Ras Al Khor database name.
      2. Run with @Execute = 0 and review the result sets.
      3. If preview is correct, set @Execute = 1 and run once.
*/

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Execute bit = 0; -- 0 = preview only, 1 = permanently correct data
DECLARE @ExpectedDatabaseName sysname = N'NetworldParkingLot'; -- TODO: set exact Ras Al Khor database name

DECLARE @CompanyNameLike nvarchar(100) = N'%7%motor%';
DECLARE @CorrectStartDate date = '2026-07-01';
DECLARE @CorrectEndDate date = '2026-08-01';
DECLARE @CorrectSlots int = 10;
DECLARE @WrongCollectedAmount decimal(18,2) = 7800.00;
DECLARE @CorrectCollectedAmount decimal(18,2) = 3000.00;
DECLARE @CorrectTotalAmount decimal(18,2) = 3000.00;
DECLARE @OperatorId int = NULL; -- optional: set admin user id for ModifiedBy, otherwise existing ModifiedBy is kept

IF DB_NAME() IN ('master', 'model', 'msdb', 'tempdb')
BEGIN
    THROW 51000, 'Correction aborted: this script must not run against a system database.', 1;
END;

IF NULLIF(LTRIM(RTRIM(@ExpectedDatabaseName)), N'') IS NULL
BEGIN
    THROW 51001, 'Correction aborted: set @ExpectedDatabaseName to the exact Ras Al Khor database name.', 1;
END;

IF DB_NAME() <> @ExpectedDatabaseName
BEGIN
    DECLARE @Mismatch nvarchar(400) =
        CONCAT('Correction aborted: connected database is [', DB_NAME(), '] but @ExpectedDatabaseName is [', @ExpectedDatabaseName, '].');
    THROW 51002, @Mismatch, 1;
END;

IF @CorrectCollectedAmount < 0 OR @CorrectTotalAmount < 0 OR @WrongCollectedAmount <= 0 OR @CorrectCollectedAmount >= @WrongCollectedAmount
BEGIN
    THROW 51003, 'Correction aborted: amount settings are invalid.', 1;
END;

IF @CorrectCollectedAmount > @CorrectTotalAmount
BEGIN
    THROW 51011, 'Correction aborted: corrected collected amount cannot be greater than corrected invoice total.', 1;
END;

IF OBJECT_ID('dbo.ParkingCompanies', 'U') IS NULL
   OR OBJECT_ID('dbo.ParkingSubscriptions', 'U') IS NULL
   OR OBJECT_ID('dbo.ParkingInvoices', 'U') IS NULL
   OR OBJECT_ID('dbo.ParkingPayments', 'U') IS NULL
BEGIN
    THROW 51004, 'Correction aborted: required parking tables were not found.', 1;
END;

DECLARE @CompanyMatches TABLE
(
    CompanyId int NOT NULL PRIMARY KEY,
    CompanyCode nvarchar(50) NULL,
    CompanyName nvarchar(250) NOT NULL,
    Status nvarchar(50) NULL
);

INSERT INTO @CompanyMatches (CompanyId, CompanyCode, CompanyName, Status)
SELECT c.CompanyId, c.CompanyCode, c.CompanyName, c.Status
FROM dbo.ParkingCompanies c
WHERE LOWER(c.CompanyName) LIKE LOWER(@CompanyNameLike);

SELECT 'CompanyMatches' AS PreviewName, *
FROM @CompanyMatches
ORDER BY CompanyName;

IF (SELECT COUNT(*) FROM @CompanyMatches) <> 1
BEGIN
    THROW 51005, 'Correction aborted: expected exactly one matching company. Adjust @CompanyNameLike or verify the company name.', 1;
END;

DECLARE @CompanyId int = (SELECT TOP (1) CompanyId FROM @CompanyMatches);

DECLARE @SubscriptionMatches TABLE
(
    SubscriptionId int NOT NULL PRIMARY KEY,
    CompanyId int NOT NULL,
    PlanType nvarchar(30) NOT NULL,
    SlotsPurchased int NOT NULL,
    RatePerSlot decimal(18,2) NOT NULL,
    StartDate datetime2 NOT NULL,
    EndDate datetime2 NOT NULL,
    TotalAmount decimal(18,2) NOT NULL,
    PaidAmount decimal(18,2) NOT NULL,
    BalanceAmount decimal(18,2) NOT NULL,
    Status nvarchar(30) NOT NULL,
    SourceInvoiceId int NULL
);

INSERT INTO @SubscriptionMatches
(
    SubscriptionId,
    CompanyId,
    PlanType,
    SlotsPurchased,
    RatePerSlot,
    StartDate,
    EndDate,
    TotalAmount,
    PaidAmount,
    BalanceAmount,
    Status,
    SourceInvoiceId
)
SELECT
    s.SubscriptionId,
    s.CompanyId,
    s.PlanType,
    s.SlotsPurchased,
    s.RatePerSlot,
    s.StartDate,
    s.EndDate,
    s.TotalAmount,
    s.PaidAmount,
    s.BalanceAmount,
    s.Status,
    s.SourceInvoiceId
FROM dbo.ParkingSubscriptions s
WHERE s.CompanyId = @CompanyId
  AND CAST(s.StartDate AS date) = @CorrectStartDate
  AND CAST(s.EndDate AS date) = @CorrectEndDate
  AND s.SlotsPurchased = @CorrectSlots
  AND s.Status <> 'Cancelled';

SELECT 'SubscriptionMatches' AS PreviewName, *
FROM @SubscriptionMatches
ORDER BY SubscriptionId;

IF (SELECT COUNT(*) FROM @SubscriptionMatches) <> 1
BEGIN
    THROW 51006, 'Correction aborted: expected exactly one matching corrected subscription.', 1;
END;

DECLARE @SubscriptionId int = (SELECT TOP (1) SubscriptionId FROM @SubscriptionMatches);

DECLARE @InvoiceMatches TABLE
(
    InvoiceId int NOT NULL PRIMARY KEY,
    InvoiceNo nvarchar(50) NOT NULL,
    CompanyId int NOT NULL,
    SubscriptionId int NULL,
    TotalAmount decimal(18,2) NOT NULL,
    PaidAmount decimal(18,2) NOT NULL,
    BalanceAmount decimal(18,2) NOT NULL,
    Status nvarchar(30) NOT NULL
);

INSERT INTO @InvoiceMatches
SELECT
    i.InvoiceId,
    i.InvoiceNo,
    i.CompanyId,
    i.SubscriptionId,
    i.TotalAmount,
    i.PaidAmount,
    i.BalanceAmount,
    i.Status
FROM dbo.ParkingInvoices i
WHERE i.CompanyId = @CompanyId
  AND i.SubscriptionId = @SubscriptionId
  AND i.Status <> 'Cancelled';

SELECT 'InvoiceMatches' AS PreviewName, *
FROM @InvoiceMatches
ORDER BY InvoiceId;

IF (SELECT COUNT(*) FROM @InvoiceMatches) <> 1
BEGIN
    THROW 51007, 'Correction aborted: expected exactly one active invoice for the subscription.', 1;
END;

DECLARE @InvoiceId int = (SELECT TOP (1) InvoiceId FROM @InvoiceMatches);
DECLARE @CurrentInvoiceTotal decimal(18,2) = (SELECT TOP (1) TotalAmount FROM @InvoiceMatches);
DECLARE @CorrectBalance decimal(18,2) = @CorrectTotalAmount - @CorrectCollectedAmount;
DECLARE @CorrectStatus nvarchar(30) =
    CASE
        WHEN @CorrectBalance <= 0 THEN 'Paid'
        WHEN @CorrectCollectedAmount > 0 THEN 'Partial'
        ELSE 'Unpaid'
    END;

DECLARE @PaymentRows TABLE
(
    PaymentId int NOT NULL PRIMARY KEY,
    ReceiptNo nvarchar(50) NOT NULL,
    InvoiceId int NULL,
    Amount decimal(18,2) NOT NULL,
    PaymentMode nvarchar(50) NOT NULL,
    PaymentDate datetime2 NOT NULL,
    Remarks nvarchar(500) NULL
);

INSERT INTO @PaymentRows
SELECT
    p.PaymentId,
    p.ReceiptNo,
    p.InvoiceId,
    p.Amount,
    p.PaymentMode,
    p.PaymentDate,
    p.Remarks
FROM dbo.ParkingPayments p
WHERE p.CompanyId = @CompanyId
  AND p.InvoiceId = @InvoiceId;

SELECT 'PaymentRowsBefore' AS PreviewName, *
FROM @PaymentRows
ORDER BY PaymentDate, PaymentId;

DECLARE @PaymentCount int = (SELECT COUNT(*) FROM @PaymentRows);
DECLARE @LinkedPaymentTotal decimal(18,2) = ISNULL((SELECT SUM(Amount) FROM @PaymentRows), 0);

IF @PaymentCount = 0
BEGIN
    THROW 51009, 'Correction aborted: no linked payment rows were found for the invoice.', 1;
END;

IF ABS(@LinkedPaymentTotal - @WrongCollectedAmount) >= 0.01
BEGIN
    DECLARE @PaymentMismatch nvarchar(400) =
        CONCAT('Correction aborted: linked payments total ', @LinkedPaymentTotal, ' but expected ', @WrongCollectedAmount, '.');
    THROW 51010, @PaymentMismatch, 1;
END;

IF OBJECT_ID('dbo.ParkingCompanyBalanceAdjustments', 'U') IS NOT NULL
BEGIN
    SELECT
        'OpenBalanceAdjustmentsBefore' AS PreviewName,
        a.BalanceAdjustmentId,
        a.CompanyId,
        a.SubscriptionId,
        a.InvoiceId,
        a.AdjustmentType,
        a.Amount,
        a.AppliedAmount,
        a.RemainingAmount,
        a.Reason,
        a.OldSlots,
        a.NewSlots,
        a.EffectiveDate,
        a.CreatedDate
    FROM dbo.ParkingCompanyBalanceAdjustments a
    WHERE a.CompanyId = @CompanyId
      AND (a.SubscriptionId = @SubscriptionId OR a.InvoiceId = @InvoiceId)
      AND a.AppliedAmount = 0
      AND a.RemainingAmount = a.Amount
      AND ABS(a.Amount - (@WrongCollectedAmount - @CorrectCollectedAmount)) < 0.01;
END;

SELECT
    'CorrectionPlan' AS PreviewName,
    DB_NAME() AS DatabaseName,
    @Execute AS ExecuteFlag,
    @CompanyId AS CompanyId,
    @SubscriptionId AS SubscriptionId,
    @InvoiceId AS InvoiceId,
    @PaymentCount AS LinkedPaymentRows,
    @LinkedPaymentTotal AS CurrentLinkedPaymentTotal,
    @CurrentInvoiceTotal AS CurrentInvoiceTotal,
    @CorrectTotalAmount AS CorrectInvoiceTotal,
    @CorrectCollectedAmount AS CorrectLinkedPaymentTotal,
    @CorrectBalance AS CorrectBalance,
    @CorrectStatus AS CorrectStatus;

IF @Execute = 0
BEGIN
    SELECT 'Preview only. No data was changed. Set @Execute = 1 after reviewing the rows above.' AS Result;
    RETURN;
END;

BEGIN TRANSACTION;

IF @PaymentCount = 1
BEGIN
    UPDATE p
    SET
        p.Amount = @CorrectCollectedAmount,
        p.Remarks = CONCAT(
            ISNULL(NULLIF(p.Remarks, ''), 'Data correction'),
            ' | Corrected mistaken collected amount from AED ',
            CONVERT(varchar(40), @WrongCollectedAmount),
            ' to AED ',
            CONVERT(varchar(40), @CorrectCollectedAmount),
            ' for 7 motors subscription.'
        )
    FROM dbo.ParkingPayments p
    WHERE p.PaymentId = (SELECT TOP (1) PaymentId FROM @PaymentRows);
END
ELSE
BEGIN
    ;WITH OrderedPayments AS
    (
        SELECT
            p.PaymentId,
            ROW_NUMBER() OVER (ORDER BY p.PaymentDate, p.PaymentId) AS RowNo
        FROM dbo.ParkingPayments p
        INNER JOIN @PaymentRows pr ON pr.PaymentId = p.PaymentId
    )
    UPDATE p
    SET
        p.Amount = CASE WHEN op.RowNo = 1 THEN @CorrectCollectedAmount ELSE 0 END,
        p.Remarks = CONCAT(
            ISNULL(NULLIF(p.Remarks, ''), 'Data correction'),
            CASE
                WHEN op.RowNo = 1 THEN ' | Kept corrected collected amount for 7 motors subscription.'
                ELSE ' | Zeroed duplicate mistaken payment amount for 7 motors subscription.'
            END
        )
    FROM dbo.ParkingPayments p
    INNER JOIN OrderedPayments op ON op.PaymentId = p.PaymentId;
END;

UPDATE dbo.ParkingInvoices
SET
    Slots = @CorrectSlots,
    SubTotal = @CorrectTotalAmount,
    DiscountAmount = 0,
    VatAmount = 0,
    TotalAmount = @CorrectTotalAmount,
    PaidAmount = @CorrectCollectedAmount,
    BalanceAmount = @CorrectBalance,
    Status = @CorrectStatus,
    ModifiedBy = COALESCE(@OperatorId, ModifiedBy),
    ModifiedDate = SYSDATETIME(),
    Remarks = CONCAT(
        ISNULL(NULLIF(Remarks, ''), 'Data correction'),
        ' | Corrected mistaken payment total from AED ',
        CONVERT(varchar(40), @WrongCollectedAmount),
        ' to AED ',
        CONVERT(varchar(40), @CorrectCollectedAmount),
        '.'
    )
WHERE InvoiceId = @InvoiceId;

UPDATE dbo.ParkingSubscriptions
SET
    SlotsPurchased = @CorrectSlots,
    RatePerSlot = CASE WHEN @CorrectSlots > 0 THEN @CorrectTotalAmount / @CorrectSlots ELSE RatePerSlot END,
    DiscountAmount = 0,
    VatAmount = 0,
    TotalAmount = @CorrectTotalAmount,
    PaidAmount = @CorrectCollectedAmount,
    BalanceAmount = @CorrectBalance,
    ModifiedBy = COALESCE(@OperatorId, ModifiedBy),
    ModifiedDate = SYSDATETIME(),
    Remarks = CONCAT(
        ISNULL(NULLIF(Remarks, ''), 'Data correction'),
        ' | Corrected mistaken payment total from AED ',
        CONVERT(varchar(40), @WrongCollectedAmount),
        ' to AED ',
        CONVERT(varchar(40), @CorrectCollectedAmount),
        '.'
    )
WHERE SubscriptionId = @SubscriptionId;

IF OBJECT_ID('dbo.ParkingCompanyBalanceAdjustments', 'U') IS NOT NULL
BEGIN
    DELETE a
    FROM dbo.ParkingCompanyBalanceAdjustments a
    WHERE a.CompanyId = @CompanyId
      AND (a.SubscriptionId = @SubscriptionId OR a.InvoiceId = @InvoiceId)
      AND a.AppliedAmount = 0
      AND a.RemainingAmount = a.Amount
      AND ABS(a.Amount - (@WrongCollectedAmount - @CorrectCollectedAmount)) < 0.01;
END;

COMMIT TRANSACTION;

SELECT 'Correction completed.' AS Result;

SELECT
    'SubscriptionAfter' AS PreviewName,
    s.SubscriptionId,
    s.CompanyId,
    s.PlanType,
    s.SlotsPurchased,
    s.StartDate,
    s.EndDate,
    s.RatePerSlot,
    s.DiscountAmount,
    s.VatAmount,
    s.TotalAmount,
    s.PaidAmount,
    s.BalanceAmount,
    s.Status,
    s.ModifiedDate
FROM dbo.ParkingSubscriptions s
WHERE s.SubscriptionId = @SubscriptionId;

SELECT
    'InvoiceAfter' AS PreviewName,
    i.InvoiceId,
    i.InvoiceNo,
    i.SubscriptionId,
    i.Slots,
    i.SubTotal,
    i.DiscountAmount,
    i.VatAmount,
    i.TotalAmount,
    i.PaidAmount,
    i.BalanceAmount,
    i.Status,
    i.ModifiedDate
FROM dbo.ParkingInvoices i
WHERE i.InvoiceId = @InvoiceId;

SELECT
    'PaymentRowsAfter' AS PreviewName,
    p.PaymentId,
    p.ReceiptNo,
    p.InvoiceId,
    p.Amount,
    p.PaymentMode,
    p.PaymentDate,
    p.Remarks
FROM dbo.ParkingPayments p
WHERE p.CompanyId = @CompanyId
  AND p.InvoiceId = @InvoiceId
ORDER BY p.PaymentDate, p.PaymentId;
