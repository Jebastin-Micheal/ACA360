/* =============================================================================
   063 - Filing deadlines

   dbo.FilingDeadlines holds the dates that matter each filing (tax) year: the IRS
   dates and the state individual-mandate dates (CA, NJ, RI, DC, MA). They show as
   countdowns on every dashboard and as read-only entries in the Calendar, and the
   Employer dashboard's mailing / e-filing countdown reads the IRS ones (it assumed
   1 and 31 March of the next year).

   Seeded for tax years 2025 and 2026 (filed in 2026 and 2027). A date that falls
   on a weekend or federal holiday moves to the next business day. The state dates
   are marked NeedsCheck = 1: confirm them on Administration > Filing Deadlines
   (SuperAdmin, Admin), where dates can be added, changed or removed.
   Electronic filing is required at 10 or more returns, so the paper date rarely applies.

   Safe to re-run: seeded rows are added only when missing; edited rows are kept.
   ============================================================================= */
SET NOCOUNT ON;
GO

IF OBJECT_ID('dbo.FilingDeadlines') IS NULL
BEGIN
    CREATE TABLE dbo.FilingDeadlines
    (
        FilingDeadlineId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FilingDeadlines PRIMARY KEY,
        FilingYear   INT            NOT NULL,   -- the tax year the forms are for
        Jurisdiction NVARCHAR(10)   NOT NULL,   -- IRS, CA, NJ, RI, DC, MA, ...
        Kind         NVARCHAR(20)   NOT NULL,   -- Furnish, E-file, Paper, Other
        Title        NVARCHAR(200)  NOT NULL,
        DueDate      DATE           NOT NULL,
        Notes        NVARCHAR(500)  NULL,
        NeedsCheck   BIT            NOT NULL CONSTRAINT DF_FilingDeadlines_NeedsCheck DEFAULT 0,
        IsActive     BIT            NOT NULL CONSTRAINT DF_FilingDeadlines_IsActive DEFAULT 1,
        ModifiedBy   NVARCHAR(256)  NULL,
        ModifiedAt   DATETIME2(0)   NOT NULL CONSTRAINT DF_FilingDeadlines_ModifiedAt DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_FilingDeadlines_Due ON dbo.FilingDeadlines (DueDate) INCLUDE (FilingYear, Jurisdiction, Kind, IsActive);
END
GO

DECLARE @Seed TABLE (FilingYear INT, Jurisdiction NVARCHAR(10), Kind NVARCHAR(20), Title NVARCHAR(200), DueDate DATE, Notes NVARCHAR(500), NeedsCheck BIT);
INSERT @Seed VALUES
 -- Tax year 2025, filed in 2026
 (2025, 'IRS', 'Furnish', 'Furnish 1095-C / 1095-B to employees',            '2026-03-02', 'Due 31 January plus the automatic 30 days.', 0),
 (2025, 'IRS', 'Paper',   'Paper 1094-C / 1095-C to the IRS',                '2026-03-02', '28 February falls on a Saturday. Only under 10 returns.', 0),
 (2025, 'IRS', 'E-file',  'E-file 1094-C / 1095-C with the IRS (AIR)',       '2026-03-31', NULL, 0),
 (2025, 'CA',  'Furnish', 'California: statement to residents',             '2026-02-02', '31 January falls on a Saturday.', 1),
 (2025, 'CA',  'E-file',  'California: file with the Franchise Tax Board',  '2026-03-31', NULL, 1),
 (2025, 'NJ',  'Furnish', 'New Jersey: statement to residents',             '2026-03-02', NULL, 1),
 (2025, 'NJ',  'E-file',  'New Jersey: file with the Division of Taxation', '2026-03-31', NULL, 1),
 (2025, 'RI',  'Furnish', 'Rhode Island: statement to residents',           '2026-02-02', '31 January falls on a Saturday.', 1),
 (2025, 'RI',  'E-file',  'Rhode Island: file with the Division of Taxation', '2026-03-31', NULL, 1),
 (2025, 'DC',  'E-file',  'District of Columbia: file with the OTR',        '2026-04-30', '30 days after the IRS deadline.', 1),
 (2025, 'MA',  'Furnish', 'Massachusetts: Form MA 1099-HC to residents',    '2026-02-02', 'Issued by the insurer or plan.', 1),
 -- Tax year 2026, filed in 2027
 (2026, 'IRS', 'Furnish', 'Furnish 1095-C / 1095-B to employees',            '2027-03-02', 'Due 31 January plus the automatic 30 days.', 0),
 (2026, 'IRS', 'Paper',   'Paper 1094-C / 1095-C to the IRS',                '2027-03-01', '28 February falls on a Sunday. Only under 10 returns.', 0),
 (2026, 'IRS', 'E-file',  'E-file 1094-C / 1095-C with the IRS (AIR)',       '2027-03-31', NULL, 0),
 (2026, 'CA',  'Furnish', 'California: statement to residents',             '2027-02-01', '31 January falls on a Sunday.', 1),
 (2026, 'CA',  'E-file',  'California: file with the Franchise Tax Board',  '2027-03-31', NULL, 1),
 (2026, 'NJ',  'Furnish', 'New Jersey: statement to residents',             '2027-03-02', NULL, 1),
 (2026, 'NJ',  'E-file',  'New Jersey: file with the Division of Taxation', '2027-03-31', NULL, 1),
 (2026, 'RI',  'Furnish', 'Rhode Island: statement to residents',           '2027-02-01', '31 January falls on a Sunday.', 1),
 (2026, 'RI',  'E-file',  'Rhode Island: file with the Division of Taxation', '2027-03-31', NULL, 1),
 (2026, 'DC',  'E-file',  'District of Columbia: file with the OTR',        '2027-04-30', '30 days after the IRS deadline.', 1),
 (2026, 'MA',  'Furnish', 'Massachusetts: Form MA 1099-HC to residents',    '2027-02-01', 'Issued by the insurer or plan.', 1);

INSERT dbo.FilingDeadlines (FilingYear, Jurisdiction, Kind, Title, DueDate, Notes, NeedsCheck, ModifiedBy)
SELECT S.FilingYear, S.Jurisdiction, S.Kind, S.Title, S.DueDate, S.Notes, S.NeedsCheck, 'script 063'
FROM   @Seed S
WHERE  NOT EXISTS (SELECT 1 FROM dbo.FilingDeadlines D
                   WHERE D.FilingYear = S.FilingYear AND D.Jurisdiction = S.Jurisdiction AND D.Kind = S.Kind);
PRINT CONCAT('  ', @@ROWCOUNT, ' deadlines added');
GO

CREATE OR ALTER PROCEDURE dbo.sp_FilingDeadlines_List
    @FilingYear INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT FilingDeadlineId, FilingYear, Jurisdiction, Kind, Title, DueDate, Notes, NeedsCheck, ModifiedBy, ModifiedAt
    FROM   dbo.FilingDeadlines
    WHERE  IsActive = 1 AND (@FilingYear IS NULL OR FilingYear = @FilingYear)
    ORDER  BY FilingYear DESC, DueDate, CASE Jurisdiction WHEN 'IRS' THEN 0 ELSE 1 END, Jurisdiction;
END
GO

-- The next deadlines from a day on (today unless given).
CREATE OR ALTER PROCEDURE dbo.sp_FilingDeadlines_Upcoming
    @From DATE = NULL,
    @Top  INT  = 5
AS
BEGIN
    SET NOCOUNT ON;
    SET @From = ISNULL(@From, CAST(GETDATE() AS DATE));
    SELECT TOP (@Top) FilingDeadlineId, FilingYear, Jurisdiction, Kind, Title, DueDate, Notes, NeedsCheck, ModifiedBy, ModifiedAt
    FROM   dbo.FilingDeadlines
    WHERE  IsActive = 1 AND DueDate >= @From
    ORDER  BY DueDate, CASE Jurisdiction WHEN 'IRS' THEN 0 ELSE 1 END, Jurisdiction;
END
GO

-- Add (Id 0) or change one deadline. Saving marks it as checked.
CREATE OR ALTER PROCEDURE dbo.sp_FilingDeadlines_Save
    @FilingDeadlineId INT,
    @FilingYear   INT,
    @Jurisdiction NVARCHAR(10),
    @Kind         NVARCHAR(20),
    @Title        NVARCHAR(200),
    @DueDate      DATE,
    @Notes        NVARCHAR(500) = NULL,
    @ModifiedBy   NVARCHAR(256) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF ISNULL(@FilingDeadlineId, 0) = 0
    BEGIN
        INSERT dbo.FilingDeadlines (FilingYear, Jurisdiction, Kind, Title, DueDate, Notes, NeedsCheck, ModifiedBy)
        VALUES (@FilingYear, UPPER(LTRIM(RTRIM(@Jurisdiction))), @Kind, LTRIM(RTRIM(@Title)), @DueDate, NULLIF(LTRIM(RTRIM(@Notes)), N''), 0, @ModifiedBy);
        SELECT CAST(SCOPE_IDENTITY() AS INT) AS FilingDeadlineId;
    END
    ELSE
    BEGIN
        UPDATE dbo.FilingDeadlines
        SET    FilingYear = @FilingYear, Jurisdiction = UPPER(LTRIM(RTRIM(@Jurisdiction))), Kind = @Kind, Title = LTRIM(RTRIM(@Title)),
               DueDate = @DueDate, Notes = NULLIF(LTRIM(RTRIM(@Notes)), N''), NeedsCheck = 0, ModifiedBy = @ModifiedBy, ModifiedAt = SYSUTCDATETIME()
        WHERE  FilingDeadlineId = @FilingDeadlineId AND IsActive = 1;
        SELECT CASE WHEN @@ROWCOUNT = 1 THEN @FilingDeadlineId ELSE 0 END AS FilingDeadlineId;
    END
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_FilingDeadlines_Delete
    @FilingDeadlineId INT,
    @ModifiedBy NVARCHAR(256) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.FilingDeadlines SET IsActive = 0, ModifiedBy = @ModifiedBy, ModifiedAt = SYSUTCDATETIME()
    WHERE  FilingDeadlineId = @FilingDeadlineId AND IsActive = 1;
    SELECT @@ROWCOUNT AS Removed;
END
GO

------------------------------------------------------------
-- Menu: Administration > Filing Deadlines, for SuperAdmin and Admin.
------------------------------------------------------------
DECLARE @Admin INT = (SELECT TOP 1 Id FROM dbo.tbl_Menus WHERE Controller = 'Administration' AND ParentMenuId IS NULL AND Flag = 0 ORDER BY Id);
IF @Admin IS NULL THROW 50063, '063: the Administration menu container was not found.', 1;

DECLARE @Menu INT = (SELECT TOP 1 Id FROM dbo.tbl_Menus WHERE Controller = 'FilingDeadlines' ORDER BY Id);
IF @Menu IS NULL
BEGIN
    INSERT INTO dbo.tbl_Menus (ParentMenuId, MenuName, Controller, Icon, DisplayOrder, IsActive, DateModified, Flag, IsVisible)
    VALUES (@Admin, 'Filing Deadlines', 'FilingDeadlines', 'bx bx-calendar-exclamation',
            ISNULL((SELECT MAX(DisplayOrder) FROM dbo.tbl_Menus WHERE ParentMenuId = @Admin AND Flag = 0), 0) + 1,
            1, GETDATE(), 0, 1);
    SET @Menu = SCOPE_IDENTITY();
    PRINT '  menu item added';
END
ELSE
    UPDATE dbo.tbl_Menus SET ParentMenuId = @Admin, IsActive = 1, Flag = 0, IsVisible = 1, DateModified = GETDATE()
    WHERE Id = @Menu AND (ParentMenuId <> @Admin OR IsActive <> 1 OR Flag <> 0 OR IsVisible <> 1);

INSERT INTO dbo.tbl_AccessPermissions (RoleId, MenuId, ActionID, IsEnabled, Date_Added, Added_by)
SELECT r.ID, @Menu, a.Id, 1, GETDATE(), 'script 063'
FROM   dbo.tbl_Roles r
CROSS  JOIN dbo.tbl_ActionTypes a
WHERE  r.RoleName IN ('SuperAdmin', 'Admin') AND a.ActionName IN ('Index', 'View')
  AND  NOT EXISTS (SELECT 1 FROM dbo.tbl_AccessPermissions p WHERE p.RoleId = r.ID AND p.MenuId = @Menu AND p.ActionID = a.Id);

UPDATE p SET IsEnabled = 1, Date_Modified = GETDATE(), Modified_by = 'script 063'
FROM   dbo.tbl_AccessPermissions p
JOIN   dbo.tbl_ActionTypes a ON a.Id = p.ActionID
JOIN   dbo.tbl_Roles r ON r.ID = p.RoleId
WHERE  p.MenuId = @Menu AND r.RoleName IN ('SuperAdmin', 'Admin') AND a.ActionName IN ('Index', 'View') AND ISNULL(p.IsEnabled, 0) = 0;

PRINT '063: filing deadlines; Administration > Filing Deadlines (SuperAdmin, Admin).';
GO
