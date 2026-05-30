/*
    Networld Parking Lot
    010_Invoices_Module_Enhancements.sql

    Purpose:
    - Adds audit/cancellation columns required by the Invoices module.
    - Adds practical indexes for invoice listing, invoice-wise payment, export and search.
    - Seeds optional invoice-related settings if missing.

    Safe to run multiple times.
*/

SET NOCOUNT ON;

PRINT 'Running 010_Invoices_Module_Enhancements.sql...';

IF OBJECT_ID(N'dbo.ParkingInvoices', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.ParkingInvoices', 'ModifiedDate') IS NULL
    BEGIN
        ALTER TABLE dbo.ParkingInvoices ADD ModifiedDate DATETIME2 NULL;
        PRINT 'Added ParkingInvoices.ModifiedDate';
    END

    IF COL_LENGTH('dbo.ParkingInvoices', 'ModifiedBy') IS NULL
    BEGIN
        ALTER TABLE dbo.ParkingInvoices ADD ModifiedBy INT NULL;
        PRINT 'Added ParkingInvoices.ModifiedBy';
    END

    IF COL_LENGTH('dbo.ParkingInvoices', 'CancelledDate') IS NULL
    BEGIN
        ALTER TABLE dbo.ParkingInvoices ADD CancelledDate DATETIME2 NULL;
        PRINT 'Added ParkingInvoices.CancelledDate';
    END

    IF COL_LENGTH('dbo.ParkingInvoices', 'CancelledBy') IS NULL
    BEGIN
        ALTER TABLE dbo.ParkingInvoices ADD CancelledBy INT NULL;
        PRINT 'Added ParkingInvoices.CancelledBy';
    END

    IF COL_LENGTH('dbo.ParkingInvoices', 'CancellationReason') IS NULL
    BEGIN
        ALTER TABLE dbo.ParkingInvoices ADD CancellationReason NVARCHAR(500) NULL;
        PRINT 'Added ParkingInvoices.CancellationReason';
    END

    IF COL_LENGTH('dbo.ParkingInvoices', 'Remarks') IS NULL
    BEGIN
        ALTER TABLE dbo.ParkingInvoices ADD Remarks NVARCHAR(500) NULL;
        PRINT 'Added ParkingInvoices.Remarks';
    END

    IF COL_LENGTH('dbo.ParkingInvoices', 'PaidAmount') IS NOT NULL
       AND COL_LENGTH('dbo.ParkingInvoices', 'BalanceAmount') IS NOT NULL
       AND COL_LENGTH('dbo.ParkingInvoices', 'TotalAmount') IS NOT NULL
    BEGIN
        UPDATE dbo.ParkingInvoices
        SET PaidAmount = ISNULL(PaidAmount, 0),
            BalanceAmount = CASE
                WHEN BalanceAmount IS NULL THEN ISNULL(TotalAmount, 0) - ISNULL(PaidAmount, 0)
                ELSE BalanceAmount
            END
        WHERE PaidAmount IS NULL OR BalanceAmount IS NULL;
    END

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ParkingInvoices_InvoiceNo' AND object_id = OBJECT_ID('dbo.ParkingInvoices'))
        CREATE INDEX IX_ParkingInvoices_InvoiceNo ON dbo.ParkingInvoices (InvoiceNo);

    IF COL_LENGTH('dbo.ParkingInvoices', 'CompanyId') IS NOT NULL
       AND COL_LENGTH('dbo.ParkingInvoices', 'InvoiceDate') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ParkingInvoices_Company_InvoiceDate' AND object_id = OBJECT_ID('dbo.ParkingInvoices'))
        CREATE INDEX IX_ParkingInvoices_Company_InvoiceDate ON dbo.ParkingInvoices (CompanyId, InvoiceDate DESC);

    IF COL_LENGTH('dbo.ParkingInvoices', 'Status') IS NOT NULL
       AND COL_LENGTH('dbo.ParkingInvoices', 'InvoiceDate') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ParkingInvoices_Status_InvoiceDate' AND object_id = OBJECT_ID('dbo.ParkingInvoices'))
        CREATE INDEX IX_ParkingInvoices_Status_InvoiceDate ON dbo.ParkingInvoices (Status, InvoiceDate DESC);

    IF COL_LENGTH('dbo.ParkingInvoices', 'InvoiceType') IS NOT NULL
       AND COL_LENGTH('dbo.ParkingInvoices', 'InvoiceDate') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ParkingInvoices_Type_InvoiceDate' AND object_id = OBJECT_ID('dbo.ParkingInvoices'))
        CREATE INDEX IX_ParkingInvoices_Type_InvoiceDate ON dbo.ParkingInvoices (InvoiceType, InvoiceDate DESC);

    IF COL_LENGTH('dbo.ParkingInvoices', 'BalanceAmount') IS NOT NULL
       AND COL_LENGTH('dbo.ParkingInvoices', 'InvoiceDate') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ParkingInvoices_Balance_InvoiceDate' AND object_id = OBJECT_ID('dbo.ParkingInvoices'))
        CREATE INDEX IX_ParkingInvoices_Balance_InvoiceDate ON dbo.ParkingInvoices (BalanceAmount, InvoiceDate DESC);
END
ELSE
BEGIN
    PRINT 'Table dbo.ParkingInvoices not found. Skipping invoice table changes.';
END

IF OBJECT_ID(N'dbo.ParkingPayments', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.ParkingPayments', 'InvoiceId') IS NOT NULL
       AND COL_LENGTH('dbo.ParkingPayments', 'PaymentDate') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ParkingPayments_Invoice_PaymentDate' AND object_id = OBJECT_ID('dbo.ParkingPayments'))
        CREATE INDEX IX_ParkingPayments_Invoice_PaymentDate ON dbo.ParkingPayments (InvoiceId, PaymentDate DESC);

    IF COL_LENGTH('dbo.ParkingPayments', 'CompanyId') IS NOT NULL
       AND COL_LENGTH('dbo.ParkingPayments', 'PaymentDate') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ParkingPayments_Company_PaymentDate' AND object_id = OBJECT_ID('dbo.ParkingPayments'))
        CREATE INDEX IX_ParkingPayments_Company_PaymentDate ON dbo.ParkingPayments (CompanyId, PaymentDate DESC);
END

IF OBJECT_ID(N'dbo.SystemSettings', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = 'InvoicePrinterName')
        INSERT INTO dbo.SystemSettings (SettingKey, SettingValue, Remarks)
        VALUES ('InvoicePrinterName', '', 'Default Windows invoice printer name. Example: E-PoS printer driver (1)');

    IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = 'InvoiceReportDefaultFolder')
        INSERT INTO dbo.SystemSettings (SettingKey, SettingValue, Remarks)
        VALUES ('InvoiceReportDefaultFolder', 'NetworldParkingReports', 'Default folder name used by Windows app for invoice exports.');
END

PRINT '010_Invoices_Module_Enhancements.sql completed.';
