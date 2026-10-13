/* =============================================================================
   069 - Employer timeline (idea #18)

   sp_EmployerTimeline: one list, newest first, of what happened to an
   employer for its tax year: files (uploaded, validated, assigned, imported,
   rejected...), code recalculations, forms generated, filing locked or
   unlocked, notes, edits to the employer, account manager and data analyst
   assignments, and the filing deadlines still ahead (state ones only where the employer
   has employees living in that state).

   Reads only. Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.EmployeeCodeAuditTrail') AND name = 'IX_EmployeeCodeAuditTrail_Employer')
    CREATE INDEX IX_EmployeeCodeAuditTrail_Employer ON dbo.EmployeeCodeAuditTrail (EmployerId, FilingYear, LineNumber)
        INCLUDE (ChangedOn, EmployeeId, ChangedByUserName, IsSystemGenerated);
GO

CREATE OR ALTER PROCEDURE dbo.sp_EmployerTimeline
    @EmployerId BIGINT,
    @Top        INT = 300
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Year INT, @Ein VARCHAR(20), @Rec VARCHAR(50) = CAST(@EmployerId AS VARCHAR(50));
    -- File times are kept in UTC; everything else here is server local time.
    DECLARE @UtcOffset INT = DATEDIFF(MINUTE, GETUTCDATE(), GETDATE());
    SELECT @Year = filingYear, @Ein = REPLACE(ISNULL(taxid, ''), '-', '') FROM dbo.Employer WHERE id = @EmployerId;
    IF @Year IS NULL RETURN;

    DECLARE @Files TABLE (FileLogId INT PRIMARY KEY, FileName NVARCHAR(260), UploadedAt DATETIME2, UploadedBy VARCHAR(50));
    INSERT INTO @Files
    SELECT L.FileLogId, L.OriginalFileName, L.UploadedAt, L.UploadedByUserId
    FROM   dbo.UploadedFileLog L
    WHERE  REPLACE(ISNULL(L.TaxID, ''), '-', '') = @Ein AND L.PlanYear = @Year AND ISNULL(L.IsDeleted, 0) = 0;

    ;WITH Events AS (
        -- Files: the upload, then each step that means something to a person
        SELECT DATEADD(MINUTE, @UtcOffset, F.UploadedAt) AS EventAt, 'File' AS Kind, N'File uploaded' AS Title,
               F.FileName AS Detail, U.User_Name AS Who, F.FileLogId
        FROM   @Files F
        LEFT   JOIN dbo.tbl_User U ON CAST(U.User_ID AS VARCHAR(50)) = F.UploadedBy
        UNION ALL
        SELECT DATEADD(MINUTE, @UtcOffset, H.Timestamp), 'File', CAST(H.ActionName AS NVARCHAR(200)),
               F.FileName + CASE WHEN NULLIF(H.Message, '') IS NOT NULL THEN N' - ' + H.Message ELSE N'' END,
               H.PerformedByUserName, F.FileLogId
        FROM   dbo.FileLifecycleHistory H
        JOIN   @Files F ON F.FileLogId = H.FileLogId
        WHERE  H.ActionName NOT IN ('Pre-Processing Started', 'Structure Check Started', 'Processing Started',
                                    'Import Started', 'Import Queued')
        UNION ALL
        -- Codes: one line per recalculation (each employee gets a marker row)
        SELECT MIN(T.ChangedOn), 'Codes',
               CASE WHEN MAX(CAST(T.IsSystemGenerated AS INT)) = 1 THEN N'Codes recalculated' ELSE N'Codes changed by hand' END,
               CAST(COUNT(DISTINCT T.EmployeeId) AS NVARCHAR(20)) + N' employee' + CASE WHEN COUNT(DISTINCT T.EmployeeId) = 1 THEN N'' ELSE N's' END,
               MAX(T.ChangedByUserName), NULL
        FROM   dbo.EmployeeCodeAuditTrail T
        WHERE  T.EmployerId = @EmployerId AND T.FilingYear = @Year AND T.LineNumber = 0 AND T.FieldName = 'All Codes'
        GROUP  BY CONVERT(VARCHAR(16), T.ChangedOn, 126)
        UNION ALL
        -- Codes locked or unlocked (script 074), one line per action
        SELECT MIN(T.ChangedOn), 'Codes',
               CASE WHEN MAX(T.NewValue) = '1' THEN N'Codes locked' ELSE N'Codes unlocked' END,
               CAST(COUNT(DISTINCT T.EmployeeId) AS NVARCHAR(20)) + N' employee' + CASE WHEN COUNT(DISTINCT T.EmployeeId) = 1 THEN N'' ELSE N's' END,
               MAX(T.ChangedByUserName), NULL
        FROM   dbo.EmployeeCodeAuditTrail T
        WHERE  T.EmployerId = @EmployerId AND T.FilingYear = @Year AND T.FieldName = 'IsLocked'
        GROUP  BY CONVERT(VARCHAR(16), T.ChangedOn, 126), T.NewValue
        UNION ALL
        SELECT B.GeneratedDate, 'Forms', N'Forms generated (' + ISNULL(B.FormType, '') + N')',
               B.FileName, COALESCE(GU.User_Name, B.GeneratedBy), NULL
        FROM   dbo.GenerateMultiEmployerBatchHistoryEmployers BE
        JOIN   dbo.GenerateMultiEmployerBatchHistory B ON B.Id = BE.BatchHistoryId
        LEFT   JOIN dbo.tbl_User GU ON CAST(GU.User_ID AS VARCHAR(50)) = B.GeneratedBy
        WHERE  BE.EmployerId = @EmployerId AND BE.TaxYear = @Year
        UNION ALL
        SELECT A.EventDate, 'Filing', N'Filing ' + LOWER(A.EventType), A.EventDescription,
               COALESCE(A.PerformedByUserName, U.User_Name), NULL
        FROM   dbo.FilingAuditLog A
        LEFT   JOIN dbo.tbl_User U ON CAST(U.User_ID AS VARCHAR(50)) = A.PerformedByUserId
        WHERE  A.EmployerId = @EmployerId AND A.TaxYear = @Year
        UNION ALL
        SELECT N.CreatedOn, 'Note', CASE WHEN N.IsInternal = 1 THEN N'Internal note' ELSE N'Note' END,
               LEFT(N.NoteMessage, 300), U.User_Name, NULL
        FROM   dbo.tbl_SystemNotes N
        LEFT   JOIN dbo.tbl_User U ON U.User_ID = N.UserID
        WHERE  N.LinkedEntityType = 'Employer' AND N.LinkedEntityID = @EmployerId AND ISNULL(N.IsDeleted, 0) = 0
        UNION ALL
        SELECT D.Created_at, 'Edit',
               CASE WHEN D.Action_Type = 'INSERT' THEN N'Employer added' ELSE N'Employer details changed' END,
               CASE WHEN D.Table_Name = 'Tracker_EmployerExtension' THEN N'Service details' ELSE N'Employer details' END,
               D.User_Name, NULL
        FROM   dbo.DataAuditlog D
        WHERE  D.Record_Id = @Rec AND D.Table_Name IN ('Employer', 'Tracker_EmployerExtension')
        UNION ALL
        SELECT A.StartDate, 'Assignment', N'Account manager assigned', U.User_Name, NULL, NULL
        FROM   dbo.tbl_AM_Assignment_History A
        LEFT   JOIN dbo.tbl_User U ON U.User_ID = A.AM_UserID
        WHERE  A.EmployerID = @EmployerId
        UNION ALL
        SELECT A.AssignedDate, 'Assignment', N'Data analyst assigned', U.User_Name, NULL, NULL
        FROM   dbo.tbl_DA_Assignment_History A
        LEFT   JOIN dbo.tbl_User U ON U.User_ID = A.DA_UserID
        WHERE  A.EmployerID = @EmployerId
        UNION ALL
        SELECT CAST(D.DueDate AS DATETIME2), 'Deadline', CAST(D.Title AS NVARCHAR(200)),
               D.Jurisdiction + CASE WHEN D.NeedsCheck = 1 THEN N' (date to be confirmed)' ELSE N'' END, NULL, NULL
        FROM   dbo.FilingDeadlines D
        WHERE  D.FilingYear = @Year AND D.DueDate >= CAST(GETDATE() AS DATE)
          AND  ISNULL(D.IsActive, 1) = 1
          -- a state date only matters when the employer has employees living there
          AND (D.Jurisdiction = 'IRS' OR EXISTS (SELECT 1 FROM dbo.Employee E WHERE E.EmployerId = @EmployerId
                                                  AND ISNULL(E.IsDeleted, 0) = 0 AND UPPER(LTRIM(RTRIM(E.state))) = D.Jurisdiction))
    )
    SELECT TOP (@Top) EventAt, Kind, Title, Detail, Who, FileLogId,
           CAST(CASE WHEN EventAt > SYSDATETIME() THEN 1 ELSE 0 END AS BIT) AS IsUpcoming
    FROM   Events
    WHERE  EventAt IS NOT NULL
    ORDER  BY EventAt DESC;
END
GO

PRINT '069: employer timeline (sp_EmployerTimeline).';
GO
