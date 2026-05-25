/*
Networld Parking Lot - Gate Operation MVP Tables
Run this script in SQL Server before running the API.
Database name suggestion: NetworldParkingLot
*/

IF DB_ID('NetworldParkingLot') IS NULL
BEGIN
    CREATE DATABASE NetworldParkingLot;
END
GO

USE NetworldParkingLot;
GO

IF OBJECT_ID('dbo.OutsideDisplayEvents','U') IS NOT NULL DROP TABLE dbo.OutsideDisplayEvents;
IF OBJECT_ID('dbo.GateActivityLogs','U') IS NOT NULL DROP TABLE dbo.GateActivityLogs;
IF OBJECT_ID('dbo.ParkingPayments','U') IS NOT NULL DROP TABLE dbo.ParkingPayments;
IF OBJECT_ID('dbo.ParkingSessions','U') IS NOT NULL DROP TABLE dbo.ParkingSessions;
IF OBJECT_ID('dbo.ParkingInvoices','U') IS NOT NULL DROP TABLE dbo.ParkingInvoices;
IF OBJECT_ID('dbo.ParkingSubscriptions','U') IS NOT NULL DROP TABLE dbo.ParkingSubscriptions;
IF OBJECT_ID('dbo.ParkingCompanies','U') IS NOT NULL DROP TABLE dbo.ParkingCompanies;
IF OBJECT_ID('dbo.SystemCounters','U') IS NOT NULL DROP TABLE dbo.SystemCounters;
IF OBJECT_ID('dbo.SystemSettings','U') IS NOT NULL DROP TABLE dbo.SystemSettings;
IF OBJECT_ID('dbo.AppUsers','U') IS NOT NULL DROP TABLE dbo.AppUsers;
GO

CREATE TABLE dbo.AppUsers
(
    UserId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Username NVARCHAR(100) NOT NULL UNIQUE,
    PasswordHash NVARCHAR(500) NOT NULL,
    FullName NVARCHAR(150) NOT NULL,
    Role NVARCHAR(50) NOT NULL DEFAULT 'Operator',
    Active BIT NOT NULL DEFAULT 1,
    CreatedDate DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
GO

CREATE TABLE dbo.ParkingCompanies
(
    CompanyId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    CompanyCode NVARCHAR(50) NOT NULL UNIQUE,
    CompanyName NVARCHAR(250) NOT NULL,
    ContactPerson NVARCHAR(150) NULL,
    Mobile NVARCHAR(50) NULL,
    TradeLicenseNo NVARCHAR(100) NULL,
    Trn NVARCHAR(100) NULL,
    Email NVARCHAR(150) NULL,
    Address NVARCHAR(500) NULL,
    Status NVARCHAR(30) NOT NULL DEFAULT 'Active',
    OpeningBalance DECIMAL(18,2) NOT NULL DEFAULT 0,
    BillingName NVARCHAR(250) NULL,
    PaymentTerms NVARCHAR(100) NULL,
    CreditLimit DECIMAL(18,2) NOT NULL DEFAULT 0,
    Remarks NVARCHAR(500) NULL,
    InternalNotes NVARCHAR(1000) NULL,
    CreatedDate DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    CreatedBy INT NULL,
    ModifiedDate DATETIME2 NULL,
    ModifiedBy INT NULL
);
GO

CREATE TABLE dbo.ParkingSubscriptions
(
    SubscriptionId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    CompanyId INT NOT NULL,
    PlanType NVARCHAR(30) NOT NULL,
    SlotsPurchased INT NOT NULL,
    RatePerSlot DECIMAL(18,2) NOT NULL DEFAULT 0,
    StartDate DATE NOT NULL,
    EndDate DATE NOT NULL,
    DiscountAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
    VatAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
    TotalAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
    PaidAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
    BalanceAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
    Status NVARCHAR(30) NOT NULL DEFAULT 'Active',
    IsExtraSlot BIT NOT NULL DEFAULT 0,
    SourceInvoiceId INT NULL,
    CreatedDate DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    CreatedBy INT NULL,
    CONSTRAINT FK_ParkingSubscriptions_Companies FOREIGN KEY (CompanyId) REFERENCES dbo.ParkingCompanies(CompanyId)
);
GO

CREATE TABLE dbo.ParkingInvoices
(
    InvoiceId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    InvoiceNo NVARCHAR(50) NOT NULL UNIQUE,
    CompanyId INT NOT NULL,
    SubscriptionId INT NULL,
    SessionId INT NULL,
    InvoiceType NVARCHAR(30) NOT NULL DEFAULT 'Subscription',
    InvoiceDate DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    DueDate DATE NULL,
    PlanType NVARCHAR(30) NULL,
    Slots INT NOT NULL DEFAULT 0,
    SubTotal DECIMAL(18,2) NOT NULL DEFAULT 0,
    DiscountAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
    VatAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
    TotalAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
    PaidAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
    BalanceAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
    Status NVARCHAR(30) NOT NULL DEFAULT 'Unpaid',
    Remarks NVARCHAR(500) NULL,
    CreatedDate DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    CreatedBy INT NULL,
    CONSTRAINT FK_ParkingInvoices_Companies FOREIGN KEY (CompanyId) REFERENCES dbo.ParkingCompanies(CompanyId)
);
GO

CREATE TABLE dbo.ParkingSessions
(
    SessionId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    BarcodeNo NVARCHAR(50) NOT NULL UNIQUE,
    CompanyId INT NOT NULL,
    SubscriptionId INT NULL,
    PlateNo NVARCHAR(30) NULL,
    VehicleType NVARCHAR(30) NOT NULL DEFAULT 'Car',
    DriverName NVARCHAR(100) NULL,
    DriverMobile NVARCHAR(30) NULL,
    EntryTime DATETIME2 NULL,
    ExitTime DATETIME2 NULL,
    Status NVARCHAR(30) NOT NULL DEFAULT 'BarcodeGenerated',
    BarcodeStatus NVARCHAR(30) NOT NULL DEFAULT 'Generated',
    EntryOperatorId INT NULL,
    ExitOperatorId INT NULL,
    OverstayDays INT NOT NULL DEFAULT 0,
    OverstayAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
    ForceExit BIT NOT NULL DEFAULT 0,
    ForceExitReason NVARCHAR(500) NULL,
    Remarks NVARCHAR(500) NULL,
    CreatedDate DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    CreatedBy INT NOT NULL,
    CONSTRAINT FK_ParkingSessions_Companies FOREIGN KEY (CompanyId) REFERENCES dbo.ParkingCompanies(CompanyId),
    CONSTRAINT FK_ParkingSessions_Subscriptions FOREIGN KEY (SubscriptionId) REFERENCES dbo.ParkingSubscriptions(SubscriptionId)
);
GO

CREATE TABLE dbo.ParkingPayments
(
    PaymentId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ReceiptNo NVARCHAR(50) NOT NULL UNIQUE,
    CompanyId INT NOT NULL,
    InvoiceId INT NULL,
    SessionId INT NULL,
    PaymentType NVARCHAR(30) NOT NULL DEFAULT 'Invoice',
    Amount DECIMAL(18,2) NOT NULL,
    PaymentMode NVARCHAR(30) NOT NULL DEFAULT 'Cash',
    ReferenceNo NVARCHAR(100) NULL,
    ReceivedBy INT NOT NULL,
    PaymentDate DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
    Remarks NVARCHAR(500) NULL,
    CONSTRAINT FK_ParkingPayments_Companies FOREIGN KEY (CompanyId) REFERENCES dbo.ParkingCompanies(CompanyId),
    CONSTRAINT FK_ParkingPayments_Invoices FOREIGN KEY (InvoiceId) REFERENCES dbo.ParkingInvoices(InvoiceId),
    CONSTRAINT FK_ParkingPayments_Sessions FOREIGN KEY (SessionId) REFERENCES dbo.ParkingSessions(SessionId)
);
GO

CREATE TABLE dbo.GateActivityLogs
(
    ActivityLogId BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ActionType NVARCHAR(50) NOT NULL,
    CompanyId INT NULL,
    SessionId INT NULL,
    BarcodeNo NVARCHAR(50) NULL,
    PlateNo NVARCHAR(30) NULL,
    Status NVARCHAR(50) NOT NULL,
    Message NVARCHAR(500) NULL,
    OperatorId INT NOT NULL,
    ActionDate DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
GO

CREATE TABLE dbo.OutsideDisplayEvents
(
    DisplayEventId BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    SessionId INT NULL,
    BarcodeNo NVARCHAR(50) NULL,
    PlateNo NVARCHAR(30) NULL,
    CompanyName NVARCHAR(250) NULL,
    DisplayStatus NVARCHAR(50) NOT NULL,
    MainMessage NVARCHAR(150) NOT NULL,
    SubMessage NVARCHAR(250) NULL,
    AmountDue DECIMAL(18,2) NOT NULL DEFAULT 0,
    OverstayDays INT NOT NULL DEFAULT 0,
    CreatedDate DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
GO

CREATE TABLE dbo.SystemSettings
(
    SettingId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    SettingKey NVARCHAR(100) NOT NULL UNIQUE,
    SettingValue NVARCHAR(500) NOT NULL,
    Remarks NVARCHAR(500) NULL
);
GO

CREATE TABLE dbo.SystemCounters
(
    SystemCounterId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    CounterName NVARCHAR(100) NOT NULL UNIQUE,
    LastNumber INT NOT NULL DEFAULT 0,
    UpdatedDate DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);
GO

CREATE INDEX IX_ParkingSessions_Company_Status ON dbo.ParkingSessions(CompanyId, Status);
CREATE INDEX IX_ParkingSessions_Plate_Status ON dbo.ParkingSessions(PlateNo, Status);
CREATE INDEX IX_ParkingSessions_EntryTime ON dbo.ParkingSessions(EntryTime);
CREATE INDEX IX_ParkingCompanies_Status_Name ON dbo.ParkingCompanies(Status, CompanyName);
CREATE INDEX IX_ParkingSubscriptions_Company_Date ON dbo.ParkingSubscriptions(CompanyId, StartDate, EndDate, Status);
CREATE INDEX IX_ParkingInvoices_Company_Balance ON dbo.ParkingInvoices(CompanyId, BalanceAmount);
CREATE INDEX IX_GateActivityLogs_ActionDate ON dbo.GateActivityLogs(ActionDate DESC);
CREATE INDEX IX_OutsideDisplayEvents_CreatedDate ON dbo.OutsideDisplayEvents(CreatedDate DESC);
GO
