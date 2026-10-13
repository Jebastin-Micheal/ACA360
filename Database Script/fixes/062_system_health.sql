/* =============================================================================
   062 - Administration > System Health

   One page for SuperAdmin and Admin that says whether the app is well:
   database size and last backup, errors in the last day and week, slow pages
   (from the slow-request log), e-mail sending, files stuck part-way through
   processing, and the background jobs (read by the app from Hangfire).

   - dbo.sp_SystemHealth: the database figures, in five result sets.
   - Menu item Administration > System Health (SystemHealthController) with the
     Index and View permissions for SuperAdmin and Admin.
   - tbl_ErrorLogs: lines written as information (the slow-request log) were
     saved with Level 'Error' (LoggerService.LogInfo never set the level), so
     they counted as errors; they are now 'Info'.

   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

UPDATE dbo.tbl_ErrorLogs SET Level = 'Info' WHERE Type = 'Info' AND Level = 'Error';
PRINT CONCAT('  ', @@ROWCOUNT, ' information lines no longer counted as errors');
GO

CREATE OR ALTER PROCEDURE dbo.sp_SystemHealth
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Now DATETIME = GETDATE(), @UtcNow DATETIME = GETUTCDATE();

    -- 1. Database: size and last backup (msdb when readable, else the app's own backup list).
    DECLARE @LastBackup DATETIME = NULL, @BackupSource NVARCHAR(50) = NULL;
    BEGIN TRY
        SELECT @LastBackup = MAX(backup_finish_date) FROM msdb.dbo.backupset WHERE database_name = DB_NAME() AND type = 'D';
        IF @LastBackup IS NOT NULL SET @BackupSource = N'SQL Server';
    END TRY
    BEGIN CATCH
        SET @LastBackup = NULL;
    END CATCH
    IF @LastBackup IS NULL
        SELECT @LastBackup = MAX(CreatedOn), @BackupSource = CASE WHEN MAX(CreatedOn) IS NULL THEN NULL ELSE N'App backups' END
        FROM dbo.tbl_DBBackupHistory;

    SELECT DB_NAME() AS DatabaseName,
           CAST(SUM(CASE WHEN type = 0 THEN size END) * 8 / 1024.0 AS DECIMAL(12,1)) AS DataMb,
           CAST(SUM(CASE WHEN type = 1 THEN size END) * 8 / 1024.0 AS DECIMAL(12,1)) AS LogMb,
           @LastBackup AS LastBackup, @BackupSource AS BackupSource
    FROM   sys.database_files;

    -- 2. Errors: counts, and the most frequent in the last week (the slow-request log is not an error).
    SELECT SUM(CASE WHEN LoggedOnDate >= DATEADD(DAY, -1, @Now) THEN 1 ELSE 0 END) AS LastDay,
           COUNT(*) AS LastWeek
    FROM   dbo.tbl_ErrorLogs
    WHERE  Level = 'Error' AND LoggedOnDate >= DATEADD(DAY, -7, @Now);

    SELECT TOP (8) CallSite, LEFT(Message, 300) AS Message, COUNT(*) AS Times, MAX(LoggedOnDate) AS Last
    FROM   dbo.tbl_ErrorLogs
    WHERE  Level = 'Error' AND LoggedOnDate >= DATEADD(DAY, -7, @Now)
    GROUP  BY CallSite, LEFT(Message, 300)
    ORDER  BY COUNT(*) DESC, MAX(LoggedOnDate) DESC;

    -- 3. Slow requests in the last week (grouped by page in the app).
    SELECT TOP (5000) Message, AdditionalInfo, LoggedOnDate
    FROM   dbo.tbl_ErrorLogs
    WHERE  CallSite = 'SlowRequestLogMiddleware' AND LoggedOnDate >= DATEADD(DAY, -7, @Now)
    ORDER  BY LoggedOnDate DESC;

    -- 4. E-mail in the last week, by status.
    SELECT ISNULL(NULLIF(LTRIM(RTRIM(Status)), ''), 'Unknown') AS Status, COUNT(*) AS Emails, MAX(SentDate) AS Last
    FROM   dbo.CommunicationLog
    WHERE  SentDate >= DATEADD(DAY, -7, @Now)
    GROUP  BY ISNULL(NULLIF(LTRIM(RTRIM(Status)), ''), 'Unknown')
    ORDER  BY COUNT(*) DESC;

    -- 5. Files stuck part-way: still pre-processing, validating or importing an hour after their last step.
    SELECT TOP (20) F.FileLogId, F.OriginalFileName, F.EmployerName,
           CAST(F.ValidationStatusId AS INT) AS ValidationStatusId, CAST(F.WorkflowStatusId AS INT) AS WorkflowStatusId,
           COALESCE(H.LastStep, F.UploadedAt) AS Since
    FROM   dbo.UploadedFileLog F
    OUTER  APPLY (SELECT MAX(Timestamp) AS LastStep FROM dbo.FileLifecycleHistory X WHERE X.FileLogId = F.FileLogId) H
    WHERE  ISNULL(F.IsDeleted, 0) = 0
      AND  (F.WorkflowStatusId IN (200, 251) OR F.ValidationStatusId IN (20, 30))
      AND  COALESCE(H.LastStep, F.UploadedAt) < DATEADD(HOUR, -1, @Now)
    ORDER  BY COALESCE(H.LastStep, F.UploadedAt);
END
GO

------------------------------------------------------------
-- Menu: Administration > System Health, for SuperAdmin and Admin.
------------------------------------------------------------
DECLARE @Admin INT = (SELECT TOP 1 Id FROM dbo.tbl_Menus WHERE Controller = 'Administration' AND ParentMenuId IS NULL AND Flag = 0 ORDER BY Id);
IF @Admin IS NULL THROW 50062, '062: the Administration menu container was not found.', 1;

DECLARE @Menu INT = (SELECT TOP 1 Id FROM dbo.tbl_Menus WHERE Controller = 'SystemHealth' ORDER BY Id);
IF @Menu IS NULL
BEGIN
    INSERT INTO dbo.tbl_Menus (ParentMenuId, MenuName, Controller, Icon, DisplayOrder, IsActive, DateModified, Flag, IsVisible)
    VALUES (@Admin, 'System Health', 'SystemHealth', 'bx bx-pulse',
            ISNULL((SELECT MAX(DisplayOrder) FROM dbo.tbl_Menus WHERE ParentMenuId = @Admin AND Flag = 0), 0) + 1,
            1, GETDATE(), 0, 1);
    SET @Menu = SCOPE_IDENTITY();
    PRINT '  menu item added';
END
ELSE
    UPDATE dbo.tbl_Menus SET ParentMenuId = @Admin, IsActive = 1, Flag = 0, IsVisible = 1, DateModified = GETDATE()
    WHERE Id = @Menu AND (ParentMenuId <> @Admin OR IsActive <> 1 OR Flag <> 0 OR IsVisible <> 1);

INSERT INTO dbo.tbl_AccessPermissions (RoleId, MenuId, ActionID, IsEnabled, Date_Added, Added_by)
SELECT r.ID, @Menu, a.Id, 1, GETDATE(), 'script 062'
FROM   dbo.tbl_Roles r
CROSS  JOIN dbo.tbl_ActionTypes a
WHERE  r.RoleName IN ('SuperAdmin', 'Admin') AND a.ActionName IN ('Index', 'View')
  AND  NOT EXISTS (SELECT 1 FROM dbo.tbl_AccessPermissions p WHERE p.RoleId = r.ID AND p.MenuId = @Menu AND p.ActionID = a.Id);

UPDATE p SET IsEnabled = 1, Date_Modified = GETDATE(), Modified_by = 'script 062'
FROM   dbo.tbl_AccessPermissions p
JOIN   dbo.tbl_ActionTypes a ON a.Id = p.ActionID
JOIN   dbo.tbl_Roles r ON r.ID = p.RoleId
WHERE  p.MenuId = @Menu AND r.RoleName IN ('SuperAdmin', 'Admin') AND a.ActionName IN ('Index', 'View') AND ISNULL(p.IsEnabled, 0) = 0;

PRINT '062: Administration > System Health (SuperAdmin, Admin).';
GO
