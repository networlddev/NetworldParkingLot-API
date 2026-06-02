/*
Creates system-wide audit history tables and a History permission module.
Preview/apply manually after code deployment. This script does not modify live data rows.
*/

IF OBJECT_ID('dbo.SystemActivityLogs', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SystemActivityLogs
    (
        SystemActivityLogId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SystemActivityLogs PRIMARY KEY,
        ActivityDate DATETIME2(7) NOT NULL CONSTRAINT DF_SystemActivityLogs_ActivityDate DEFAULT SYSUTCDATETIME(),
        UserId INT NULL,
        Username NVARCHAR(100) NOT NULL CONSTRAINT DF_SystemActivityLogs_Username DEFAULT N'-',
        ModuleKey NVARCHAR(100) NOT NULL,
        ActionKey NVARCHAR(80) NOT NULL,
        Result NVARCHAR(20) NOT NULL,
        EntityType NVARCHAR(100) NOT NULL,
        EntityId NVARCHAR(100) NULL,
        Title NVARCHAR(250) NOT NULL,
        Message NVARCHAR(1000) NULL,
        IpAddress NVARCHAR(80) NULL,
        UserAgent NVARCHAR(500) NULL,
        RequestPath NVARCHAR(300) NULL
    );

    CREATE INDEX IX_SystemActivityLogs_ActivityDate_ModuleKey
        ON dbo.SystemActivityLogs (ActivityDate DESC, ModuleKey);

    CREATE INDEX IX_SystemActivityLogs_UserId_ActivityDate
        ON dbo.SystemActivityLogs (UserId, ActivityDate DESC);
END

IF OBJECT_ID('dbo.SystemActivityLogDetails', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SystemActivityLogDetails
    (
        SystemActivityLogDetailId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SystemActivityLogDetails PRIMARY KEY,
        SystemActivityLogId BIGINT NOT NULL,
        FieldName NVARCHAR(150) NOT NULL,
        OldValue NVARCHAR(2000) NULL,
        NewValue NVARCHAR(2000) NULL,
        CONSTRAINT FK_SystemActivityLogDetails_SystemActivityLogs
            FOREIGN KEY (SystemActivityLogId)
            REFERENCES dbo.SystemActivityLogs (SystemActivityLogId)
            ON DELETE CASCADE
    );

    CREATE INDEX IX_SystemActivityLogDetails_SystemActivityLogId
        ON dbo.SystemActivityLogDetails (SystemActivityLogId);
END

DECLARE @HistoryModuleId INT;

IF NOT EXISTS (SELECT 1 FROM dbo.AppModules WHERE ModuleKey = 'history')
BEGIN
    INSERT INTO dbo.AppModules (ModuleKey, ModuleName, SortOrder, Active)
    VALUES ('history', 'History / Audit Logs', 130, 1);
END

SELECT @HistoryModuleId = ModuleId FROM dbo.AppModules WHERE ModuleKey = 'history';

IF @HistoryModuleId IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.AppModuleActions WHERE ModuleId = @HistoryModuleId AND ActionKey = 'view')
BEGIN
    INSERT INTO dbo.AppModuleActions (ModuleId, ActionKey, ActionName, SortOrder, Active)
    VALUES (@HistoryModuleId, 'view', 'View History', 10, 1);
END

IF @HistoryModuleId IS NOT NULL
BEGIN
    INSERT INTO dbo.AppRolePermissions (RoleId, ModuleId, ActionKey, IsAllowed)
    SELECT r.RoleId, @HistoryModuleId, 'view', 1
    FROM dbo.AppRoles r
    WHERE r.RoleKey IN ('super_admin', 'admin')
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.AppRolePermissions rp
          WHERE rp.RoleId = r.RoleId
            AND rp.ModuleId = @HistoryModuleId
            AND rp.ActionKey = 'view'
      );
END
