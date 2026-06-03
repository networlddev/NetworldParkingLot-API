/*
Adds/repairs dashboard and reports permissions used by the dashboard/reports UI.
Also verifies the AppUserPermissions key used for per-user Default/Allow/Deny overrides.

This script is incremental and must be applied manually. It does not update business data.
*/

DECLARE @DashboardModuleId INT;
DECLARE @ReportsModuleId INT;

IF NOT EXISTS (SELECT 1 FROM dbo.AppModules WHERE ModuleKey = 'dashboard')
BEGIN
    INSERT INTO dbo.AppModules (ModuleKey, ModuleName, SortOrder, Active)
    VALUES ('dashboard', 'Dashboard', 10, 1);
END

IF NOT EXISTS (SELECT 1 FROM dbo.AppModules WHERE ModuleKey = 'reports')
BEGIN
    INSERT INTO dbo.AppModules (ModuleKey, ModuleName, SortOrder, Active)
    VALUES ('reports', 'Reports', 120, 1);
END

SELECT @DashboardModuleId = ModuleId FROM dbo.AppModules WHERE ModuleKey = 'dashboard';
SELECT @ReportsModuleId = ModuleId FROM dbo.AppModules WHERE ModuleKey = 'reports';

IF @DashboardModuleId IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.AppModuleActions WHERE ModuleId = @DashboardModuleId AND ActionKey = 'view')
BEGIN
    INSERT INTO dbo.AppModuleActions (ModuleId, ActionKey, ActionName, SortOrder, Active)
    VALUES (@DashboardModuleId, 'view', 'View Dashboard', 10, 1);
END

IF @ReportsModuleId IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.AppModuleActions WHERE ModuleId = @ReportsModuleId AND ActionKey = 'view')
BEGIN
    INSERT INTO dbo.AppModuleActions (ModuleId, ActionKey, ActionName, SortOrder, Active)
    VALUES (@ReportsModuleId, 'view', 'View Reports', 10, 1);
END

IF @ReportsModuleId IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.AppModuleActions WHERE ModuleId = @ReportsModuleId AND ActionKey = 'export')
BEGIN
    INSERT INTO dbo.AppModuleActions (ModuleId, ActionKey, ActionName, SortOrder, Active)
    VALUES (@ReportsModuleId, 'export', 'Export Reports', 20, 1);
END

IF @DashboardModuleId IS NOT NULL
BEGIN
    INSERT INTO dbo.AppRolePermissions (RoleId, ModuleId, ActionKey, IsAllowed)
    SELECT r.RoleId, @DashboardModuleId, 'view', 1
    FROM dbo.AppRoles r
    WHERE r.RoleKey IN ('super_admin', 'admin', 'manager', 'gm', 'viewer')
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.AppRolePermissions rp
          WHERE rp.RoleId = r.RoleId
            AND rp.ModuleId = @DashboardModuleId
            AND rp.ActionKey = 'view'
      );
END

IF @ReportsModuleId IS NOT NULL
BEGIN
    INSERT INTO dbo.AppRolePermissions (RoleId, ModuleId, ActionKey, IsAllowed)
    SELECT r.RoleId, @ReportsModuleId, a.ActionKey, 1
    FROM dbo.AppRoles r
    CROSS JOIN (VALUES ('view'), ('export')) a(ActionKey)
    WHERE r.RoleKey IN ('super_admin', 'admin', 'manager', 'gm')
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.AppRolePermissions rp
          WHERE rp.RoleId = r.RoleId
            AND rp.ModuleId = @ReportsModuleId
            AND rp.ActionKey = a.ActionKey
      );

    INSERT INTO dbo.AppRolePermissions (RoleId, ModuleId, ActionKey, IsAllowed)
    SELECT r.RoleId, @ReportsModuleId, 'view', 1
    FROM dbo.AppRoles r
    WHERE r.RoleKey = 'viewer'
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.AppRolePermissions rp
          WHERE rp.RoleId = r.RoleId
            AND rp.ModuleId = @ReportsModuleId
            AND rp.ActionKey = 'view'
      );
END

IF OBJECT_ID('dbo.AppUserPermissions', 'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1
       FROM sys.indexes
       WHERE name = 'IX_AppUserPermissions_User_Module_Action'
         AND object_id = OBJECT_ID('dbo.AppUserPermissions')
   )
BEGIN
    CREATE INDEX IX_AppUserPermissions_User_Module_Action
        ON dbo.AppUserPermissions (UserId, ModuleId, ActionId)
        INCLUDE (IsAllowed);
END
