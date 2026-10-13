/* =============================================================================
   022_seed_consent_portal_permissions.sql

   SEEDS PERMISSION ROWS ONLY. Makes no C# change and, on its own, changes
   nothing about who can reach Consent, PortalAdmin, or EmployeeAcaTracking --
   those three controllers still gate access with [Authorize(Roles=...)] today,
   not [AuthorizePermission]. This script exists so that switch can be made
   later without a lockout, which is exactly why code-review finding #2 was
   skipped rather than applied: [AuthorizePermission] fails closed on a missing
   claim, so flipping the attribute before these rows exist would deny access
   to every user, including SuperAdmin.

   ---------------------------------------------------------------------------
   HOW THE PERMISSION CLAIM SYSTEM WORKS  (read from sp_Login_access)

   At login, the 2nd result set of sp_Login_access runs:

       SELECT m.Controller, at.ActionName AS Action, ...
       FROM   tbl_User u
       JOIN   tbl_AccessPermissions ap ON ap.RoleId = u.Role_ID AND ap.IsEnabled = 1
       JOIN   tbl_Menus m  ON m.Id = ap.MenuId AND m.IsActive = 1 AND m.Flag = 0
       JOIN   tbl_ActionTypes at ON at.Id = ap.ActionID

   AccountController.cs turns every row into a claim: new Claim("Permission",
   $"{Controller}:{Action}"). [AuthorizePermission("X")] on a controller action
   then checks for a "{ControllerName}:X" claim. Note IsVisible is NOT checked
   here -- unlike the main-menu query (sp_GetUserMenu), a menu row can grant
   permissions without ever appearing in the left nav. That is deliberate below:
   these three features are reached by deep link (the consent icon on the
   employee list, the portal-activate action, the employee's own tracking
   dashboard), not through the main menu, so the new rows are created with
   IsVisible = 0.

   THE ACTION VOCABULARY IS DELIBERATELY COARSE. Grepping the whole codebase,
   every existing [AuthorizePermission(...)] call site uses one of only three
   values -- "View", "Edit", "Index" -- out of the seven rows that exist in
   tbl_ActionTypes (View, Add, Edit, Delete, Download, Upload, Index). Nothing
   in this app grants permission per literal C# method name. This script
   follows that convention rather than inventing a new one, mapping each
   controller action to the nearest fit:

       ConsentController           Controller = 'Consent'
           Confirmation   (GET,  read the consent page)        -> View
           Receipt        (GET,  PDF download)                 -> Download
           EmailReceipt   (POST, records an email request)      -> Edit
           WithdrawConsent(POST, reverts consent to paper)       -> Edit

       PortalAdminController       Controller = 'PortalAdmin'
           Activate       (GET, but writes EmployeePortalUser)  -> Edit

       EmployeeAcaTrackingController  Controller = 'EmployeeAcaTracking'
           Index                  (GET, dashboard)              -> Index
           SendConsentReminder    (POST, sends reminders)       -> Edit
           DownloadConsentReport  (GET, exports a report)       -> Download

   WHEN YOU FLIP THE C# ATTRIBUTE, use exactly these values -- e.g.
   [AuthorizePermission("View")] on Confirmation, [AuthorizePermission("Edit")]
   on WithdrawConsent -- or the claim this script grants will not match what
   the attribute asks for, and access will be denied despite this script
   having run cleanly.

   ---------------------------------------------------------------------------
   WHO GETS ACCESS

   Copied exactly from each controller's current [Authorize(Roles = ...)], so
   flipping the attribute later changes HOW access is checked, not WHO has it:

       Consent              SuperAdmin, Admin, ACA Director, Employer
       PortalAdmin          SuperAdmin, Admin, ACA Director, Account Manager
                             (== UserRoles.PortalActivators)
       EmployeeAcaTracking  Employer

   SAFE TO RE-RUN: yes. Every insert is guarded by an existence check; running
   this twice changes nothing the second time.
   ============================================================================= */

USE [db_ACA360];
GO

SET NOCOUNT ON;
GO

DECLARE @ActionView     INT = (SELECT Id FROM dbo.tbl_ActionTypes WHERE ActionName = 'View');
DECLARE @ActionEdit     INT = (SELECT Id FROM dbo.tbl_ActionTypes WHERE ActionName = 'Edit');
DECLARE @ActionDownload INT = (SELECT Id FROM dbo.tbl_ActionTypes WHERE ActionName = 'Download');
DECLARE @ActionIndex    INT = (SELECT Id FROM dbo.tbl_ActionTypes WHERE ActionName = 'Index');

IF @ActionView IS NULL OR @ActionEdit IS NULL OR @ActionDownload IS NULL OR @ActionIndex IS NULL
BEGIN
    RAISERROR('One of the four expected tbl_ActionTypes rows (View/Edit/Download/Index) is missing. Stopping rather than seeding against an unexpected action vocabulary.', 16, 1);
    RETURN;
END

DECLARE @RoleSuperAdmin     INT = (SELECT ID FROM dbo.tbl_Roles WHERE RoleName = 'SuperAdmin');
DECLARE @RoleAdmin          INT = (SELECT ID FROM dbo.tbl_Roles WHERE RoleName = 'Admin');
DECLARE @RoleACADirector    INT = (SELECT ID FROM dbo.tbl_Roles WHERE RoleName = 'ACA Director');
DECLARE @RoleEmployer       INT = (SELECT ID FROM dbo.tbl_Roles WHERE RoleName = 'Employer');
DECLARE @RoleAccountManager INT = (SELECT ID FROM dbo.tbl_Roles WHERE RoleName = 'Account Manager');

IF @RoleSuperAdmin IS NULL OR @RoleAdmin IS NULL OR @RoleACADirector IS NULL
   OR @RoleEmployer IS NULL OR @RoleAccountManager IS NULL
BEGIN
    RAISERROR('One of the five expected roles (SuperAdmin/Admin/ACA Director/Employer/Account Manager) was not found by name. Role names may have changed -- stopping rather than guessing.', 16, 1);
    RETURN;
END

/* =============================================================================
   PART 1 -- Create the three menu rows (idempotent by Controller name).
   Not shown in the main nav (IsVisible = 0): these are reached by deep link,
   and sp_Login_access's permission query does not require IsVisible = 1.
   ============================================================================= */

DECLARE @MenuConsent INT, @MenuPortalAdmin INT, @MenuAcaTracking INT;

SELECT @MenuConsent = Id FROM dbo.tbl_Menus WHERE Controller = 'Consent';
IF @MenuConsent IS NULL
BEGIN
    INSERT INTO dbo.tbl_Menus (ParentMenuId, MenuName, Controller, Icon, DisplayOrder, IsActive, DateModified, Flag, IsVisible)
    VALUES (NULL, 'Consent', 'Consent', 'bx bx-check-shield', 0, 1, GETDATE(), 0, 0);
    SET @MenuConsent = SCOPE_IDENTITY();
    PRINT '--- Created tbl_Menus row for Consent (Id ' + CAST(@MenuConsent AS VARCHAR(10)) + ').';
END
ELSE
    PRINT '--- tbl_Menus row for Consent already exists (Id ' + CAST(@MenuConsent AS VARCHAR(10)) + '), reusing it.';

SELECT @MenuPortalAdmin = Id FROM dbo.tbl_Menus WHERE Controller = 'PortalAdmin';
IF @MenuPortalAdmin IS NULL
BEGIN
    INSERT INTO dbo.tbl_Menus (ParentMenuId, MenuName, Controller, Icon, DisplayOrder, IsActive, DateModified, Flag, IsVisible)
    VALUES (NULL, 'Portal Admin', 'PortalAdmin', 'bx bx-user-check', 0, 1, GETDATE(), 0, 0);
    SET @MenuPortalAdmin = SCOPE_IDENTITY();
    PRINT '--- Created tbl_Menus row for PortalAdmin (Id ' + CAST(@MenuPortalAdmin AS VARCHAR(10)) + ').';
END
ELSE
    PRINT '--- tbl_Menus row for PortalAdmin already exists (Id ' + CAST(@MenuPortalAdmin AS VARCHAR(10)) + '), reusing it.';

SELECT @MenuAcaTracking = Id FROM dbo.tbl_Menus WHERE Controller = 'EmployeeAcaTracking';
IF @MenuAcaTracking IS NULL
BEGIN
    INSERT INTO dbo.tbl_Menus (ParentMenuId, MenuName, Controller, Icon, DisplayOrder, IsActive, DateModified, Flag, IsVisible)
    VALUES (NULL, 'ACA Tracking', 'EmployeeAcaTracking', 'bx bx-line-chart', 0, 1, GETDATE(), 0, 0);
    SET @MenuAcaTracking = SCOPE_IDENTITY();
    PRINT '--- Created tbl_Menus row for EmployeeAcaTracking (Id ' + CAST(@MenuAcaTracking AS VARCHAR(10)) + ').';
END
ELSE
    PRINT '--- tbl_Menus row for EmployeeAcaTracking already exists (Id ' + CAST(@MenuAcaTracking AS VARCHAR(10)) + '), reusing it.';

/* =============================================================================
   PART 2 -- Grant the (Role, Menu, Action) permission rows.

   Built as a table of (RoleId, MenuId, ActionId) triples, then inserted with a
   NOT EXISTS guard per row -- safe to re-run, and re-enables a row that exists
   but was previously switched off.
   ============================================================================= */

DECLARE @Grants TABLE (RoleId INT, MenuId INT, ActionId INT);

-- Consent: Confirmation=View, Receipt=Download, EmailReceipt+WithdrawConsent=Edit
INSERT INTO @Grants (RoleId, MenuId, ActionId)
SELECT r.RoleId, @MenuConsent, a.ActionId
FROM (VALUES (@RoleSuperAdmin), (@RoleAdmin), (@RoleACADirector), (@RoleEmployer)) r(RoleId)
CROSS JOIN (VALUES (@ActionView), (@ActionDownload), (@ActionEdit)) a(ActionId);

-- PortalAdmin: Activate=Edit
INSERT INTO @Grants (RoleId, MenuId, ActionId)
SELECT r.RoleId, @MenuPortalAdmin, @ActionEdit
FROM (VALUES (@RoleSuperAdmin), (@RoleAdmin), (@RoleACADirector), (@RoleAccountManager)) r(RoleId);

-- EmployeeAcaTracking: Index=Index, SendConsentReminder=Edit, DownloadConsentReport=Download
INSERT INTO @Grants (RoleId, MenuId, ActionId)
SELECT @RoleEmployer, @MenuAcaTracking, a.ActionId
FROM (VALUES (@ActionIndex), (@ActionEdit), (@ActionDownload)) a(ActionId);

INSERT INTO dbo.tbl_AccessPermissions (RoleId, MenuId, ActionID, IsEnabled, Date_Added, Added_by)
SELECT g.RoleId, g.MenuId, g.ActionId, 1, GETDATE(), 'script 022'
FROM   @Grants g
WHERE  NOT EXISTS (
           SELECT 1 FROM dbo.tbl_AccessPermissions p
           WHERE  p.RoleId = g.RoleId AND p.MenuId = g.MenuId AND p.ActionID = g.ActionId);

PRINT '--- Granted ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' new permission row(s).';

UPDATE p
SET    p.IsEnabled = 1, p.Date_Modified = GETDATE(), p.Modified_by = 'script 022'
FROM   dbo.tbl_AccessPermissions p
JOIN   @Grants g ON g.RoleId = p.RoleId AND g.MenuId = p.MenuId AND g.ActionId = p.ActionID
WHERE  ISNULL(p.IsEnabled, 0) = 0;

PRINT '--- Re-enabled ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' previously disabled row(s).';
GO

/* -------------------------------------------------------------- VERIFY ----- */

PRINT '';
PRINT '=== 1. The three menu rows, and whether each is visible in the nav ===';
PRINT '    IsVisible should be 0 for all three -- deep-link features, not menu items.';
SELECT  Id, MenuName, Controller, IsActive, Flag, IsVisible
FROM    dbo.tbl_Menus
WHERE   Controller IN ('Consent', 'PortalAdmin', 'EmployeeAcaTracking');

PRINT '=== 2. Exactly the permission claims each role would receive at next login ===';
PRINT '    Compare this against the ACTION VOCABULARY table in the header comment --';
PRINT '    that is what the [AuthorizePermission("...")] argument must say once the';
PRINT '    C# attribute is flipped.';
SELECT  RoleName   = r.RoleName,
        ClaimValue = m.Controller + ':' + at.ActionName
FROM    dbo.tbl_AccessPermissions ap
JOIN    dbo.tbl_Menus m       ON m.Id = ap.MenuId AND m.IsActive = 1 AND m.Flag = 0
JOIN    dbo.tbl_ActionTypes at ON at.Id = ap.ActionID
JOIN    dbo.tbl_Roles r        ON r.ID = ap.RoleId
WHERE   m.Controller IN ('Consent', 'PortalAdmin', 'EmployeeAcaTracking')
  AND   ap.IsEnabled = 1
ORDER BY m.Controller, r.RoleName, at.ActionName;

PRINT '=== 3. Row counts, for a quick sanity check against the header table ===';
PRINT '    Expect: Consent 4 roles x 3 actions = 12. PortalAdmin 4 roles x 1 action = 4.';
PRINT '    EmployeeAcaTracking 1 role x 3 actions = 3. Total 19.';
SELECT  m.Controller, Grants = COUNT(*)
FROM    dbo.tbl_AccessPermissions ap
JOIN    dbo.tbl_Menus m ON m.Id = ap.MenuId
WHERE   m.Controller IN ('Consent', 'PortalAdmin', 'EmployeeAcaTracking')
  AND   ap.IsEnabled = 1
GROUP BY m.Controller;

PRINT '';
PRINT '=== REMINDER ===';
PRINT '    This script alone changes nothing for any user -- the three controllers';
PRINT '    still check [Authorize(Roles=...)], not these claims. Nothing is live';
PRINT '    until the C# attributes are flipped to [AuthorizePermission] with the';
PRINT '    exact action names in the header comment.';
GO
