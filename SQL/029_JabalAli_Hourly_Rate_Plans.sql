/*
    Jabal Ali hourly custom rate plans.

    Safe to run multiple times.
    Allows ParkingRatePlans.PeriodUnit = 'Hours' and preserves time values in
    subscription StartDate/EndDate for hourly subscriptions.
*/

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

IF OBJECT_ID('dbo.ParkingRatePlans', 'U') IS NOT NULL
BEGIN
    IF EXISTS (
        SELECT 1
        FROM sys.check_constraints
        WHERE name = 'CK_ParkingRatePlans_PeriodUnit'
          AND parent_object_id = OBJECT_ID('dbo.ParkingRatePlans')
    )
    BEGIN
        ALTER TABLE dbo.ParkingRatePlans DROP CONSTRAINT CK_ParkingRatePlans_PeriodUnit;
    END;

    ALTER TABLE dbo.ParkingRatePlans WITH CHECK ADD CONSTRAINT CK_ParkingRatePlans_PeriodUnit
        CHECK (PeriodUnit IN ('Hours', 'Days', 'Months', 'Years'));
END;

IF OBJECT_ID('dbo.ParkingSubscriptions', 'U') IS NOT NULL
BEGIN
    IF EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE name = 'IX_ParkingSubscriptions_Company_Date'
          AND object_id = OBJECT_ID('dbo.ParkingSubscriptions')
    )
    BEGIN
        DROP INDEX IX_ParkingSubscriptions_Company_Date ON dbo.ParkingSubscriptions;
    END;

    IF EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE name = 'IX_ParkingSubscriptions_Company_Status_Date'
          AND object_id = OBJECT_ID('dbo.ParkingSubscriptions')
    )
    BEGIN
        DROP INDEX IX_ParkingSubscriptions_Company_Status_Date ON dbo.ParkingSubscriptions;
    END;

    IF EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE name = 'IX_ParkingSubscriptions_Status_EndDate'
          AND object_id = OBJECT_ID('dbo.ParkingSubscriptions')
    )
    BEGIN
        DROP INDEX IX_ParkingSubscriptions_Status_EndDate ON dbo.ParkingSubscriptions;
    END;

    IF EXISTS (
        SELECT 1
        FROM sys.columns
        WHERE object_id = OBJECT_ID('dbo.ParkingSubscriptions')
          AND name = 'StartDate'
          AND system_type_id = TYPE_ID('date')
    )
    BEGIN
        ALTER TABLE dbo.ParkingSubscriptions ALTER COLUMN StartDate datetime2 NOT NULL;
    END;

    IF EXISTS (
        SELECT 1
        FROM sys.columns
        WHERE object_id = OBJECT_ID('dbo.ParkingSubscriptions')
          AND name = 'EndDate'
          AND system_type_id = TYPE_ID('date')
    )
    BEGIN
        ALTER TABLE dbo.ParkingSubscriptions ALTER COLUMN EndDate datetime2 NOT NULL;
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE name = 'IX_ParkingSubscriptions_Company_Date'
          AND object_id = OBJECT_ID('dbo.ParkingSubscriptions')
    )
    BEGIN
        CREATE INDEX IX_ParkingSubscriptions_Company_Date
            ON dbo.ParkingSubscriptions(CompanyId, StartDate, EndDate, Status);
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE name = 'IX_ParkingSubscriptions_Company_Status_Date'
          AND object_id = OBJECT_ID('dbo.ParkingSubscriptions')
    )
    BEGIN
        CREATE INDEX IX_ParkingSubscriptions_Company_Status_Date
            ON dbo.ParkingSubscriptions(CompanyId, Status, StartDate, EndDate);
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE name = 'IX_ParkingSubscriptions_Status_EndDate'
          AND object_id = OBJECT_ID('dbo.ParkingSubscriptions')
    )
    BEGIN
        CREATE INDEX IX_ParkingSubscriptions_Status_EndDate
            ON dbo.ParkingSubscriptions(Status, EndDate);
    END;
END;

IF OBJECT_ID('dbo.AppModules', 'U') IS NOT NULL
   AND OBJECT_ID('dbo.AppModuleActions', 'U') IS NOT NULL
BEGIN
    DECLARE @PaymentsModuleId int;
    SELECT @PaymentsModuleId = ModuleId
    FROM dbo.AppModules
    WHERE ModuleKey = 'payments';

    IF @PaymentsModuleId IS NOT NULL
       AND NOT EXISTS (
           SELECT 1
           FROM dbo.AppModuleActions
           WHERE ModuleId = @PaymentsModuleId
             AND ActionKey = 'print'
       )
    BEGIN
        INSERT INTO dbo.AppModuleActions (ModuleId, ActionKey, ActionName, SortOrder, Active)
        VALUES (@PaymentsModuleId, 'print', 'Print Payment Receipts', 40, 1);
    END;

    IF @PaymentsModuleId IS NOT NULL
       AND OBJECT_ID('dbo.AppRoles', 'U') IS NOT NULL
       AND OBJECT_ID('dbo.AppRolePermissions', 'U') IS NOT NULL
    BEGIN
        INSERT INTO dbo.AppRolePermissions (RoleId, ModuleId, ActionKey, IsAllowed)
        SELECT r.RoleId, @PaymentsModuleId, 'print', 1
        FROM dbo.AppRoles r
        WHERE r.Active = 1
          AND LOWER(r.RoleKey) IN ('super_admin', 'superadmin', 'admin', 'owner', 'manager', 'gm')
          AND NOT EXISTS (
              SELECT 1
              FROM dbo.AppRolePermissions p
              WHERE p.RoleId = r.RoleId
                AND p.ModuleId = @PaymentsModuleId
                AND p.ActionKey = 'print'
          );
    END;
END;

COMMIT TRANSACTION;
