/*
    Jabal Ali deployment cleanup for NetworldParkingLot.

    Goal:
      Clear all operational/customer data from the selected deployment database,
      preserve application settings and access metadata, and keep admin users
      only. Gate operators and other non-admin users are deleted.

    Preserved:
      - SystemSettings
      - AppModules
      - AppModuleActions
      - AppRoles
      - AppRolePermissions
      - AppUsers detected as Admin / System Admin / Super Admin
      - AppUserRoles for protected admin users
      - AppUserPermissions for protected admin users

    Deleted:
      - ParkingCompanies
      - ParkingSubscriptions
      - ParkingCompanyBalanceAdjustments
      - ParkingInvoices
      - ParkingPayments
      - ParkingSessions / barcodes
      - GateActivityLogs
      - OutsideDisplayEvents
      - SystemActivityLogs and details
      - SystemCounters
      - AppUsers not detected as admin, including gate operators
      - AppUserRoles/AppUserPermissions for deleted users

    Important:
      This is destructive. It is intentionally preview-only by default.
      Run once with @Execute = 0 and review all result sets.
      Then change @Execute = 1 only on the exact Jabal Ali database connection.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Execute bit = 0; -- 0 = preview only, 1 = permanently delete
DECLARE @ExpectedDatabaseName sysname = N''; -- TODO: type the exact Jabal Ali database name here

IF DB_NAME() IN ('master', 'model', 'msdb', 'tempdb')
BEGIN
    THROW 51000, 'Cleanup aborted: this script must not run against a system database.', 1;
END;

IF NULLIF(LTRIM(RTRIM(@ExpectedDatabaseName)), N'') IS NULL
BEGIN
    THROW 51005, 'Cleanup aborted: set @ExpectedDatabaseName to the exact database name before running this script.', 1;
END;

IF DB_NAME() <> @ExpectedDatabaseName
BEGIN
    DECLARE @DatabaseMismatchMessage nvarchar(400) =
        CONCAT('Cleanup aborted: connected database is [', DB_NAME(), '] but @ExpectedDatabaseName is [', @ExpectedDatabaseName, '].');
    THROW 51006, @DatabaseMismatchMessage, 1;
END;

IF OBJECT_ID('dbo.AppUsers', 'U') IS NULL
BEGIN
    THROW 51001, 'Cleanup aborted: dbo.AppUsers was not found.', 1;
END;

IF OBJECT_ID('dbo.SystemSettings', 'U') IS NULL
BEGIN
    THROW 51002, 'Cleanup aborted: dbo.SystemSettings was not found; settings must be preserved.', 1;
END;

IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings)
BEGIN
    THROW 51003, 'Cleanup aborted: SystemSettings is empty; refusing to run because settings must be preserved.', 1;
END;

DECLARE @ProtectedUsers TABLE
(
    UserId int NOT NULL PRIMARY KEY
);

INSERT INTO @ProtectedUsers (UserId)
SELECT DISTINCT u.UserId
FROM dbo.AppUsers u
WHERE LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(u.Username, ''))), ' ', '_'), '-', '_')) IN
      ('admin', 'administrator', 'system_admin', 'super_admin', 'superadmin')
   OR LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(u.FullName, ''))), ' ', '_'), '-', '_')) IN
      ('admin', 'administrator', 'system_admin', 'super_admin', 'superadmin', 'admin_user')
   OR LOWER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(u.Role, ''))), ' ', '_'), '-', '_')) IN
      ('admin', 'administrator', 'system_admin', 'super_admin', 'superadmin');

IF NOT EXISTS (SELECT 1 FROM @ProtectedUsers)
BEGIN
    SELECT UserId, Username, FullName, Role, Status, Active
    FROM dbo.AppUsers
    ORDER BY UserId;

    THROW 51004, 'Cleanup aborted: no admin/system-admin/super-admin user was found to preserve.', 1;
END;

DECLARE @Tables TABLE
(
    TableName sysname NOT NULL PRIMARY KEY
);

INSERT INTO @Tables (TableName)
VALUES
    ('AppUsers'),
    ('AppUserRoles'),
    ('AppUserPermissions'),
    ('AppRoles'),
    ('AppRolePermissions'),
    ('AppModules'),
    ('AppModuleActions'),
    ('SystemSettings'),
    ('SystemCounters'),
    ('SystemActivityLogDetails'),
    ('SystemActivityLogs'),
    ('GateActivityLogs'),
    ('OutsideDisplayEvents'),
    ('ParkingPayments'),
    ('ParkingSessions'),
    ('ParkingCompanyBalanceAdjustments'),
    ('ParkingInvoices'),
    ('ParkingSubscriptions'),
    ('ParkingCompanies');

DECLARE @Before TABLE
(
    TableName sysname NOT NULL PRIMARY KEY,
    RowCountValue bigint NOT NULL
);

INSERT INTO @Before (TableName, RowCountValue)
SELECT t.name, SUM(p.rows)
FROM sys.tables t
INNER JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0, 1)
INNER JOIN @Tables target ON target.TableName = t.name
GROUP BY t.name;

SELECT
    Mode = CASE WHEN @Execute = 1 THEN 'READY_TO_DELETE' ELSE 'PREVIEW_ONLY' END,
    DatabaseName = DB_NAME(),
    AdminUsersToKeep = (SELECT COUNT(*) FROM @ProtectedUsers),
    NonAdminUsersToDelete = (
        SELECT COUNT(*)
        FROM dbo.AppUsers u
        WHERE NOT EXISTS (SELECT 1 FROM @ProtectedUsers p WHERE p.UserId = u.UserId)
    ),
    CompaniesToDelete = COALESCE((SELECT RowCountValue FROM @Before WHERE TableName = 'ParkingCompanies'), 0),
    SubscriptionsToDelete = COALESCE((SELECT RowCountValue FROM @Before WHERE TableName = 'ParkingSubscriptions'), 0),
    InvoicesToDelete = COALESCE((SELECT RowCountValue FROM @Before WHERE TableName = 'ParkingInvoices'), 0),
    PaymentsToDelete = COALESCE((SELECT RowCountValue FROM @Before WHERE TableName = 'ParkingPayments'), 0),
    BarcodeSessionsToDelete = COALESCE((SELECT RowCountValue FROM @Before WHERE TableName = 'ParkingSessions'), 0),
    SettingsToKeep = COALESCE((SELECT RowCountValue FROM @Before WHERE TableName = 'SystemSettings'), 0);

SELECT 'AdminUsersToKeep' AS PreviewSection, u.UserId, u.Username, u.FullName, u.Role, u.Status, u.Active
FROM dbo.AppUsers u
INNER JOIN @ProtectedUsers p ON p.UserId = u.UserId
ORDER BY u.UserId;

SELECT 'UsersToDelete' AS PreviewSection, u.UserId, u.Username, u.FullName, u.Role, u.Status, u.Active
FROM dbo.AppUsers u
WHERE NOT EXISTS (SELECT 1 FROM @ProtectedUsers p WHERE p.UserId = u.UserId)
ORDER BY u.UserId;

SELECT
    'BeforeCounts' AS PreviewSection,
    target.TableName,
    COALESCE(b.RowCountValue, 0) AS RowCountValue
FROM @Tables target
LEFT JOIN @Before b ON b.TableName = target.TableName
ORDER BY target.TableName;

IF @Execute = 0
BEGIN
    PRINT 'Preview only. No records were deleted. Set @Execute = 1 only after reviewing this output and confirming the Jabal Ali database connection.';
    RETURN;
END;

BEGIN TRANSACTION;

IF OBJECT_ID('dbo.SystemActivityLogDetails', 'U') IS NOT NULL DELETE FROM dbo.SystemActivityLogDetails;
IF OBJECT_ID('dbo.SystemActivityLogs', 'U') IS NOT NULL DELETE FROM dbo.SystemActivityLogs;

IF OBJECT_ID('dbo.OutsideDisplayEvents', 'U') IS NOT NULL DELETE FROM dbo.OutsideDisplayEvents;
IF OBJECT_ID('dbo.GateActivityLogs', 'U') IS NOT NULL DELETE FROM dbo.GateActivityLogs;

IF OBJECT_ID('dbo.ParkingPayments', 'U') IS NOT NULL DELETE FROM dbo.ParkingPayments;
IF OBJECT_ID('dbo.ParkingSessions', 'U') IS NOT NULL DELETE FROM dbo.ParkingSessions;
IF OBJECT_ID('dbo.ParkingCompanyBalanceAdjustments', 'U') IS NOT NULL DELETE FROM dbo.ParkingCompanyBalanceAdjustments;
IF OBJECT_ID('dbo.ParkingInvoices', 'U') IS NOT NULL DELETE FROM dbo.ParkingInvoices;
IF OBJECT_ID('dbo.ParkingSubscriptions', 'U') IS NOT NULL DELETE FROM dbo.ParkingSubscriptions;
IF OBJECT_ID('dbo.ParkingCompanies', 'U') IS NOT NULL DELETE FROM dbo.ParkingCompanies;

IF OBJECT_ID('dbo.SystemCounters', 'U') IS NOT NULL DELETE FROM dbo.SystemCounters;

IF OBJECT_ID('dbo.AppUserPermissions', 'U') IS NOT NULL
BEGIN
    DELETE up
    FROM dbo.AppUserPermissions up
    WHERE NOT EXISTS (SELECT 1 FROM @ProtectedUsers p WHERE p.UserId = up.UserId);
END;

IF OBJECT_ID('dbo.AppUserRoles', 'U') IS NOT NULL
BEGIN
    DELETE ur
    FROM dbo.AppUserRoles ur
    WHERE NOT EXISTS (SELECT 1 FROM @ProtectedUsers p WHERE p.UserId = ur.UserId);
END;

DELETE u
FROM dbo.AppUsers u
WHERE NOT EXISTS (SELECT 1 FROM @ProtectedUsers p WHERE p.UserId = u.UserId);

UPDATE u
SET
    Active = 1,
    Status = 'Active',
    ModifiedDate = GETDATE()
FROM dbo.AppUsers u
INNER JOIN @ProtectedUsers p ON p.UserId = u.UserId;

IF OBJECT_ID('dbo.SystemActivityLogDetails', 'U') IS NOT NULL DBCC CHECKIDENT ('dbo.SystemActivityLogDetails', RESEED, 0) WITH NO_INFOMSGS;
IF OBJECT_ID('dbo.SystemActivityLogs', 'U') IS NOT NULL DBCC CHECKIDENT ('dbo.SystemActivityLogs', RESEED, 0) WITH NO_INFOMSGS;
IF OBJECT_ID('dbo.OutsideDisplayEvents', 'U') IS NOT NULL DBCC CHECKIDENT ('dbo.OutsideDisplayEvents', RESEED, 0) WITH NO_INFOMSGS;
IF OBJECT_ID('dbo.GateActivityLogs', 'U') IS NOT NULL DBCC CHECKIDENT ('dbo.GateActivityLogs', RESEED, 0) WITH NO_INFOMSGS;
IF OBJECT_ID('dbo.ParkingPayments', 'U') IS NOT NULL DBCC CHECKIDENT ('dbo.ParkingPayments', RESEED, 0) WITH NO_INFOMSGS;
IF OBJECT_ID('dbo.ParkingSessions', 'U') IS NOT NULL DBCC CHECKIDENT ('dbo.ParkingSessions', RESEED, 0) WITH NO_INFOMSGS;
IF OBJECT_ID('dbo.ParkingCompanyBalanceAdjustments', 'U') IS NOT NULL DBCC CHECKIDENT ('dbo.ParkingCompanyBalanceAdjustments', RESEED, 0) WITH NO_INFOMSGS;
IF OBJECT_ID('dbo.ParkingInvoices', 'U') IS NOT NULL DBCC CHECKIDENT ('dbo.ParkingInvoices', RESEED, 0) WITH NO_INFOMSGS;
IF OBJECT_ID('dbo.ParkingSubscriptions', 'U') IS NOT NULL DBCC CHECKIDENT ('dbo.ParkingSubscriptions', RESEED, 0) WITH NO_INFOMSGS;
IF OBJECT_ID('dbo.ParkingCompanies', 'U') IS NOT NULL DBCC CHECKIDENT ('dbo.ParkingCompanies', RESEED, 0) WITH NO_INFOMSGS;
IF OBJECT_ID('dbo.SystemCounters', 'U') IS NOT NULL DBCC CHECKIDENT ('dbo.SystemCounters', RESEED, 0) WITH NO_INFOMSGS;

COMMIT TRANSACTION;

SELECT
    b.TableName,
    b.RowCountValue AS BeforeRows,
    COALESCE(a.RowCountValue, 0) AS AfterRows
FROM @Before b
OUTER APPLY
(
    SELECT SUM(p.rows) AS RowCountValue
    FROM sys.tables t
    INNER JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0, 1)
    WHERE t.name = b.TableName
) a
ORDER BY b.TableName;

SELECT 'Deployment cleanup completed. Operational data, companies, subscriptions, invoices, payments, barcodes, logs, counters, and non-admin users were deleted. Settings and admin users were preserved.' AS Message;

SELECT UserId, Username, FullName, Role, Status, Active
FROM dbo.AppUsers
ORDER BY UserId;
