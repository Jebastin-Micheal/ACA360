/* =============================================================================
   070 - Notification centre (idea #21)

   In-app notifications only (no e-mail). Each user chooses which events
   they get, on the Notifications page.

   1. tbl_UserNotifications.EventKey: lets a reminder be sent once only.
   2. NotificationPreferences: a user's choice per event (no row = on).
   3. sp_Notify_FileEvent: the uploader and the assigned analyst hear when
      their file is ready to import, failed, or was imported; the analyst
      hears when a file is assigned to them. Never the person who did it.
   4. sp_UpdateFileLogStatus calls it after writing the file's history.
      Everything else is the database's current procedure, unchanged. A
      notification that fails never stops the status change.
   5. sp_Notify_Daily (a daily background job): filing deadlines 14 and 7
      days ahead for staff; new errors on System Health for admins.
   6. sp_Notifications_Recent, the preference procedures, and
      sp_MarkNotificationAsRead / sp_MarkNotificationAsUnread taking the
      user, so nobody can mark someone else's notification.

   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

IF COL_LENGTH('dbo.tbl_UserNotifications', 'EventKey') IS NULL
    ALTER TABLE dbo.tbl_UserNotifications ADD EventKey VARCHAR(120) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.tbl_UserNotifications') AND name = 'IX_UserNotifications_User')
    CREATE INDEX IX_UserNotifications_User ON dbo.tbl_UserNotifications (UserId, IsRead, CreatedDate DESC);
GO
-- Not a unique filtered index: those need QUOTED_IDENTIFIER ON in every procedure that writes
-- here, and older ones were created without it. The reminder procedure checks before it inserts.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.tbl_UserNotifications') AND name = 'IX_UserNotifications_EventKey')
    CREATE INDEX IX_UserNotifications_EventKey ON dbo.tbl_UserNotifications (UserId, EventKey);
GO

IF OBJECT_ID('dbo.NotificationPreferences') IS NULL
    CREATE TABLE dbo.NotificationPreferences (
        UserId     INT          NOT NULL,
        EventCode  VARCHAR(40)  NOT NULL,   -- FileReady, FileFailed, FileAssigned, ImportDone, Deadline, HealthErrors
        IsEnabled  BIT          NOT NULL,
        ModifiedAt DATETIME2(0) NOT NULL CONSTRAINT DF_NotificationPreferences_ModifiedAt DEFAULT SYSDATETIME(),
        CONSTRAINT PK_NotificationPreferences PRIMARY KEY (UserId, EventCode)
    );
GO

/* Who may get an event: an active user, of a role the event is for, who has not turned it off. */
CREATE OR ALTER FUNCTION dbo.fn_NotificationWanted (@UserId INT, @EventCode VARCHAR(40))
RETURNS TABLE
AS
RETURN
    SELECT CAST(1 AS BIT) AS Wanted
    FROM   dbo.tbl_User U
    JOIN   dbo.tbl_Roles R ON R.ID = U.Role_ID
    WHERE  U.User_ID = @UserId
      AND  ISNULL(U.Flag, 1) = 1
      AND  (@EventCode <> 'HealthErrors' OR R.RoleName IN ('SuperAdmin', 'Admin'))
      AND  (@EventCode <> 'Deadline' OR R.RoleName NOT IN ('Employer', 'Broker'))
      AND  NOT EXISTS (SELECT 1 FROM dbo.NotificationPreferences P
                       WHERE P.UserId = @UserId AND P.EventCode = @EventCode AND P.IsEnabled = 0);
GO

CREATE OR ALTER PROCEDURE dbo.sp_Notify_FileEvent
    @FileLogId   INT,
    @Action      NVARCHAR(100),
    @PerformedBy NVARCHAR(450) = NULL,
    @Message     NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Event VARCHAR(40) =
        CASE WHEN @Action IN ('Validation Passed', 'Validation Completed with Warnings') THEN 'FileReady'
             WHEN @Action IN ('Validation Failed', 'System Error', 'DA Rejected', 'Import Abandoned') THEN 'FileFailed'
             WHEN @Action = 'File Assigned' THEN 'FileAssigned'
             WHEN @Action = 'Import Complete' THEN 'ImportDone' END;
    IF @Event IS NULL RETURN;

    DECLARE @File NVARCHAR(260), @Employer NVARCHAR(255), @Uploader VARCHAR(50), @Assignee VARCHAR(50);
    SELECT @File = OriginalFileName, @Employer = EmployerName,
           @Uploader = NULLIF(UploadedByUserId, ''), @Assignee = NULLIF(AssignedToUserId, '')
    FROM   dbo.UploadedFileLog WHERE FileLogId = @FileLogId;
    IF @File IS NULL RETURN;

    DECLARE @Title VARCHAR(255) =
        CASE @Event WHEN 'FileReady'    THEN CASE WHEN @Action = 'Validation Passed' THEN 'File ready to import' ELSE 'File ready, with warnings' END
                    WHEN 'FileFailed'   THEN CASE WHEN @Action = 'DA Rejected' THEN 'File rejected'
                                                  WHEN @Action = 'Import Abandoned' THEN 'Import did not finish'
                                                  ELSE 'File needs attention' END
                    WHEN 'FileAssigned' THEN 'File assigned to you'
                    ELSE 'Import complete' END;
    DECLARE @Text NVARCHAR(MAX) = @File + ISNULL(N' (' + @Employer + N')', N'')
                                + CASE WHEN NULLIF(@Message, N'') IS NOT NULL AND @Event <> 'FileAssigned' THEN N': ' + @Message ELSE N'.' END;
    DECLARE @Type VARCHAR(50) = CASE @Event WHEN 'FileFailed' THEN 'danger'
                                            WHEN 'FileReady' THEN CASE WHEN @Action = 'Validation Passed' THEN 'success' ELSE 'warning' END
                                            WHEN 'ImportDone' THEN 'success' ELSE 'info' END;
    DECLARE @Link VARCHAR(255) = CASE WHEN @Event IN ('FileReady', 'FileFailed') AND @Action <> 'Import Abandoned'
                                      THEN '/ErrorTriage/ErrorTriageDashboard/' + CAST(@FileLogId AS VARCHAR(20))
                                      ELSE '/FileUpload?searchTerm=' + LEFT(REPLACE(REPLACE(REPLACE(@File, '%', '%25'), '&', '%26'), ' ', '%20'), 200) END;

    INSERT INTO dbo.tbl_UserNotifications (UserId, Title, Message, Type, LinkUrl, CreatedDate, IsRead)
    SELECT DISTINCT TRY_CAST(R.UserId AS INT), @Title, @Text, @Type, @Link, GETUTCDATE(), 0
    FROM   (VALUES (CASE WHEN @Event <> 'FileAssigned' THEN @Uploader END), (@Assignee)) R (UserId)
    CROSS  APPLY dbo.fn_NotificationWanted(TRY_CAST(R.UserId AS INT), @Event) W
    WHERE  TRY_CAST(R.UserId AS INT) > 0
      AND  ISNULL(R.UserId, '') <> ISNULL(@PerformedBy, '');
END
GO

CREATE OR ALTER PROCEDURE [dbo].[sp_UpdateFileLogStatus]  
    @FileLogId INT,  
    @ValidationStatusId TINYINT = NULL,  
    @WorkflowStatusId INT = NULL,
    @UserId NVARCHAR(450) = NULL,      -- NEW: Who did it?
    @Action NVARCHAR(100) = 'System Update', -- NEW: What did they do?
    @Message NVARCHAR(500) = NULL      -- NEW: Why? (e.g. "Found 20 errors")
AS  
BEGIN  
    SET NOCOUNT ON;  
    
    BEGIN TRANSACTION;

    BEGIN TRY
        -- 1. CAPTURE OLD STATUS (For History)
        DECLARE @OldStatus INT;
        SELECT @OldStatus = WorkflowStatusId FROM dbo.UploadedFileLog WHERE FileLogId = @FileLogId;

        -- 2. UPDATE THE FILE (Current State)
        UPDATE dbo.UploadedFileLog  
        SET  
            ValidationStatusId = COALESCE(@ValidationStatusId, ValidationStatusId),  
            WorkflowStatusId = COALESCE(@WorkflowStatusId, WorkflowStatusId),  
            AnalysisDate = CASE  
                                WHEN @ValidationStatusId IS NOT NULL THEN GETUTCDATE()  
                                ELSE AnalysisDate  
                           END,
            ModifiedOn = GETUTCDATE()
        WHERE  
            FileLogId = @FileLogId;  

        -- 3. INSERT HISTORY (Flight Tracker)
        INSERT INTO dbo.FileLifecycleHistory
        (
            FileLogId, 
            PreviousStatusId, 
            NewStatusId, 
            ActionName, 
            PerformedByUserId, 
            PerformedByUserName, 
            Timestamp, 
            Message
        )
        SELECT 
            @FileLogId,
            @OldStatus,
            COALESCE(@WorkflowStatusId, @OldStatus), -- New Status
            @Action,
            @UserId,
            u.User_Name, -- Fetch UserName automatically if UserID exists
            GETUTCDATE(),
            @Message
        FROM (SELECT 1 AS Dummy) d
        LEFT JOIN tbl_User u ON u.User_ID = @UserId; -- Join to Identity table if exists

        -- 070: tell the uploader / assigned analyst. A notification never stops the status change.
        BEGIN TRY
            EXEC dbo.sp_Notify_FileEvent @FileLogId = @FileLogId, @Action = @Action, @PerformedBy = @UserId, @Message = @Message;
        END TRY
        BEGIN CATCH
            IF XACT_STATE() = -1 THROW;
        END CATCH

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_Notify_Daily
    @Today DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET @Today = ISNULL(@Today, CAST(GETDATE() AS DATE));

    /* Filing deadlines: once when 14 days or fewer are left, once more at 7. A missed day still
       sends the reminder late rather than never; the EventKey keeps each one to a single send. */
    ;WITH Due AS (
        SELECT D.FilingDeadlineId, D.Title, D.Jurisdiction, D.DueDate, D.NeedsCheck,
               DATEDIFF(DAY, @Today, D.DueDate) AS DaysLeft,
               CASE WHEN DATEDIFF(DAY, @Today, D.DueDate) <= 7 THEN 7 ELSE 14 END AS Stage
        FROM   dbo.FilingDeadlines D
        WHERE  ISNULL(D.IsActive, 1) = 1
          AND  DATEDIFF(DAY, @Today, D.DueDate) BETWEEN 0 AND 14
    )
    INSERT INTO dbo.tbl_UserNotifications (UserId, Title, Message, Type, LinkUrl, CreatedDate, IsRead, EventKey)
    SELECT U.User_ID,
           CASE WHEN D.DaysLeft = 0 THEN 'Deadline today' ELSE 'Deadline in ' + CAST(D.DaysLeft AS VARCHAR(5)) + ' day' + CASE WHEN D.DaysLeft = 1 THEN '' ELSE 's' END END,
           D.Title + N' (' + D.Jurisdiction + N') is due ' + FORMAT(D.DueDate, 'MMMM d, yyyy')
               + CASE WHEN D.NeedsCheck = 1 THEN N'. The date is still to be confirmed.' ELSE N'.' END,
           CASE WHEN D.Stage = 7 THEN 'danger' ELSE 'warning' END,
           '/Calendar', GETUTCDATE(), 0,
           'Deadline:' + CAST(D.FilingDeadlineId AS VARCHAR(20)) + ':' + CAST(D.Stage AS VARCHAR(5))
    FROM   Due D
    CROSS  JOIN dbo.tbl_User U
    CROSS  APPLY dbo.fn_NotificationWanted(U.User_ID, 'Deadline') W
    WHERE  NOT EXISTS (SELECT 1 FROM dbo.tbl_UserNotifications N
                       WHERE N.UserId = U.User_ID
                         AND N.EventKey = 'Deadline:' + CAST(D.FilingDeadlineId AS VARCHAR(20)) + ':' + CAST(D.Stage AS VARCHAR(5)));
    DECLARE @Deadlines INT = @@ROWCOUNT;

    /* System Health: admins hear about new errors once a day. */
    DECLARE @Errors INT = (SELECT COUNT(*) FROM dbo.tbl_ErrorLogs
                           WHERE Level = 'Error' AND LoggedOnDate >= DATEADD(DAY, -1, CAST(@Today AS DATETIME))
                             AND LoggedOnDate < DATEADD(DAY, 1, CAST(@Today AS DATETIME)));
    DECLARE @Key VARCHAR(120) = 'Health:' + CONVERT(VARCHAR(10), @Today, 23);
    IF @Errors > 0
        INSERT INTO dbo.tbl_UserNotifications (UserId, Title, Message, Type, LinkUrl, CreatedDate, IsRead, EventKey)
        SELECT U.User_ID, 'Errors on System Health',
               CAST(@Errors AS VARCHAR(10)) + ' error' + CASE WHEN @Errors = 1 THEN ' was' ELSE 's were' END + ' logged in the last day.',
               'warning', '/SystemHealth', GETUTCDATE(), 0, @Key
        FROM   dbo.tbl_User U
        CROSS  APPLY dbo.fn_NotificationWanted(U.User_ID, 'HealthErrors') W
        WHERE  NOT EXISTS (SELECT 1 FROM dbo.tbl_UserNotifications N WHERE N.UserId = U.User_ID AND N.EventKey = @Key);

    SELECT @Deadlines AS DeadlineNotifications, @@ROWCOUNT AS HealthNotifications;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_Notifications_Recent
    @UserId INT,
    @Top    INT = 8
AS
BEGIN
    SET NOCOUNT ON;
    SELECT COUNT(*) AS Unread FROM dbo.tbl_UserNotifications WHERE UserId = @UserId AND IsRead = 0;
    SELECT TOP (@Top) NotificationId, UserId, Title, Message, Type, IsRead, CreatedDate, LinkUrl
    FROM   dbo.tbl_UserNotifications
    WHERE  UserId = @UserId
    ORDER  BY IsRead, CreatedDate DESC, NotificationId DESC;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_NotificationPreferences_Get
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT E.EventCode,
           ISNULL(P.IsEnabled, CAST(1 AS BIT)) AS IsEnabled,
           CAST(CASE WHEN E.EventCode = 'HealthErrors'
                      AND NOT EXISTS (SELECT 1 FROM dbo.tbl_User U JOIN dbo.tbl_Roles R ON R.ID = U.Role_ID
                                      WHERE U.User_ID = @UserId AND R.RoleName IN ('SuperAdmin', 'Admin')) THEN 0
                     WHEN E.EventCode = 'Deadline'
                      AND EXISTS (SELECT 1 FROM dbo.tbl_User U JOIN dbo.tbl_Roles R ON R.ID = U.Role_ID
                                  WHERE U.User_ID = @UserId AND R.RoleName IN ('Employer', 'Broker')) THEN 0
                     ELSE 1 END AS BIT) AS IsAvailable
    FROM   (VALUES ('FileReady', 1), ('FileFailed', 2), ('FileAssigned', 3), ('ImportDone', 4), ('Deadline', 5), ('HealthErrors', 6)) E (EventCode, SortOrder)
    LEFT   JOIN dbo.NotificationPreferences P ON P.UserId = @UserId AND P.EventCode = E.EventCode
    ORDER  BY E.SortOrder;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_NotificationPreferences_Save
    @UserId    INT,
    @EventCode VARCHAR(40),
    @IsEnabled BIT
AS
BEGIN
    SET NOCOUNT ON;
    IF @EventCode NOT IN ('FileReady', 'FileFailed', 'FileAssigned', 'ImportDone', 'Deadline', 'HealthErrors') RETURN;
    MERGE dbo.NotificationPreferences AS T
    USING (SELECT @UserId AS UserId, @EventCode AS EventCode) AS S
    ON T.UserId = S.UserId AND T.EventCode = S.EventCode
    WHEN MATCHED THEN UPDATE SET IsEnabled = @IsEnabled, ModifiedAt = SYSDATETIME()
    WHEN NOT MATCHED THEN INSERT (UserId, EventCode, IsEnabled) VALUES (@UserId, @EventCode, @IsEnabled);
END
GO

/* Reading or un-reading a notification now needs its owner. @UserId stays optional so a caller
   that does not pass it keeps working, but the app always passes it. */
CREATE OR ALTER PROCEDURE dbo.sp_MarkNotificationAsRead
    @Id     INT,
    @UserId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.tbl_UserNotifications SET IsRead = 1
    WHERE  NotificationId = @Id AND (@UserId IS NULL OR UserId = @UserId);
    SELECT @@ROWCOUNT AS Changed;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_MarkNotificationAsUnread
    @Id     INT,
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.tbl_UserNotifications SET IsRead = 0 WHERE NotificationId = @Id AND UserId = @UserId;
    SELECT @@ROWCOUNT AS Changed;
END
GO

PRINT '070: notification centre (file events, deadlines, health; preferences; owner checks).';
GO
