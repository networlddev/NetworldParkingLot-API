/*
    Jabal Ali pricing/VAT/bank/vehicle master foundation.

    Safe to run multiple times.
    Adds:
      - Custom rate plans with one period unit at a time: Days, Months, or Years
      - Vehicle type master
      - Optional rate-to-vehicle-type mapping
      - Bank account master
      - Optional invoice/subscription VAT metadata
      - Optional payment BankAccountId
      - VAT settings defaults
*/

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

IF OBJECT_ID('dbo.ParkingRatePlans', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ParkingRatePlans
    (
        RatePlanId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ParkingRatePlans PRIMARY KEY,
        PlanName nvarchar(80) NOT NULL,
        PeriodUnit nvarchar(20) NOT NULL,
        PeriodValue int NOT NULL,
        RatePerSlot decimal(18,2) NOT NULL,
        IsSystemDefault bit NOT NULL CONSTRAINT DF_ParkingRatePlans_IsSystemDefault DEFAULT (0),
        IsActive bit NOT NULL CONSTRAINT DF_ParkingRatePlans_IsActive DEFAULT (1),
        SortOrder int NOT NULL CONSTRAINT DF_ParkingRatePlans_SortOrder DEFAULT (0),
        Remarks nvarchar(500) NULL,
        CreatedDate datetime2 NOT NULL CONSTRAINT DF_ParkingRatePlans_CreatedDate DEFAULT (SYSDATETIME()),
        CreatedBy int NULL,
        ModifiedDate datetime2 NULL,
        ModifiedBy int NULL,
        CONSTRAINT UQ_ParkingRatePlans_PlanName UNIQUE (PlanName),
        CONSTRAINT CK_ParkingRatePlans_PeriodUnit CHECK (PeriodUnit IN ('Days', 'Months', 'Years')),
        CONSTRAINT CK_ParkingRatePlans_PeriodValue CHECK (PeriodValue > 0),
        CONSTRAINT CK_ParkingRatePlans_RatePerSlot CHECK (RatePerSlot >= 0)
    );
END;

IF OBJECT_ID('dbo.ParkingVehicleTypes', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ParkingVehicleTypes
    (
        VehicleTypeId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ParkingVehicleTypes PRIMARY KEY,
        VehicleTypeName nvarchar(60) NOT NULL,
        Description nvarchar(250) NULL,
        IsActive bit NOT NULL CONSTRAINT DF_ParkingVehicleTypes_IsActive DEFAULT (1),
        SortOrder int NOT NULL CONSTRAINT DF_ParkingVehicleTypes_SortOrder DEFAULT (0),
        CreatedDate datetime2 NOT NULL CONSTRAINT DF_ParkingVehicleTypes_CreatedDate DEFAULT (SYSDATETIME()),
        CreatedBy int NULL,
        ModifiedDate datetime2 NULL,
        ModifiedBy int NULL,
        CONSTRAINT UQ_ParkingVehicleTypes_Name UNIQUE (VehicleTypeName)
    );
END;

IF OBJECT_ID('dbo.ParkingRateVehicleTypeMappings', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ParkingRateVehicleTypeMappings
    (
        RateVehicleTypeMappingId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ParkingRateVehicleTypeMappings PRIMARY KEY,
        RatePlanId int NOT NULL,
        VehicleTypeId int NOT NULL,
        RatePerSlotOverride decimal(18,2) NULL,
        IsActive bit NOT NULL CONSTRAINT DF_ParkingRateVehicleTypeMappings_IsActive DEFAULT (1),
        CreatedDate datetime2 NOT NULL CONSTRAINT DF_ParkingRateVehicleTypeMappings_CreatedDate DEFAULT (SYSDATETIME()),
        CreatedBy int NULL,
        ModifiedDate datetime2 NULL,
        ModifiedBy int NULL,
        CONSTRAINT UQ_ParkingRateVehicleTypeMappings_Rate_Vehicle UNIQUE (RatePlanId, VehicleTypeId),
        CONSTRAINT FK_ParkingRateVehicleTypeMappings_RatePlans FOREIGN KEY (RatePlanId) REFERENCES dbo.ParkingRatePlans(RatePlanId),
        CONSTRAINT FK_ParkingRateVehicleTypeMappings_VehicleTypes FOREIGN KEY (VehicleTypeId) REFERENCES dbo.ParkingVehicleTypes(VehicleTypeId)
    );
END;

IF OBJECT_ID('dbo.ParkingBankAccounts', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ParkingBankAccounts
    (
        BankAccountId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ParkingBankAccounts PRIMARY KEY,
        BankName nvarchar(120) NOT NULL,
        AccountName nvarchar(120) NULL,
        AccountNumber nvarchar(80) NULL,
        Iban nvarchar(80) NULL,
        BranchName nvarchar(120) NULL,
        IsActive bit NOT NULL CONSTRAINT DF_ParkingBankAccounts_IsActive DEFAULT (1),
        SortOrder int NOT NULL CONSTRAINT DF_ParkingBankAccounts_SortOrder DEFAULT (0),
        Remarks nvarchar(500) NULL,
        CreatedDate datetime2 NOT NULL CONSTRAINT DF_ParkingBankAccounts_CreatedDate DEFAULT (SYSDATETIME()),
        CreatedBy int NULL,
        ModifiedDate datetime2 NULL,
        ModifiedBy int NULL
    );
END;

IF COL_LENGTH('dbo.ParkingSubscriptions', 'RatePlanId') IS NULL ALTER TABLE dbo.ParkingSubscriptions ADD RatePlanId int NULL;
IF COL_LENGTH('dbo.ParkingSubscriptions', 'VehicleTypeId') IS NULL ALTER TABLE dbo.ParkingSubscriptions ADD VehicleTypeId int NULL;
IF COL_LENGTH('dbo.ParkingSubscriptions', 'VatPercent') IS NULL ALTER TABLE dbo.ParkingSubscriptions ADD VatPercent decimal(9,4) NOT NULL CONSTRAINT DF_ParkingSubscriptions_VatPercent DEFAULT (0);
IF COL_LENGTH('dbo.ParkingSubscriptions', 'VatMode') IS NULL ALTER TABLE dbo.ParkingSubscriptions ADD VatMode nvarchar(20) NOT NULL CONSTRAINT DF_ParkingSubscriptions_VatMode DEFAULT ('Exclusive');

IF COL_LENGTH('dbo.ParkingInvoices', 'RatePlanId') IS NULL ALTER TABLE dbo.ParkingInvoices ADD RatePlanId int NULL;
IF COL_LENGTH('dbo.ParkingInvoices', 'VehicleTypeId') IS NULL ALTER TABLE dbo.ParkingInvoices ADD VehicleTypeId int NULL;
IF COL_LENGTH('dbo.ParkingInvoices', 'VatPercent') IS NULL ALTER TABLE dbo.ParkingInvoices ADD VatPercent decimal(9,4) NOT NULL CONSTRAINT DF_ParkingInvoices_VatPercent DEFAULT (0);
IF COL_LENGTH('dbo.ParkingInvoices', 'VatMode') IS NULL ALTER TABLE dbo.ParkingInvoices ADD VatMode nvarchar(20) NOT NULL CONSTRAINT DF_ParkingInvoices_VatMode DEFAULT ('Exclusive');

IF COL_LENGTH('dbo.ParkingPayments', 'BankAccountId') IS NULL ALTER TABLE dbo.ParkingPayments ADD BankAccountId int NULL;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_ParkingSubscriptions_RatePlans')
    ALTER TABLE dbo.ParkingSubscriptions ADD CONSTRAINT FK_ParkingSubscriptions_RatePlans FOREIGN KEY (RatePlanId) REFERENCES dbo.ParkingRatePlans(RatePlanId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_ParkingSubscriptions_VehicleTypes')
    ALTER TABLE dbo.ParkingSubscriptions ADD CONSTRAINT FK_ParkingSubscriptions_VehicleTypes FOREIGN KEY (VehicleTypeId) REFERENCES dbo.ParkingVehicleTypes(VehicleTypeId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_ParkingInvoices_RatePlans')
    ALTER TABLE dbo.ParkingInvoices ADD CONSTRAINT FK_ParkingInvoices_RatePlans FOREIGN KEY (RatePlanId) REFERENCES dbo.ParkingRatePlans(RatePlanId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_ParkingInvoices_VehicleTypes')
    ALTER TABLE dbo.ParkingInvoices ADD CONSTRAINT FK_ParkingInvoices_VehicleTypes FOREIGN KEY (VehicleTypeId) REFERENCES dbo.ParkingVehicleTypes(VehicleTypeId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_ParkingPayments_BankAccounts')
    ALTER TABLE dbo.ParkingPayments ADD CONSTRAINT FK_ParkingPayments_BankAccounts FOREIGN KEY (BankAccountId) REFERENCES dbo.ParkingBankAccounts(BankAccountId);

IF NOT EXISTS (SELECT 1 FROM dbo.ParkingRatePlans WHERE PlanName = 'Daily')
    INSERT INTO dbo.ParkingRatePlans (PlanName, PeriodUnit, PeriodValue, RatePerSlot, IsSystemDefault, SortOrder, Remarks)
    SELECT 'Daily', 'Days', 1, TRY_CONVERT(decimal(18,2), SettingValue), 1, 10, 'Seeded from DailyRatePerSlot'
    FROM dbo.SystemSettings WHERE SettingKey = 'DailyRatePerSlot';

IF NOT EXISTS (SELECT 1 FROM dbo.ParkingRatePlans WHERE PlanName = 'Weekly')
    INSERT INTO dbo.ParkingRatePlans (PlanName, PeriodUnit, PeriodValue, RatePerSlot, IsSystemDefault, SortOrder, Remarks)
    SELECT 'Weekly', 'Days', 7, TRY_CONVERT(decimal(18,2), SettingValue), 1, 20, 'Seeded from WeeklyRatePerSlot'
    FROM dbo.SystemSettings WHERE SettingKey = 'WeeklyRatePerSlot';

IF NOT EXISTS (SELECT 1 FROM dbo.ParkingRatePlans WHERE PlanName = 'Monthly')
    INSERT INTO dbo.ParkingRatePlans (PlanName, PeriodUnit, PeriodValue, RatePerSlot, IsSystemDefault, SortOrder, Remarks)
    SELECT 'Monthly', 'Months', 1, TRY_CONVERT(decimal(18,2), SettingValue), 1, 30, 'Seeded from MonthlyRatePerSlot'
    FROM dbo.SystemSettings WHERE SettingKey = 'MonthlyRatePerSlot';

IF NOT EXISTS (SELECT 1 FROM dbo.ParkingVehicleTypes WHERE VehicleTypeName = 'Car')
    INSERT INTO dbo.ParkingVehicleTypes (VehicleTypeName, Description, SortOrder) VALUES ('Car', 'Default car/light vehicle type', 10);
IF NOT EXISTS (SELECT 1 FROM dbo.ParkingVehicleTypes WHERE VehicleTypeName = 'Truck')
    INSERT INTO dbo.ParkingVehicleTypes (VehicleTypeName, Description, SortOrder) VALUES ('Truck', 'Truck/heavy vehicle type', 20);
IF NOT EXISTS (SELECT 1 FROM dbo.ParkingVehicleTypes WHERE VehicleTypeName = 'Bus')
    INSERT INTO dbo.ParkingVehicleTypes (VehicleTypeName, Description, SortOrder) VALUES ('Bus', 'Bus/coach vehicle type', 30);

IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = 'VatEnabled')
    INSERT INTO dbo.SystemSettings (SettingKey, SettingValue, Remarks) VALUES ('VatEnabled', 'true', 'Enable VAT calculation on invoices and subscriptions');
IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = 'DefaultVatMode')
    INSERT INTO dbo.SystemSettings (SettingKey, SettingValue, Remarks) VALUES ('DefaultVatMode', 'Exclusive', 'Default VAT mode: Exclusive or Inclusive');

COMMIT TRANSACTION;
