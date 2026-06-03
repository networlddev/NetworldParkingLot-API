/*
Preview invoice/payment consistency issues.
SELECT-only script. Do not use this script to update live data.
Run manually before any reconciliation/fix script.
*/

SET NOCOUNT ON;

;WITH PaymentByInvoice AS
(
    SELECT
        p.InvoiceId,
        COUNT_BIG(*) AS LinkedPaymentCount,
        SUM(p.Amount) AS LinkedPaymentAmount
    FROM dbo.ParkingPayments p
    WHERE p.InvoiceId IS NOT NULL
    GROUP BY p.InvoiceId
),
InvoiceReconciliation AS
(
    SELECT
        i.InvoiceId,
        i.InvoiceNo,
        i.CompanyId,
        c.CompanyCode,
        c.CompanyName,
        i.InvoiceType,
        i.InvoiceDate,
        i.DueDate,
        i.TotalAmount,
        i.PaidAmount AS StoredPaidAmount,
        i.BalanceAmount AS StoredBalanceAmount,
        ISNULL(p.LinkedPaymentAmount, 0) AS LinkedPaymentAmount,
        ISNULL(p.LinkedPaymentCount, 0) AS LinkedPaymentCount,
        i.TotalAmount - ISNULL(p.LinkedPaymentAmount, 0) AS ExpectedBalanceAmount,
        i.PaidAmount - ISNULL(p.LinkedPaymentAmount, 0) AS PaidDifference,
        i.BalanceAmount - (i.TotalAmount - ISNULL(p.LinkedPaymentAmount, 0)) AS BalanceDifference,
        i.TotalAmount - i.PaidAmount - i.BalanceAmount AS StoredMathDifference,
        i.Status AS StoredStatus,
        CASE
            WHEN i.Status = 'Cancelled' THEN 'Cancelled'
            WHEN i.TotalAmount - ISNULL(p.LinkedPaymentAmount, 0) <= 0 THEN 'Paid'
            WHEN ISNULL(p.LinkedPaymentAmount, 0) > 0 THEN 'Partial'
            ELSE 'Unpaid'
        END AS ExpectedStatus
    FROM dbo.ParkingInvoices i
    INNER JOIN dbo.ParkingCompanies c ON c.CompanyId = i.CompanyId
    LEFT JOIN PaymentByInvoice p ON p.InvoiceId = i.InvoiceId
)
SELECT
    'Invoice totals vs linked payment rows' AS PreviewName,
    r.*
FROM InvoiceReconciliation r
WHERE
    ABS(r.PaidDifference) >= 0.01
    OR ABS(r.BalanceDifference) >= 0.01
    OR ABS(r.StoredMathDifference) >= 0.01
    OR r.TotalAmount < 0
    OR r.StoredPaidAmount < 0
    OR r.StoredBalanceAmount < 0
    OR r.LinkedPaymentAmount < 0
    OR r.StoredStatus <> r.ExpectedStatus
ORDER BY r.InvoiceDate DESC, r.InvoiceId DESC;

;WITH CompanyPendingFromInvoices AS
(
    SELECT
        i.CompanyId,
        SUM(CASE WHEN i.Status <> 'Cancelled' AND i.BalanceAmount > 0 THEN i.BalanceAmount ELSE 0 END) AS InvoicePendingAmount
    FROM dbo.ParkingInvoices i
    GROUP BY i.CompanyId
),
CompanyUnlinkedPayments AS
(
    SELECT
        p.CompanyId,
        COUNT_BIG(*) AS UnlinkedPaymentCount,
        SUM(p.Amount) AS UnlinkedPaymentAmount
    FROM dbo.ParkingPayments p
    WHERE p.InvoiceId IS NULL
      AND p.SessionId IS NULL
    GROUP BY p.CompanyId
)
SELECT
    'Company-level unlinked payments' AS PreviewName,
    c.CompanyId,
    c.CompanyCode,
    c.CompanyName,
    ISNULL(i.InvoicePendingAmount, 0) AS InvoicePendingAmount,
    ISNULL(p.UnlinkedPaymentCount, 0) AS UnlinkedPaymentCount,
    ISNULL(p.UnlinkedPaymentAmount, 0) AS UnlinkedPaymentAmount
FROM dbo.ParkingCompanies c
LEFT JOIN CompanyPendingFromInvoices i ON i.CompanyId = c.CompanyId
LEFT JOIN CompanyUnlinkedPayments p ON p.CompanyId = c.CompanyId
WHERE ISNULL(p.UnlinkedPaymentCount, 0) > 0
ORDER BY c.CompanyName;

SELECT
    'Invalid payment rows' AS PreviewName,
    p.PaymentId,
    p.ReceiptNo,
    p.CompanyId,
    c.CompanyCode,
    c.CompanyName,
    p.InvoiceId,
    i.InvoiceNo,
    p.SessionId,
    p.PaymentType,
    p.Amount,
    p.PaymentMode,
    p.ReferenceNo,
    p.PaymentDate,
    p.ReceivedBy,
    CASE
        WHEN p.Amount <= 0 THEN 'Payment amount is zero or negative'
        WHEN p.PaymentMode IS NULL OR LTRIM(RTRIM(p.PaymentMode)) = '' THEN 'Payment mode is blank'
        WHEN p.InvoiceId IS NOT NULL AND i.InvoiceId IS NULL THEN 'Linked invoice does not exist'
        WHEN p.InvoiceId IS NOT NULL AND i.CompanyId <> p.CompanyId THEN 'Payment company differs from invoice company'
        ELSE 'Review'
    END AS Issue
FROM dbo.ParkingPayments p
INNER JOIN dbo.ParkingCompanies c ON c.CompanyId = p.CompanyId
LEFT JOIN dbo.ParkingInvoices i ON i.InvoiceId = p.InvoiceId
WHERE
    p.Amount <= 0
    OR p.PaymentMode IS NULL
    OR LTRIM(RTRIM(p.PaymentMode)) = ''
    OR (p.InvoiceId IS NOT NULL AND i.InvoiceId IS NULL)
    OR (p.InvoiceId IS NOT NULL AND i.CompanyId <> p.CompanyId)
ORDER BY p.PaymentDate DESC, p.PaymentId DESC;

SELECT
    'Payments linked to cancelled invoices' AS PreviewName,
    p.PaymentId,
    p.ReceiptNo,
    p.CompanyId,
    c.CompanyCode,
    c.CompanyName,
    p.InvoiceId,
    i.InvoiceNo,
    i.Status AS InvoiceStatus,
    p.PaymentType,
    p.Amount,
    p.PaymentMode,
    p.PaymentDate,
    p.ReceivedBy
FROM dbo.ParkingPayments p
INNER JOIN dbo.ParkingInvoices i ON i.InvoiceId = p.InvoiceId
INNER JOIN dbo.ParkingCompanies c ON c.CompanyId = p.CompanyId
WHERE i.Status = 'Cancelled'
ORDER BY p.PaymentDate DESC, p.PaymentId DESC;
