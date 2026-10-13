/* =============================================================================
   041_file_visible_to_user.sql

   dbo.fn_FileVisibleToUser(@FileLogId, @UserId): may this user open this uploaded
   file? The same rule sp_SearchFileLogsPaginated uses to decide which files the
   File Upload list shows, so a file can be opened exactly when it is listed:

     - SuperAdmin, Admin, ACA Director, DA Supervisor, AM Supervisor, StaffAU
       (Role_ID 2, 3, 4, 5, 6, 15) see every file;
     - anyone sees a file they uploaded or that is assigned to them;
     - otherwise the file's employer (matched on EIN and plan year, as the list
       does) must be theirs: the Employer login's own employer, an Account
       Manager's portfolio, a Data Analyst's operational assignment, or a
       Broker's assigned employers.

   Before this, the per-file checks in FileUploadWorkflowService disagreed with the
   list: downloads, archive and delete let every Account Manager open any file by
   id, while the status / details checks refused supervisors and employer-scoped
   users files the list showed them. Keep this function and the list procedure's
   "Security Scoping" block in step.

   Read-only (creates a function). Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER FUNCTION dbo.fn_FileVisibleToUser (@FileLogId INT, @UserId INT)
RETURNS TABLE
AS
RETURN
    SELECT CAST(CASE WHEN EXISTS (
        SELECT 1
        FROM dbo.UploadedFileLog log
        JOIN dbo.tbl_User u ON u.User_ID = @UserId
        WHERE log.FileLogId = @FileLogId
          AND (
                u.Role_ID IN (2, 3, 4, 5, 6, 15)
             OR log.UploadedByUserId = CAST(@UserId AS VARCHAR(20))
             OR log.AssignedToUserId = CAST(@UserId AS VARCHAR(20))
             OR EXISTS (
                    SELECT 1
                    FROM dbo.Employer emp
                    WHERE emp.taxid = log.TaxID
                      AND emp.filingYear = log.PlanYear
                      AND (
                            (u.Role_ID = 13 AND emp.id = u.Ref_ID)
                         OR (u.Role_ID = 16 AND EXISTS (SELECT 1 FROM dbo.tbl_Portfolio_Assignment pa
                                                        WHERE pa.AM_UserID = @UserId AND pa.EmployerID = emp.id AND pa.IsActive = 1))
                         OR (u.Role_ID = 11 AND EXISTS (SELECT 1 FROM dbo.tbl_Operational_Assignment oa
                                                        WHERE oa.DA_UserID = @UserId AND oa.EmployerID = emp.id AND oa.IsActive = 1))
                         OR (u.Role_ID = 10 AND EXISTS (SELECT 1 FROM dbo.tbl_Employer_Broker_Assignment ba
                                                        WHERE ba.Broker_UserID = @UserId AND ba.EmployerID = emp.id AND ba.IsActive = 1))
                      )
                )
              )
    ) THEN 1 ELSE 0 END AS BIT) AS IsVisible;
GO

PRINT '041: dbo.fn_FileVisibleToUser created.';
GO
