/* =============================================================================
   046_local_delete_duplicate_superadmin.sql

   LOCAL DATA ONLY (db_ACA360 on the developer machine). Decided 2026-10-01.
   Keyed on the exact local row (id + e-mail, compared case-sensitively + role), so
   on any other database nothing matches and nothing happens. Not part of
   deploy_combined.sql.

   TestACA360@gmail.com (login 43, staff 19) and Testaca360@gmail.com (login 10043,
   staff 10019) are the same address in different capitals, both SuperAdmin.
   Login 43 is DELETED with its staff record; 10043 stays. Nothing else refers to
   login 43 or staff 19 (checked: sessions, preferences, layouts, shortcuts,
   calendar, assignments, files, inbox, notifications, notes, audit logs).

   Safe to re-run (nothing matches the second time).
   ============================================================================= */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

DECLARE @Remove TABLE (User_ID BIGINT PRIMARY KEY, [User_Name] NVARCHAR(256), Ref_ID BIGINT NULL);

INSERT @Remove (User_ID, [User_Name], Ref_ID)
SELECT u.User_ID, u.[User_Name], u.Ref_ID
FROM   dbo.tbl_User u
WHERE  u.User_ID = 43
  AND  u.[User_Name] COLLATE Latin1_General_CS_AS = N'TestACA360@gmail.com'
  AND  u.Role_ID = 2
  AND  EXISTS (SELECT 1 FROM dbo.tbl_User k
               WHERE k.User_ID = 10043 AND k.[User_Name] COLLATE Latin1_General_CS_AS = N'Testaca360@gmail.com');

IF EXISTS (SELECT 1 FROM dbo.UploadedFileLog f
           WHERE f.UploadedByUserId IN (SELECT CAST(User_ID AS VARCHAR(20)) FROM @Remove)
              OR f.AssignedToUserId IN (SELECT CAST(User_ID AS VARCHAR(20)) FROM @Remove))
    THROW 50046, '046: an uploaded file still names this login; nothing was changed.', 1;

-- Its staff record, unless another staff login uses it.
DECLARE @Staff TABLE (Staff_ID BIGINT PRIMARY KEY);
INSERT @Staff (Staff_ID)
SELECT r.Ref_ID
FROM   @Remove r
WHERE  r.Ref_ID IS NOT NULL
  AND  NOT EXISTS (SELECT 1 FROM dbo.tbl_User k
                   WHERE k.Ref_ID = r.Ref_ID AND ISNULL(k.Role_ID, 0) NOT IN (10, 13)
                     AND k.User_ID NOT IN (SELECT User_ID FROM @Remove));

DELETE FROM dbo.tbl_AuthSessions            WHERE Subject IN (SELECT CONCAT(N'user:', User_ID) FROM @Remove);
DELETE FROM dbo.UserPreferences             WHERE UserId IN (SELECT CAST(User_ID AS NVARCHAR(50)) FROM @Remove);
DELETE FROM dbo.UserDashboardLayout         WHERE UserId IN (SELECT User_ID FROM @Remove);
DELETE FROM dbo.tbl_UserShortcuts           WHERE UserId IN (SELECT User_ID FROM @Remove);
DELETE FROM dbo.tbl_CalendarEvents          WHERE UserId IN (SELECT User_ID FROM @Remove);
DELETE FROM dbo.tbl_Portfolio_Assignment    WHERE AM_UserID IN (SELECT User_ID FROM @Remove);
DELETE FROM dbo.tbl_Operational_Assignment  WHERE DA_UserID IN (SELECT User_ID FROM @Remove);
DELETE FROM dbo.EmployerAccountManagers     WHERE AccountManagerUserId IN (SELECT User_ID FROM @Remove);
UPDATE dbo.tbl_User SET ManagerId = NULL    WHERE ManagerId IN (SELECT User_ID FROM @Remove);

DELETE FROM dbo.tbl_User      WHERE User_ID IN (SELECT User_ID FROM @Remove);
DELETE FROM dbo.StaffAccounts WHERE Staff_ID IN (SELECT Staff_ID FROM @Staff);

SELECT 'Login deleted' AS [Change], User_ID, [User_Name] FROM @Remove;
SELECT 'Staff record deleted' AS [Change], Staff_ID FROM @Staff;

COMMIT TRANSACTION;
GO
