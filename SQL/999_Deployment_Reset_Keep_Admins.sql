/*
Deployment reset for NetworldParkingLot.

This script deletes operational data, logs, counters, settings, and non-protected
users while preserving the access-control metadata required for login:

- AppModules
- AppModuleActions
- AppRoles
- AppRolePermissions
- AppUsers for System Admin / Super Admin
- AppUserRoles for the protected users

Run only against the deployment database you intend to reset.
*/

SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @ProtectedUsers TABLE (UserId INT PRIMARY KEY);

INSERT INTO @ProtectedUsers (UserId)
SELECT DISTINCT u.UserId
FROM dbo.AppUsers u
WHERE LOWER(REPLACE(REPLACE(LTRIM(RTRIM(u.Username)), ' ', '_'), '-', '_')) IN ('admin', 'system_admin', 'super_admin', 'superadmin')
   OR LOWER(REPLACE(REPLACE(LTRIM(RTRIM(u.FullName)), ' ', '_'), '-', '_')) IN ('system_admin', 'super_admin', 'superadmin')
   OR LOWER(REPLACE(REPLACE(LTRIM(RTRIM(u.Role)), ' ', '_'), '-', '_')) IN ('admin', 'system_admin', 'super_admin', 'superadmin');

IF NOT EXISTS (SELECT 1 FROM @ProtectedUsers)
BEGIN
    THROW 51000, 'Reset aborted: no System Admin or Super Admin account was found.', 1;
END;

DELETE FROM dbo.SystemActivityLogDetails;
DELETE FROM dbo.SystemActivityLogs;

DELETE FROM dbo.OutsideDisplayEvents;
DELETE FROM dbo.GateActivityLogs;

DELETE FROM dbo.ParkingPayments;
DELETE FROM dbo.ParkingSessions;
DELETE FROM dbo.ParkingInvoices;
DELETE FROM dbo.ParkingSubscriptions;
DELETE FROM dbo.ParkingCompanies;

DELETE FROM dbo.SystemCounters;
DELETE FROM dbo.SystemSettings;

DELETE FROM dbo.AppUserPermissions
WHERE UserId NOT IN (SELECT UserId FROM @ProtectedUsers);

DELETE FROM dbo.AppUserRoles
WHERE UserId NOT IN (SELECT UserId FROM @ProtectedUsers);

DELETE FROM dbo.AppUsers
WHERE UserId NOT IN (SELECT UserId FROM @ProtectedUsers);

UPDATE dbo.AppUsers
SET Active = 1,
    Status = 'Active',
    ModifiedDate = GETDATE()
WHERE UserId IN (SELECT UserId FROM @ProtectedUsers);

COMMIT TRANSACTION;

SELECT 'Deployment reset completed.' AS Message;
SELECT UserId, Username, FullName, Role, Status, Active
FROM dbo.AppUsers
ORDER BY UserId;
