SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH('dbo.ParkingCompanies', 'AutoRenewSubscriptions') IS NULL
BEGIN
    ALTER TABLE dbo.ParkingCompanies
        ADD AutoRenewSubscriptions bit NOT NULL
            CONSTRAINT DF_ParkingCompanies_AutoRenewSubscriptions DEFAULT (1);
END;

IF COL_LENGTH('dbo.ParkingSubscriptions', 'AutoRenew') IS NULL
BEGIN
    ALTER TABLE dbo.ParkingSubscriptions
        ADD AutoRenew bit NOT NULL
            CONSTRAINT DF_ParkingSubscriptions_AutoRenew DEFAULT (1);
END;

IF COL_LENGTH('dbo.ParkingSubscriptions', 'StoppedDate') IS NULL
BEGIN
    ALTER TABLE dbo.ParkingSubscriptions ADD StoppedDate datetime2 NULL;
END;

IF COL_LENGTH('dbo.ParkingSubscriptions', 'StoppedBy') IS NULL
BEGIN
    ALTER TABLE dbo.ParkingSubscriptions ADD StoppedBy int NULL;
END;

IF COL_LENGTH('dbo.ParkingSubscriptions', 'StopReason') IS NULL
BEGIN
    ALTER TABLE dbo.ParkingSubscriptions ADD StopReason nvarchar(500) NULL;
END;

IF OBJECT_ID('dbo.ParkingCompanyBalanceAdjustments', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ParkingCompanyBalanceAdjustments
    (
        BalanceAdjustmentId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ParkingCompanyBalanceAdjustments PRIMARY KEY,
        CompanyId int NOT NULL,
        SubscriptionId int NULL,
        InvoiceId int NULL,
        AdjustmentType nvarchar(30) NOT NULL CONSTRAINT DF_ParkingCompanyBalanceAdjustments_AdjustmentType DEFAULT ('Credit'),
        Amount decimal(18,2) NOT NULL,
        AppliedAmount decimal(18,2) NOT NULL CONSTRAINT DF_ParkingCompanyBalanceAdjustments_AppliedAmount DEFAULT (0),
        RemainingAmount decimal(18,2) NOT NULL,
        Reason nvarchar(500) NOT NULL,
        OldSlots int NULL,
        NewSlots int NULL,
        EffectiveDate datetime2 NULL,
        CreatedDate datetime2 NOT NULL CONSTRAINT DF_ParkingCompanyBalanceAdjustments_CreatedDate DEFAULT (SYSDATETIME()),
        CreatedBy int NULL,
        CONSTRAINT FK_ParkingCompanyBalanceAdjustments_ParkingCompanies
            FOREIGN KEY (CompanyId) REFERENCES dbo.ParkingCompanies(CompanyId)
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_ParkingCompanyBalanceAdjustments_Company_CreatedDate'
      AND object_id = OBJECT_ID('dbo.ParkingCompanyBalanceAdjustments')
)
BEGIN
    CREATE INDEX IX_ParkingCompanyBalanceAdjustments_Company_CreatedDate
        ON dbo.ParkingCompanyBalanceAdjustments(CompanyId, CreatedDate);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_ParkingCompanyBalanceAdjustments_OpenCredit'
      AND object_id = OBJECT_ID('dbo.ParkingCompanyBalanceAdjustments')
)
BEGIN
    CREATE INDEX IX_ParkingCompanyBalanceAdjustments_OpenCredit
        ON dbo.ParkingCompanyBalanceAdjustments(CompanyId, RemainingAmount)
        WHERE RemainingAmount > 0;
END;

COMMIT TRANSACTION;
