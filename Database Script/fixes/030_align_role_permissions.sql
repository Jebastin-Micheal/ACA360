/* =============================================================================
   030_align_role_permissions.sql

   Grants the menus and actions the client's role sheets list (ACA 360 Coding-Testing
   Tracking Document.xlsx: Director / DA Supervisor / AM Supervisor / DA / AM Role
   Testing) to those five roles. Goes with the web change that makes every
   User Accounts action check its permission (before, any logged-in role could open
   User Accounts and add or edit users).

   ADDITIVE ONLY. Nothing is revoked. Grants a role has beyond its sheet are
   listed by the last query for review. Disabled rows for a granted menu/action
   are re-enabled rather than duplicated. A backup of tbl_AccessPermissions is
   taken on the first run (tbl_AccessPermissions_Backup_030).

   How the sheet is read
     - "View" on a page also grants Index: every page's landing action checks
       Index, so View alone would show the menu and then deny the page.
     - "Broker Dashboard" / "Employer Dashboard" = Employer/Dashboard, which checks
       Employer:Index, so those are folded into the Employers menu.
     - "Generate Reports" = the Reports menu and its four report pages.
     - "Files: Upload" / "Download Hub: Download" also get Index + View to open them.
     - "?" and "N/A" grant nothing. Master Settings and ACA Calculation are "?".
     - User Accounts: the menu sheets give AM Supervisor and Account Manager full
       rights, but their dashboard spec says "User Management (Reset Passwords
       Only)". They get Index + View + Edit; the web app treats Edit without Add as
       password-reset-only. ACA Director gets full rights, as the sheet says.
       (Only Admin/SuperAdmin can touch Admin/SuperAdmin accounts — enforced in code.)
     - Every role gets Dashboard:Index so the Dashboard link shows (DA Supervisor,
       AM Supervisor, StaffAC and StaffAU had no row, so no Dashboard menu item).

   Parent menus
     A child menu only renders when its parent (and grandparent) has a row for the
     role, so each ancestor of a granted menu gets View. The Administration
     container's Controller was 'UserAccounts' (script 015), which turned that
     container row into a UserAccounts:View permission for every role that can see
     System Settings or Master Settings. It becomes 'Administration' (a label, not a
     page) so a container row grants nothing.

   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.tbl_AccessPermissions_Backup_030', N'U') IS NULL
    SELECT * INTO dbo.tbl_AccessPermissions_Backup_030 FROM dbo.tbl_AccessPermissions;
GO

BEGIN TRANSACTION;

-- The menu ids below are from the ACA360 menu seed. Stop if any of them points to
-- a different page in this database, rather than granting the wrong screen.
DECLARE @Expect TABLE (Id INT PRIMARY KEY, Controller NVARCHAR(100));
INSERT INTO @Expect VALUES
    (2,'Employee'),(3,'Plan'),(5,'Dashboard'),(8,'SelectEmployer'),(9,'Employer'),(15,'Home'),(16,'Home'),
    (17,'FileUpload'),(18,'Role'),(19,'UserAccounts'),(21,'Rules'),(1021,'System'),(1023,'Filing'),
    (1025,'Downloads'),(1026,'Distribution'),(1028,'Tracker'),(1029,'Service'),(1030,'Process'),
    (1031,'Partner'),(1032,'TrackerEmployer'),(1039,'Report'),(1040,'eFileReport'),
    (1041,'MailFulfillmentReport'),(1047,'AMReport'),(1048,'DataAnalystReport');

IF EXISTS (SELECT 1 FROM @Expect e LEFT JOIN dbo.tbl_Menus m ON m.Id = e.Id
           WHERE m.Id IS NULL OR LTRIM(RTRIM(m.Controller)) <> e.Controller)
BEGIN
    SELECT e.Id, e.Controller AS Expected, m.Controller AS Found, m.MenuName
    FROM   @Expect e LEFT JOIN dbo.tbl_Menus m ON m.Id = e.Id
    WHERE  m.Id IS NULL OR LTRIM(RTRIM(m.Controller)) <> e.Controller;
    THROW 50030, 'Menu ids do not match this database. Nothing changed.', 1;
END

-- Administration container: a label, not a page.
UPDATE dbo.tbl_Menus
SET    Controller = 'Administration', DateModified = GETDATE()
WHERE  MenuName = 'Administration' AND ParentMenuId IS NULL AND Controller = 'UserAccounts';

DECLARE @All VARCHAR(100) = 'Index,View,Add,Edit,Delete,Download,Upload';

-- Role, menu, actions (from the role sheets; see header for how they are read)
DECLARE @Spec TABLE (RoleName NVARCHAR(60), MenuId INT, Actions VARCHAR(100));
INSERT INTO @Spec (RoleName, MenuId, Actions) VALUES
    -- ACA Director
    ('ACA Director',    5, 'Index'),
    ('ACA Director',    9, 'Index,View,Edit,Delete,Download,Upload'),
    ('ACA Director',   21, @All),
    ('ACA Director', 1029, 'Index,View'),
    ('ACA Director', 1030, @All),
    ('ACA Director',    8, 'Index,View'),
    ('ACA Director', 1031, @All),
    ('ACA Director', 1032, @All),
    ('ACA Director',    3, @All),
    ('ACA Director',    2, @All),
    ('ACA Director',   17, 'Index,View,Upload'),
    ('ACA Director', 1028, @All),
    ('ACA Director', 1039, @All), ('ACA Director', 1040, @All), ('ACA Director', 1041, @All),
    ('ACA Director', 1047, @All), ('ACA Director', 1048, @All),
    ('ACA Director', 1025, 'Index,View,Download'),
    ('ACA Director', 1023, @All),
    ('ACA Director', 1026, @All),
    ('ACA Director',   15, @All),
    ('ACA Director',   16, @All),
    ('ACA Director',   19, @All),
    ('ACA Director', 1021, @All),
    ('ACA Director',   18, @All),

    -- DA Supervisor
    ('DA Supervisor',    5, 'Index'),
    ('DA Supervisor',    9, 'Index,View'),
    ('DA Supervisor',   21, 'Index,View'),
    ('DA Supervisor', 1030, @All),
    ('DA Supervisor',    8, 'Index,View'),
    ('DA Supervisor', 1031, 'Index,View'),
    ('DA Supervisor', 1032, 'Index,View'),
    ('DA Supervisor',    3, 'Index,View'),
    ('DA Supervisor',    2, 'Index,View,Edit'),
    ('DA Supervisor',   17, 'Index,View,Upload'),
    ('DA Supervisor', 1028, @All),
    ('DA Supervisor', 1039, @All), ('DA Supervisor', 1040, @All), ('DA Supervisor', 1041, @All),
    ('DA Supervisor', 1047, @All), ('DA Supervisor', 1048, @All),
    ('DA Supervisor', 1025, 'Index,View,Download'),
    ('DA Supervisor',   15, 'Index,View'),
    ('DA Supervisor',   16, 'Index,View'),

    -- Account Manager Supervisor
    ('Account Manager Supervisor',    5, 'Index'),
    ('Account Manager Supervisor',    9, 'Index,View,Edit,Download,Upload'),
    ('Account Manager Supervisor', 1030, @All),
    ('Account Manager Supervisor',    8, 'Index,View'),
    ('Account Manager Supervisor', 1031, @All),
    ('Account Manager Supervisor', 1032, @All),
    ('Account Manager Supervisor',    3, @All),
    ('Account Manager Supervisor',    2, @All),
    ('Account Manager Supervisor',   17, 'Index,View,Upload'),
    ('Account Manager Supervisor', 1028, @All),
    ('Account Manager Supervisor', 1039, @All), ('Account Manager Supervisor', 1040, @All),
    ('Account Manager Supervisor', 1041, @All), ('Account Manager Supervisor', 1047, @All),
    ('Account Manager Supervisor', 1048, @All),
    ('Account Manager Supervisor', 1025, 'Index,View,Download'),
    ('Account Manager Supervisor', 1023, @All),
    ('Account Manager Supervisor',   15, 'Index,View'),
    ('Account Manager Supervisor',   16, 'Index,View'),
    ('Account Manager Supervisor',   19, 'Index,View,Edit'),

    -- Data Analyst
    ('Data Analyst',    5, 'Index'),
    ('Data Analyst',    9, 'Index,View'),
    ('Data Analyst',   21, 'Index,View'),
    ('Data Analyst', 1030, @All),
    ('Data Analyst',    8, 'Index,View'),
    ('Data Analyst', 1031, 'Index,View'),
    ('Data Analyst', 1032, 'Index,View'),
    ('Data Analyst',    3, 'Index,View'),
    ('Data Analyst',    2, 'Index,View,Edit'),
    ('Data Analyst',   17, 'Index,View,Upload'),
    ('Data Analyst', 1028, @All),
    ('Data Analyst', 1039, @All), ('Data Analyst', 1040, @All), ('Data Analyst', 1041, @All),
    ('Data Analyst', 1047, @All), ('Data Analyst', 1048, @All),
    ('Data Analyst', 1025, 'Index,View,Download'),
    ('Data Analyst',   15, 'Index,View'),
    ('Data Analyst',   16, 'Index,View'),

    -- Account Manager
    ('Account Manager',    5, 'Index'),
    ('Account Manager',    9, 'Index,View,Edit,Download,Upload'),
    ('Account Manager', 1030, @All),
    ('Account Manager',    8, 'Index,View'),
    ('Account Manager', 1031, @All),
    ('Account Manager', 1032, @All),
    ('Account Manager',    3, @All),
    ('Account Manager',    2, @All),
    ('Account Manager',   17, 'Index,View,Upload'),
    ('Account Manager', 1028, @All),
    ('Account Manager', 1039, @All), ('Account Manager', 1040, @All), ('Account Manager', 1041, @All),
    ('Account Manager', 1047, @All), ('Account Manager', 1048, @All),
    ('Account Manager', 1025, 'Index,View,Download'),
    ('Account Manager', 1023, @All),
    ('Account Manager',   15, 'Index,View'),
    ('Account Manager',   16, 'Index,View'),
    ('Account Manager',   19, 'Index,View,Edit'),

    -- Home page for the roles that had no Dashboard menu row
    ('StaffAC', 5, 'Index'),
    ('StaffAU', 5, 'Index');

-- Expand to (RoleId, MenuId, ActionID)
DECLARE @Want TABLE (RoleId INT, MenuId INT, ActionID INT, PRIMARY KEY (RoleId, MenuId, ActionID));
INSERT INTO @Want (RoleId, MenuId, ActionID)
SELECT DISTINCT r.ID, s.MenuId, at.Id
FROM   @Spec s
JOIN   dbo.tbl_Roles r  ON r.RoleName = s.RoleName
JOIN   dbo.tbl_Menus m  ON m.Id = s.MenuId
CROSS  APPLY STRING_SPLIT(s.Actions, ',') a
JOIN   dbo.tbl_ActionTypes at ON at.ActionName = LTRIM(RTRIM(a.value));

-- Ancestors of every granted menu get View, or the child never renders.
;WITH Up AS (
    SELECT w.RoleId, m.ParentMenuId AS MenuId
    FROM   (SELECT DISTINCT RoleId, MenuId FROM @Want) w
    JOIN   dbo.tbl_Menus m ON m.Id = w.MenuId
    WHERE  m.ParentMenuId IS NOT NULL
    UNION ALL
    SELECT u.RoleId, m.ParentMenuId
    FROM   Up u
    JOIN   dbo.tbl_Menus m ON m.Id = u.MenuId
    WHERE  m.ParentMenuId IS NOT NULL
)
INSERT INTO @Want (RoleId, MenuId, ActionID)
SELECT DISTINCT u.RoleId, u.MenuId, 1
FROM   Up u
WHERE  NOT EXISTS (SELECT 1 FROM @Want w WHERE w.RoleId = u.RoleId AND w.MenuId = u.MenuId AND w.ActionID = 1);

-- Re-enable disabled rows that the sheet grants
UPDATE ap
SET    IsEnabled = 1, Date_Modified = GETDATE(), Modified_by = 'script 030'
FROM   dbo.tbl_AccessPermissions ap
JOIN   @Want w ON w.RoleId = ap.RoleId AND w.MenuId = ap.MenuId AND w.ActionID = ap.ActionID
WHERE  ISNULL(ap.IsEnabled, 0) = 0;
PRINT CONCAT('Re-enabled: ', @@ROWCOUNT);

-- Add the missing ones
INSERT INTO dbo.tbl_AccessPermissions (RoleId, MenuId, ActionID, IsEnabled, Date_Added, Added_by)
SELECT w.RoleId, w.MenuId, w.ActionID, 1, GETDATE(), 'script 030'
FROM   @Want w
WHERE  NOT EXISTS (SELECT 1 FROM dbo.tbl_AccessPermissions ap
                   WHERE ap.RoleId = w.RoleId AND ap.MenuId = w.MenuId AND ap.ActionID = w.ActionID);
PRINT CONCAT('Added: ', @@ROWCOUNT);

COMMIT TRANSACTION;

-- For review: enabled grants these five roles hold that their sheet does not list
-- (nothing was revoked). Parent-menu View rows are expected and not listed.
SELECT r.RoleName, m.MenuName, m.Controller, STRING_AGG(at.ActionName, ',') AS ExtraActions
FROM   dbo.tbl_AccessPermissions ap
JOIN   dbo.tbl_Roles r ON r.ID = ap.RoleId
JOIN   dbo.tbl_Menus m ON m.Id = ap.MenuId
JOIN   dbo.tbl_ActionTypes at ON at.Id = ap.ActionID
WHERE  ap.IsEnabled = 1
  AND  r.RoleName IN ('ACA Director', 'DA Supervisor', 'Account Manager Supervisor', 'Data Analyst', 'Account Manager')
  AND  NOT EXISTS (
        SELECT 1 FROM @Spec s
        CROSS APPLY STRING_SPLIT(s.Actions, ',') a
        WHERE s.RoleName = r.RoleName AND s.MenuId = ap.MenuId AND LTRIM(RTRIM(a.value)) = at.ActionName)
  AND  NOT (ap.ActionID = 1 AND EXISTS (SELECT 1 FROM dbo.tbl_Menus c WHERE c.ParentMenuId = m.Id))
GROUP  BY r.RoleName, m.MenuName, m.Controller
ORDER  BY r.RoleName, m.MenuName;
GO
