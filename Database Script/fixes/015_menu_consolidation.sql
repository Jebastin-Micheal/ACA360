/* =============================================================================
   015_menu_consolidation.sql

   Reorganises the navigation. Data only — no code change.

   THE PROBLEM

       Seventeen top-level menu items render in the horizontal bar, competing for
       the same row as the employer chip, year picker, notifications and profile.
       The bar wraps, and when it rewraps the items move under the cursor — which
       is why getting back to the employer menu feels unreliable.

   THE SHAPE THIS PRODUCES        17 top-level items -> 11

       Dashboard
       Employers | Employees | Plan          stay top-level, daily use
       Files
       Forms          NEW   Generate Forms, Distribution Center, Downloads Hub
       eFiling        NEW   Filing Center, IRS AIR Filing / XML Generation
       Tracker              unchanged subtree
       Reports              eFile, Mail Fulfillment, AM Report, DA Report
       Administration NEW   User Accounts, Master Settings, System Settings
       Help                 Contact Us

   WHY GENERATE FORMS IS NOT UNDER REPORTS

       It was proposed there, and it is the one grouping worth arguing about.
       Generating forms is a production action — it is how the 1095-Cs get made.
       Reports is read-only analysis. Filing the action under Reports means
       someone doing their main job hunts for it where they go when they are NOT
       doing their main job. The process flowchart also shows Generate Forms as a
       top-level module with its own subtree.

       Grouping it with Distribution Center and Downloads Hub instead gives one
       coherent story — produce, distribute, retrieve — and still removes two
       items from the bar.

   THE PART THAT CAN BREAK LOGINS, HANDLED HERE

       sp_GetUserMenu INNER JOINs tbl_AccessPermissions, and the renderer walks
       down from the roots. A parent with no permission row for your role is not
       returned, and its children then never render at all.

       So reparenting without granting the parents would make Generate Forms,
       Filing Center and User Accounts VANISH for every role. PART 2 grants each
       new parent to exactly the roles that already hold a permission on one of
       its children, and it runs in the same transaction as the reparenting.

   ALSO FIXED IN PASSING

       - Menu 1049 "Report" has Controller = CHAR(9) + 'Tracker' — a literal tab,
         which cannot route. Its two children move to the real Reports menu and it
         is retired.
       - Duplicate DisplayOrder values: Help and User Accounts are both 13,
         Contact Us and System Settings are both 14, so ordering was undefined.
         Everything is renumbered.

   MENU DEPTH

       Administration > Master Settings > Validation Rules is three levels. The
       renderer already handles that — Tracker > Report > AM Report exists today —
       but three-deep flyouts are fiddly in the horizontal layout. If it feels
       awkward, flatten by giving Master Settings' children ParentMenuId =
       @AdministrationId directly; the rollback notes show how.

   SAFE TO RE-RUN
       Yes. Parents are matched by name, permissions by NOT EXISTS, reparenting is
       idempotent.

   REVERSIBLE
       A rollback block is at the end, commented out.
   ============================================================================= */

USE [db_ACA360];
GO

SET NOCOUNT ON;
GO

/* ---------------------------------------------------------------- PART 0 ----
   Before.
   ---------------------------------------------------------------------------- */
PRINT '--- Top-level menu items BEFORE ---';
SELECT  m.Id, m.MenuName, m.Controller, m.DisplayOrder,
        Roles = (SELECT COUNT(DISTINCT ap.RoleId) FROM dbo.tbl_AccessPermissions ap
                 WHERE ap.MenuId = m.Id AND ap.IsEnabled = 1)
FROM    dbo.tbl_Menus m
WHERE   m.ParentMenuId IS NULL AND m.IsActive = 1 AND m.Flag = 0 AND m.IsVisible = 1
ORDER BY m.DisplayOrder, m.MenuName;

SELECT TopLevelCount_Before = COUNT(*)
FROM   dbo.tbl_Menus
WHERE  ParentMenuId IS NULL AND IsActive = 1 AND Flag = 0 AND IsVisible = 1;
GO

BEGIN TRY
BEGIN TRANSACTION;

/* ---------------------------------------------------------------- PART 1 ----
   The three new containers. Matched by name so a re-run does not duplicate them.
   A container's own Controller is ignored while it has children — the renderer
   emits a toggle — but it is set anyway, because a role that can see the parent
   and none of its children would otherwise get a link to nowhere.
   ---------------------------------------------------------------------------- */
DECLARE @FormsId INT, @EFilingId INT, @AdminId INT, @HelpId INT = 15, @ReportsId INT = 1039;

SELECT @FormsId   = Id FROM dbo.tbl_Menus WHERE MenuName = 'Forms'          AND ParentMenuId IS NULL;
SELECT @EFilingId = Id FROM dbo.tbl_Menus WHERE MenuName = 'eFiling'        AND ParentMenuId IS NULL;
SELECT @AdminId   = Id FROM dbo.tbl_Menus WHERE MenuName = 'Administration' AND ParentMenuId IS NULL;

IF @FormsId IS NULL
BEGIN
    INSERT INTO dbo.tbl_Menus (ParentMenuId, MenuName, Controller, Icon, DisplayOrder, IsActive, DateModified, Flag, IsVisible)
    VALUES (NULL, 'Forms', 'FormGeneration', 'bx bx-file', 6, 1, GETDATE(), 0, 1);
    SET @FormsId = SCOPE_IDENTITY();
END

IF @EFilingId IS NULL
BEGIN
    INSERT INTO dbo.tbl_Menus (ParentMenuId, MenuName, Controller, Icon, DisplayOrder, IsActive, DateModified, Flag, IsVisible)
    VALUES (NULL, 'eFiling', 'Filing', 'bx bx-cloud-upload', 7, 1, GETDATE(), 0, 1);
    SET @EFilingId = SCOPE_IDENTITY();
END

IF @AdminId IS NULL
BEGIN
    INSERT INTO dbo.tbl_Menus (ParentMenuId, MenuName, Controller, Icon, DisplayOrder, IsActive, DateModified, Flag, IsVisible)
    VALUES (NULL, 'Administration', 'UserAccounts', 'bx bx-shield-quarter', 10, 1, GETDATE(), 0, 1);
    SET @AdminId = SCOPE_IDENTITY();
END

PRINT '--- Containers: Forms=' + CAST(@FormsId AS VARCHAR(10))
    + ' eFiling=' + CAST(@EFilingId AS VARCHAR(10))
    + ' Administration=' + CAST(@AdminId AS VARCHAR(10)) + ' ---';

/* ---------------------------------------------------------------- PART 2 ----
   Permissions, BEFORE reparenting. Each container is granted to exactly the roles
   that already hold an enabled permission on one of the menus moving under it, so
   nobody gains access they did not have and nobody loses a menu they did.

   ActionID 1 (View) is enough: sp_GetUserMenu only needs one enabled row. The
   container has no actions of its own, and AuthorizePermissionAttribute reads
   Controller:Action claims that belong to the leaf screens.
   ---------------------------------------------------------------------------- */
DECLARE @Grants TABLE (ContainerId INT, RoleId INT);

INSERT INTO @Grants (ContainerId, RoleId)
SELECT DISTINCT c.ContainerId, ap.RoleId
FROM   (VALUES
            (@FormsId,   12), (@FormsId,   1026), (@FormsId,   1025),   -- Generate Forms, Distribution, Downloads
            (@EFilingId, 1023), (@EFilingId, 1024),                     -- Filing Center, IRS AIR
            (@AdminId,   19), (@AdminId,   20), (@AdminId,   1021),     -- User Accounts, Master, System
            (@HelpId,    16)                                            -- Contact Us
       ) c(ContainerId, ChildId)
JOIN   dbo.tbl_AccessPermissions ap ON ap.MenuId = c.ChildId AND ap.IsEnabled = 1;

INSERT INTO dbo.tbl_AccessPermissions (RoleId, MenuId, ActionID, IsEnabled, Date_Added, Added_by)
SELECT g.RoleId, g.ContainerId, 1, 1, GETDATE(), 'migration-015'
FROM   @Grants g
WHERE  NOT EXISTS (SELECT 1 FROM dbo.tbl_AccessPermissions ap
                   WHERE ap.RoleId = g.RoleId AND ap.MenuId = g.ContainerId AND ap.ActionID = 1);

PRINT '--- Container permissions granted: ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' ---';

/* ---------------------------------------------------------------- PART 3 ----
   Reparent, and renumber within each group.
   ---------------------------------------------------------------------------- */
UPDATE dbo.tbl_Menus SET ParentMenuId = @FormsId,   DisplayOrder = 1, DateModified = GETDATE() WHERE Id = 12;    -- Generate Forms
UPDATE dbo.tbl_Menus SET ParentMenuId = @FormsId,   DisplayOrder = 2, DateModified = GETDATE() WHERE Id = 1026;  -- Distribution Center
UPDATE dbo.tbl_Menus SET ParentMenuId = @FormsId,   DisplayOrder = 3, DateModified = GETDATE() WHERE Id = 1025;  -- Downloads Hub

UPDATE dbo.tbl_Menus SET ParentMenuId = @EFilingId, DisplayOrder = 1, DateModified = GETDATE() WHERE Id = 1023;  -- Filing Center
UPDATE dbo.tbl_Menus SET ParentMenuId = @EFilingId, DisplayOrder = 2, DateModified = GETDATE() WHERE Id = 1024;  -- IRS AIR Filing / XML

UPDATE dbo.tbl_Menus SET ParentMenuId = @AdminId,   DisplayOrder = 1, DateModified = GETDATE() WHERE Id = 19;    -- User Accounts
UPDATE dbo.tbl_Menus SET ParentMenuId = @AdminId,   DisplayOrder = 2, DateModified = GETDATE() WHERE Id = 20;    -- Master Settings
UPDATE dbo.tbl_Menus SET ParentMenuId = @AdminId,   DisplayOrder = 3, DateModified = GETDATE() WHERE Id = 1021;  -- System Settings

UPDATE dbo.tbl_Menus SET ParentMenuId = @HelpId,    DisplayOrder = 1, DateModified = GETDATE() WHERE Id = 16;    -- Contact Us

/* ---------------------------------------------------------------- PART 4 ----
   One Reports menu, not two. AM and DA reports sat under Tracker > Report, whose
   Controller is CHAR(9) + 'Tracker' — a literal tab that cannot route. They move
   to the real Reports menu and the broken node is retired.

   Permissions follow: whoever could see a report under Tracker keeps seeing it,
   which needs a grant on the top-level Reports menu.
   ---------------------------------------------------------------------------- */
INSERT INTO dbo.tbl_AccessPermissions (RoleId, MenuId, ActionID, IsEnabled, Date_Added, Added_by)
SELECT DISTINCT ap.RoleId, @ReportsId, 1, 1, GETDATE(), 'migration-015'
FROM   dbo.tbl_AccessPermissions ap
WHERE  ap.MenuId IN (1047, 1048) AND ap.IsEnabled = 1
  AND  NOT EXISTS (SELECT 1 FROM dbo.tbl_AccessPermissions x
                   WHERE x.RoleId = ap.RoleId AND x.MenuId = @ReportsId AND x.ActionID = 1);

UPDATE dbo.tbl_Menus SET ParentMenuId = @ReportsId, DisplayOrder = 3, DateModified = GETDATE() WHERE Id = 1047;  -- AM Report
UPDATE dbo.tbl_Menus SET ParentMenuId = @ReportsId, DisplayOrder = 4, DateModified = GETDATE() WHERE Id = 1048;  -- DA Report

UPDATE dbo.tbl_Menus SET Flag = 1, IsVisible = 0, DateModified = GETDATE() WHERE Id IN (1049, 1050);

UPDATE dbo.tbl_Menus SET DisplayOrder = 1, DateModified = GETDATE() WHERE Id = 1040;  -- eFile Report
UPDATE dbo.tbl_Menus SET DisplayOrder = 2, DateModified = GETDATE() WHERE Id = 1041;  -- Mail Fulfillment

/* ---------------------------------------------------------------- PART 5 ----
   Renumber the top level. Duplicates made ordering undefined, which is part of why
   the bar felt unstable between page loads.
   ---------------------------------------------------------------------------- */
UPDATE dbo.tbl_Menus SET DisplayOrder = 1,  DateModified = GETDATE() WHERE Id = 5;     -- Dashboard
UPDATE dbo.tbl_Menus SET DisplayOrder = 2,  DateModified = GETDATE() WHERE Id = 9;     -- Employers
UPDATE dbo.tbl_Menus SET DisplayOrder = 3,  DateModified = GETDATE() WHERE Id = 2;     -- Employees
UPDATE dbo.tbl_Menus SET DisplayOrder = 4,  DateModified = GETDATE() WHERE Id = 3;     -- Plan
UPDATE dbo.tbl_Menus SET DisplayOrder = 5,  DateModified = GETDATE() WHERE Id = 17;    -- Files
UPDATE dbo.tbl_Menus SET DisplayOrder = 6,  DateModified = GETDATE() WHERE Id = @FormsId;
UPDATE dbo.tbl_Menus SET DisplayOrder = 7,  DateModified = GETDATE() WHERE Id = @EFilingId;
UPDATE dbo.tbl_Menus SET DisplayOrder = 8,  DateModified = GETDATE() WHERE Id = 1028;  -- Tracker
UPDATE dbo.tbl_Menus SET DisplayOrder = 9,  DateModified = GETDATE() WHERE Id = @ReportsId;
UPDATE dbo.tbl_Menus SET DisplayOrder = 10, DateModified = GETDATE() WHERE Id = @AdminId;
UPDATE dbo.tbl_Menus SET DisplayOrder = 11, DateModified = GETDATE() WHERE Id = @HelpId;

COMMIT TRANSACTION;
PRINT '--- Committed. ---';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT '--- Rolled back. The menu is unchanged. ---';
    THROW;
END CATCH
GO

/* -------------------------------------------------------------- VERIFY ----- */
PRINT '--- Top-level menu items AFTER ---';
SELECT  m.Id, m.MenuName, m.DisplayOrder,
        Children = (SELECT COUNT(*) FROM dbo.tbl_Menus c
                    WHERE c.ParentMenuId = m.Id AND c.IsActive = 1 AND c.Flag = 0 AND c.IsVisible = 1),
        Roles    = (SELECT COUNT(DISTINCT ap.RoleId) FROM dbo.tbl_AccessPermissions ap
                    WHERE ap.MenuId = m.Id AND ap.IsEnabled = 1)
FROM    dbo.tbl_Menus m
WHERE   m.ParentMenuId IS NULL AND m.IsActive = 1 AND m.Flag = 0 AND m.IsVisible = 1
ORDER BY m.DisplayOrder;

SELECT TopLevelCount_After = COUNT(*)
FROM   dbo.tbl_Menus
WHERE  ParentMenuId IS NULL AND IsActive = 1 AND Flag = 0 AND IsVisible = 1;

PRINT '--- ORPHAN CHECK. Any row here is a menu a role can no longer reach. ---';
PRINT '    A child is unreachable when its role can see the child but not the parent.';
SELECT  RoleId   = ap.RoleId,
        RoleName = r.RoleName,
        ChildId  = m.Id,
        ChildMenu = m.MenuName,
        ParentMenu = p.MenuName
FROM    dbo.tbl_AccessPermissions ap
JOIN    dbo.tbl_Menus m ON m.Id = ap.MenuId
JOIN    dbo.tbl_Menus p ON p.Id = m.ParentMenuId
LEFT JOIN dbo.tbl_Roles r ON r.ID = ap.RoleId
WHERE   ap.IsEnabled = 1
  AND   m.IsActive = 1 AND m.Flag = 0 AND m.IsVisible = 1
  AND   p.IsActive = 1 AND p.Flag = 0 AND p.IsVisible = 1
  AND   NOT EXISTS (SELECT 1 FROM dbo.tbl_AccessPermissions pp
                    WHERE pp.RoleId = ap.RoleId AND pp.MenuId = p.Id AND pp.IsEnabled = 1)
ORDER BY ap.RoleId, p.MenuName, m.MenuName;
PRINT '    Empty is the required result. Anything listed needs a permission on the parent.';

PRINT '--- What each role now sees at the top level ---';
PRINT '    Check Broker and Employer especially: they hold the narrowest menus.';
SELECT  RoleName = r.RoleName,
        TopLevelItems = COUNT(DISTINCT m.Id),
        Items = STUFF((SELECT ', ' + m2.MenuName
                       FROM   dbo.tbl_Menus m2
                       JOIN   dbo.tbl_AccessPermissions ap2 ON ap2.MenuId = m2.Id AND ap2.IsEnabled = 1
                       WHERE  ap2.RoleId = r.ID AND m2.ParentMenuId IS NULL
                         AND  m2.IsActive = 1 AND m2.Flag = 0 AND m2.IsVisible = 1
                       GROUP BY m2.MenuName, m2.DisplayOrder
                       ORDER BY MIN(m2.DisplayOrder)
                       FOR XML PATH('')), 1, 2, '')
FROM    dbo.tbl_Roles r
LEFT JOIN dbo.tbl_AccessPermissions ap ON ap.RoleId = r.ID AND ap.IsEnabled = 1
LEFT JOIN dbo.tbl_Menus m ON m.Id = ap.MenuId AND m.ParentMenuId IS NULL
                        AND m.IsActive = 1 AND m.Flag = 0 AND m.IsVisible = 1
WHERE   r.IsActive = 1
GROUP BY r.ID, r.RoleName
ORDER BY r.RoleName;
GO

/* =============================================================================
   ROLLBACK — commented. Restores the previous flat structure.

   BEGIN TRANSACTION;

     DECLARE @F INT, @E INT, @A INT;
     SELECT @F = Id FROM dbo.tbl_Menus WHERE MenuName = 'Forms'          AND ParentMenuId IS NULL;
     SELECT @E = Id FROM dbo.tbl_Menus WHERE MenuName = 'eFiling'        AND ParentMenuId IS NULL;
     SELECT @A = Id FROM dbo.tbl_Menus WHERE MenuName = 'Administration' AND ParentMenuId IS NULL;

     UPDATE dbo.tbl_Menus SET ParentMenuId = NULL, DisplayOrder = 8  WHERE Id = 12;
     UPDATE dbo.tbl_Menus SET ParentMenuId = NULL, DisplayOrder = 12 WHERE Id = 1026;
     UPDATE dbo.tbl_Menus SET ParentMenuId = NULL, DisplayOrder = 9  WHERE Id = 1025;
     UPDATE dbo.tbl_Menus SET ParentMenuId = NULL, DisplayOrder = 10 WHERE Id = 1023;
     UPDATE dbo.tbl_Menus SET ParentMenuId = NULL, DisplayOrder = 11 WHERE Id = 1024;
     UPDATE dbo.tbl_Menus SET ParentMenuId = NULL, DisplayOrder = 13 WHERE Id = 19;
     UPDATE dbo.tbl_Menus SET ParentMenuId = NULL, DisplayOrder = 15 WHERE Id = 20;
     UPDATE dbo.tbl_Menus SET ParentMenuId = NULL, DisplayOrder = 14 WHERE Id = 1021;
     UPDATE dbo.tbl_Menus SET ParentMenuId = NULL, DisplayOrder = 14 WHERE Id = 16;
     UPDATE dbo.tbl_Menus SET ParentMenuId = 1049, DisplayOrder = 6  WHERE Id = 1047;
     UPDATE dbo.tbl_Menus SET ParentMenuId = 1049, DisplayOrder = 7  WHERE Id = 1048;
     UPDATE dbo.tbl_Menus SET Flag = 0, IsVisible = 1 WHERE Id = 1049;

     DELETE FROM dbo.tbl_AccessPermissions WHERE Added_by = 'migration-015';
     DELETE FROM dbo.tbl_Menus WHERE Id IN (@F, @E, @A);

   COMMIT TRANSACTION;

   TO FLATTEN Administration instead of nesting Master Settings inside it:

     UPDATE dbo.tbl_Menus SET ParentMenuId = @AdminId
     WHERE  ParentMenuId = 20;          -- Master Settings' children move up one level
     UPDATE dbo.tbl_Menus SET Flag = 1, IsVisible = 0 WHERE Id = 20;
     -- then grant @AdminId to any role that held a permission on those children
   ============================================================================= */
