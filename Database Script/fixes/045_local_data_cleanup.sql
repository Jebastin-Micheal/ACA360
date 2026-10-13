/* =============================================================================
   045_local_data_cleanup.sql

   LOCAL DATA ONLY (db_ACA360 on the developer machine). Decided 2026-10-01. Every
   change is keyed on the exact local row (id + e-mail + role), so on any other
   database nothing matches and nothing happens. Not part of deploy_combined.sql.

   1. Duplicate logins: one login per person is kept, the others are DELETED
      (with what signing in left behind: sessions, preferences, layouts,
      shortcuts, calendar events, assignments), and so is a staff record that
      only the deleted logins used.
        keep 36 jamesmicheal007@gmail.com (SuperAdmin)
          delete 20 (Account Manager) and 35 (no role)
        keep 34 james123@gmail.com (Broker)
          delete 33 (Account Manager, already switched off by 037)
        keep 44 ajaykannan2808@gmail.com (Admin)
          delete 42 (Data Analyst)
   2. email@gmail.com's Employer login (38) is DELETED with its EmployerAccounts
      profile (row 2): it reached Pinnacle Retail (employer 2) only because the
      profile's id was 2 (see 044). Its ACA Director login (39) stays.
   3. Atlantic Health's 2024 record (1015) had EIN 45-3387297; the 2023, 2025
      and 2026 records have 45-3387291. Corrected.

   Stops without changing anything if an uploaded file still names one of these
   logins. Prints what it changed. Safe to re-run (nothing matches the second time).
   ============================================================================= */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

DECLARE @Remove TABLE (User_ID BIGINT PRIMARY KEY, [User_Name] NVARCHAR(256), Role_ID INT NULL, Ref_ID BIGINT NULL, Why NVARCHAR(200));

INSERT @Remove (User_ID, [User_Name], Role_ID, Ref_ID, Why)
SELECT u.User_ID, u.[User_Name], u.Role_ID, u.Ref_ID, x.Why
FROM (VALUES
        (20, 'jamesmicheal007@gmail.com', 16,   'duplicate of 36 (SuperAdmin)'),
        (35, 'jamesmicheal007@gmail.com', NULL, 'duplicate of 36; has no role'),
        (33, 'james123@gmail.com',        16,   'duplicate of 34 (Broker); already switched off'),
        (42, 'ajaykannan2808@gmail.com',  11,   'duplicate of 44 (Admin)'),
        (38, 'email@gmail.com',           13,   'Employer login linked to employer 2 by coincidence (044)')
     ) x (Id, Name, RoleId, Why)
JOIN dbo.tbl_User u
  ON u.User_ID = x.Id AND u.[User_Name] = x.Name
 AND (u.Role_ID = x.RoleId OR (u.Role_ID IS NULL AND x.RoleId IS NULL));

-- An uploaded file that still names one of them: stop, nothing changed.
IF EXISTS (SELECT 1 FROM dbo.UploadedFileLog f
           WHERE f.UploadedByUserId IN (SELECT CAST(User_ID AS VARCHAR(20)) FROM @Remove)
              OR f.AssignedToUserId IN (SELECT CAST(User_ID AS VARCHAR(20)) FROM @Remove))
    THROW 50045, '045: an uploaded file still names one of these logins; nothing was changed.', 1;

-- Staff records only the removed logins use, and the removed Employer login's own profile.
DECLARE @Staff TABLE (Staff_ID BIGINT PRIMARY KEY);
INSERT @Staff (Staff_ID)
SELECT DISTINCT r.Ref_ID
FROM   @Remove r
WHERE  ISNULL(r.Role_ID, 0) NOT IN (10, 13) AND r.Ref_ID IS NOT NULL
  AND  NOT EXISTS (SELECT 1 FROM dbo.tbl_User k
                   WHERE k.Ref_ID = r.Ref_ID AND ISNULL(k.Role_ID, 0) NOT IN (10, 13)
                     AND k.User_ID NOT IN (SELECT User_ID FROM @Remove));

DECLARE @Profiles TABLE (Employer_ID BIGINT PRIMARY KEY);
INSERT @Profiles (Employer_ID)
SELECT ea.Employer_ID
FROM   @Remove r
JOIN   dbo.EmployerAccounts ea ON r.Role_ID = 13 AND ea.Employer_ID = r.Ref_ID AND ea.Employer_Email = r.[User_Name];

-- What signing in and using the app left behind for these logins.
DELETE FROM dbo.tbl_AuthSessions            WHERE Subject IN (SELECT CONCAT(N'user:', User_ID) FROM @Remove);
DELETE FROM dbo.UserPreferences             WHERE UserId IN (SELECT CAST(User_ID AS NVARCHAR(50)) FROM @Remove);
DELETE FROM dbo.UserDashboardLayout         WHERE UserId IN (SELECT User_ID FROM @Remove);
DELETE FROM dbo.tbl_UserShortcuts           WHERE UserId IN (SELECT User_ID FROM @Remove);
DELETE FROM dbo.tbl_CalendarEvents          WHERE UserId IN (SELECT User_ID FROM @Remove);
DELETE FROM dbo.tbl_Portfolio_Assignment    WHERE AM_UserID IN (SELECT User_ID FROM @Remove);
DELETE FROM dbo.tbl_Operational_Assignment  WHERE DA_UserID IN (SELECT User_ID FROM @Remove);
DELETE FROM dbo.EmployerAccountManagers     WHERE AccountManagerUserId IN (SELECT User_ID FROM @Remove);
UPDATE dbo.tbl_User SET ManagerId = NULL    WHERE ManagerId IN (SELECT User_ID FROM @Remove);

DELETE FROM dbo.tbl_User         WHERE User_ID IN (SELECT User_ID FROM @Remove);
DELETE FROM dbo.StaffAccounts    WHERE Staff_ID IN (SELECT Staff_ID FROM @Staff);
DELETE FROM dbo.EmployerAccounts WHERE Employer_ID IN (SELECT Employer_ID FROM @Profiles);

SELECT 'Login deleted' AS [Change], User_ID, [User_Name], Role_ID, Why FROM @Remove;
SELECT 'Staff record deleted' AS [Change], Staff_ID FROM @Staff;
SELECT 'EmployerAccounts profile deleted' AS [Change], Employer_ID FROM @Profiles;

-- 3. Atlantic Health 2024 EIN.
UPDATE dbo.Employer
SET    taxid = '45-3387291'
OUTPUT 'EIN corrected' AS [Change], inserted.id, deleted.taxid AS OldEIN, inserted.taxid AS NewEIN
WHERE  id = 1015 AND taxid = '45-3387297' AND [name] = 'ATLANTIC HEALTH PARTNERS INC';

COMMIT TRANSACTION;
GO
