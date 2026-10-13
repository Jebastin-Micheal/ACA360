/* =============================================================================
   036_fix_menu_rows.sql

   Two menu-data fixes found while checking which menu rows lead to missing pages.

   1. Whitespace in Controller values. Permission claims are built as
      "Controller:Action", so a tab or space in the value produces claims nothing
      matches (row 1049 "Report" under Tracker held TAB + "Tracker"). Every
      Controller value is trimmed of spaces, tabs, carriage returns and line feeds.
      The web app now trims the value when a menu is saved.

   2. The "Filing Year" menu (Controller 'FilingYear'). There is no FilingYear
      controller any more - filing years are managed on the System settings page -
      so the item would link to a missing page. It is set inactive (not deleted).

   Group headings that have child menus (Tracker, Reports, Administration) and the
   hidden permission-only rows (ACA Calculation, Consent, Portal Admin) are correct
   as they are: headings render as toggles, hidden rows are never shown.

   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

-- 1. Trim whitespace (incl. tab / CR / LF) from Controller values.
DECLARE @Trimmed TABLE (Id INT, OldValue NVARCHAR(200), NewValue NVARCHAR(200));

UPDATE m
SET    m.Controller = LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(m.Controller, CHAR(9), N''), CHAR(13), N''), CHAR(10), N''))),
       m.DateModified = GETDATE()
OUTPUT inserted.Id, deleted.Controller, inserted.Controller INTO @Trimmed (Id, OldValue, NewValue)
FROM   dbo.tbl_Menus m
WHERE  m.Controller IS NOT NULL
  AND  m.Controller <> LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(m.Controller, CHAR(9), N''), CHAR(13), N''), CHAR(10), N'')));

-- 2. Retire the Filing Year menu item (its controller no longer exists).
DECLARE @Retired TABLE (Id INT, MenuName NVARCHAR(200));

UPDATE m
SET    m.IsActive = 0,
       m.DateModified = GETDATE()
OUTPUT inserted.Id, inserted.MenuName INTO @Retired (Id, MenuName)
FROM   dbo.tbl_Menus m
WHERE  m.Controller = N'FilingYear'
  AND  m.IsActive = 1;

COMMIT TRANSACTION;

SELECT Id, N'Controller trimmed' AS Change,
       REPLACE(REPLACE(OldValue, CHAR(9), N'<TAB>'), CHAR(13), N'<CR>') AS OldValue, NewValue
FROM @Trimmed
UNION ALL
SELECT Id, N'Set inactive', MenuName, NULL FROM @Retired;
GO
