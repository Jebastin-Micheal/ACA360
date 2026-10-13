/* =============================================================================
   054_dummy_data_menu.sql

   Administration > Dummy Data (DummyDataController): made-up import workbooks for
   testing uploads. The page itself admits SuperAdmin only ([Authorize(Roles =
   SuperAdmin)]); this adds its menu item under Administration and gives the
   SuperAdmin role the Index and View permissions, which is what shows a menu item.
   Users see it after they next sign in.

   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

DECLARE @Admin INT = (SELECT TOP 1 Id FROM dbo.tbl_Menus WHERE Controller = 'Administration' AND ParentMenuId IS NULL AND Flag = 0 ORDER BY Id);
IF @Admin IS NULL THROW 50054, '054: the Administration menu container was not found.', 1;

DECLARE @Menu INT = (SELECT TOP 1 Id FROM dbo.tbl_Menus WHERE Controller = 'DummyData' ORDER BY Id);
IF @Menu IS NULL
BEGIN
    INSERT INTO dbo.tbl_Menus (ParentMenuId, MenuName, Controller, Icon, DisplayOrder, IsActive, DateModified, Flag, IsVisible)
    VALUES (@Admin, 'Dummy Data', 'DummyData', 'bx bx-data',
            ISNULL((SELECT MAX(DisplayOrder) FROM dbo.tbl_Menus WHERE ParentMenuId = @Admin AND Flag = 0), 0) + 1,
            1, GETDATE(), 0, 1);
    SET @Menu = SCOPE_IDENTITY();
    PRINT '  menu item added';
END
ELSE
    UPDATE dbo.tbl_Menus SET ParentMenuId = @Admin, IsActive = 1, Flag = 0, IsVisible = 1, DateModified = GETDATE()
    WHERE Id = @Menu AND (ParentMenuId <> @Admin OR IsActive <> 1 OR Flag <> 0 OR IsVisible <> 1);

DECLARE @SuperAdmin INT = (SELECT TOP 1 ID FROM dbo.tbl_Roles WHERE RoleName = 'SuperAdmin');
IF @SuperAdmin IS NULL THROW 50054, '054: the SuperAdmin role was not found.', 1;

INSERT INTO dbo.tbl_AccessPermissions (RoleId, MenuId, ActionID, IsEnabled, Date_Added, Added_by)
SELECT @SuperAdmin, @Menu, a.Id, 1, GETDATE(), 'script 054'
FROM   dbo.tbl_ActionTypes a
WHERE  a.ActionName IN ('Index', 'View')
  AND  NOT EXISTS (SELECT 1 FROM dbo.tbl_AccessPermissions p WHERE p.RoleId = @SuperAdmin AND p.MenuId = @Menu AND p.ActionID = a.Id);

UPDATE p SET IsEnabled = 1, Date_Modified = GETDATE(), Modified_by = 'script 054'
FROM   dbo.tbl_AccessPermissions p JOIN dbo.tbl_ActionTypes a ON a.Id = p.ActionID
WHERE  p.RoleId = @SuperAdmin AND p.MenuId = @Menu AND a.ActionName IN ('Index', 'View') AND ISNULL(p.IsEnabled, 0) = 0;

PRINT '054: Administration > Dummy Data (SuperAdmin).';
GO
