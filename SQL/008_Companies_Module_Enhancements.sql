/*
Networld Parking Lot - Companies Module Enhancements
Run this once on the existing NetworldParkingLot database.
Adds optional company master fields and keeps subscription rates/settings ready.
*/

USE NetworldParkingLot;
GO

IF COL_LENGTH('dbo.ParkingCompanies', 'TradeLicenseNo') IS NULL
    ALTER TABLE dbo.ParkingCompanies ADD TradeLicenseNo NVARCHAR(100) NULL;
GO

IF COL_LENGTH('dbo.ParkingCompanies', 'Trn') IS NULL
    ALTER TABLE dbo.ParkingCompanies ADD Trn NVARCHAR(100) NULL;
GO

IF COL_LENGTH('dbo.ParkingCompanies', 'BillingName') IS NULL
    ALTER TABLE dbo.ParkingCompanies ADD BillingName NVARCHAR(250) NULL;
GO

IF COL_LENGTH('dbo.ParkingCompanies', 'PaymentTerms') IS NULL
    ALTER TABLE dbo.ParkingCompanies ADD PaymentTerms NVARCHAR(100) NULL;
GO

IF COL_LENGTH('dbo.ParkingCompanies', 'CreditLimit') IS NULL
    ALTER TABLE dbo.ParkingCompanies ADD CreditLimit DECIMAL(18,2) NOT NULL CONSTRAINT DF_ParkingCompanies_CreditLimit DEFAULT 0;
GO

IF COL_LENGTH('dbo.ParkingCompanies', 'InternalNotes') IS NULL
    ALTER TABLE dbo.ParkingCompanies ADD InternalNotes NVARCHAR(1000) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ParkingCompanies_Status_Name' AND object_id = OBJECT_ID('dbo.ParkingCompanies'))
    CREATE INDEX IX_ParkingCompanies_Status_Name ON dbo.ParkingCompanies(Status, CompanyName);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ParkingSubscriptions_Company_Date' AND object_id = OBJECT_ID('dbo.ParkingSubscriptions'))
    CREATE INDEX IX_ParkingSubscriptions_Company_Date ON dbo.ParkingSubscriptions(CompanyId, StartDate, EndDate, Status);
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = 'DailyRatePerSlot')
    INSERT INTO dbo.SystemSettings (SettingKey, SettingValue, Remarks) VALUES ('DailyRatePerSlot', '50', 'Default daily company/extra slot rate per slot');
ELSE
    UPDATE dbo.SystemSettings SET Remarks = COALESCE(Remarks, 'Default daily company/extra slot rate per slot') WHERE SettingKey = 'DailyRatePerSlot';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = 'WeeklyRatePerSlot')
    INSERT INTO dbo.SystemSettings (SettingKey, SettingValue, Remarks) VALUES ('WeeklyRatePerSlot', '150', 'Default weekly company/extra slot rate per slot');
ELSE
    UPDATE dbo.SystemSettings SET Remarks = COALESCE(Remarks, 'Default weekly company/extra slot rate per slot') WHERE SettingKey = 'WeeklyRatePerSlot';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = 'MonthlyRatePerSlot')
    INSERT INTO dbo.SystemSettings (SettingKey, SettingValue, Remarks) VALUES ('MonthlyRatePerSlot', '500', 'Default monthly company/extra slot rate per slot');
ELSE
    UPDATE dbo.SystemSettings SET Remarks = COALESCE(Remarks, 'Default monthly company/extra slot rate per slot') WHERE SettingKey = 'MonthlyRatePerSlot';
GO
