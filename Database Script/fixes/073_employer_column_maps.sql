/* =============================================================================
   073 - Remember column mappings for each employer's files (idea #5)

   A template knows each column's header and a few alternate names. When an
   employer's file calls a column something else, the file used to fail the
   structure check and had to be fixed and uploaded again, every time.

   Now staff map the file's header to the template column once, from the
   rejected file; the mapping is kept for that employer (by EIN, so it holds
   across tax years) and template, and every later file from the employer
   is checked and read with it.

   1. EmployerColumnMaps.
   2. sp_EmployerColumnMaps_Get / _Save / _Delete.

   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

IF OBJECT_ID('dbo.EmployerColumnMaps') IS NULL
BEGIN
    CREATE TABLE dbo.EmployerColumnMaps (
        EmployerColumnMapId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmployerColumnMaps PRIMARY KEY,
        EmployerEin      VARCHAR(20)   NOT NULL,   -- digits only
        TemplateId       INT           NOT NULL,
        SheetName        NVARCHAR(128) NOT NULL,   -- TemplateColumnMaps.SourceSheetName
        TargetColumnName NVARCHAR(128) NOT NULL,   -- TemplateColumnMaps.TargetColumnName
        SourceHeader     NVARCHAR(200) NOT NULL,   -- the header as the employer's file has it
        CreatedBy        NVARCHAR(256) NULL,
        CreatedAt        DATETIME2(0)  NOT NULL CONSTRAINT DF_EmployerColumnMaps_CreatedAt DEFAULT SYSDATETIME(),
        CONSTRAINT UQ_EmployerColumnMaps UNIQUE (EmployerEin, TemplateId, SheetName, TargetColumnName)
    );
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_EmployerColumnMaps_Get
    @EmployerEin VARCHAR(20),
    @TemplateId  INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT M.EmployerColumnMapId, M.EmployerEin, M.TemplateId, M.SheetName, M.TargetColumnName, M.SourceHeader,
           M.CreatedBy, M.CreatedAt, T.SourceColumnName AS TemplateHeader
    FROM   dbo.EmployerColumnMaps M
    LEFT   JOIN dbo.TemplateColumnMaps T ON T.TemplateId = M.TemplateId
                                         AND T.SourceSheetName = M.SheetName AND T.TargetColumnName = M.TargetColumnName
    WHERE  M.EmployerEin = REPLACE(@EmployerEin, '-', '')
      AND  (@TemplateId IS NULL OR M.TemplateId = @TemplateId)
    ORDER  BY M.SheetName, M.TargetColumnName;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_EmployerColumnMaps_Save
    @EmployerEin      VARCHAR(20),
    @TemplateId       INT,
    @SheetName        NVARCHAR(128),
    @TargetColumnName NVARCHAR(128),
    @SourceHeader     NVARCHAR(200),
    @CreatedBy        NVARCHAR(256) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET @EmployerEin = REPLACE(@EmployerEin, '-', '');
    -- Only a column the template really has.
    IF NOT EXISTS (SELECT 1 FROM dbo.TemplateColumnMaps
                   WHERE TemplateId = @TemplateId AND SourceSheetName = @SheetName AND TargetColumnName = @TargetColumnName)
    BEGIN
        SELECT CAST(0 AS INT) AS Saved;
        RETURN;
    END

    MERGE dbo.EmployerColumnMaps AS T
    USING (SELECT @EmployerEin AS EmployerEin, @TemplateId AS TemplateId, @SheetName AS SheetName, @TargetColumnName AS TargetColumnName) AS S
    ON  T.EmployerEin = S.EmployerEin AND T.TemplateId = S.TemplateId AND T.SheetName = S.SheetName AND T.TargetColumnName = S.TargetColumnName
    WHEN MATCHED THEN UPDATE SET SourceHeader = @SourceHeader, CreatedBy = @CreatedBy, CreatedAt = SYSDATETIME()
    WHEN NOT MATCHED THEN INSERT (EmployerEin, TemplateId, SheetName, TargetColumnName, SourceHeader, CreatedBy)
                          VALUES (@EmployerEin, @TemplateId, @SheetName, @TargetColumnName, @SourceHeader, @CreatedBy);
    SELECT CAST(1 AS INT) AS Saved;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_EmployerColumnMaps_Delete
    @EmployerColumnMapId INT,
    @EmployerEin         VARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.EmployerColumnMaps
    WHERE  EmployerColumnMapId = @EmployerColumnMapId AND EmployerEin = REPLACE(@EmployerEin, '-', '');
    SELECT @@ROWCOUNT AS Deleted;
END
GO

PRINT '073: employer column mappings (EmployerColumnMaps).';
GO
