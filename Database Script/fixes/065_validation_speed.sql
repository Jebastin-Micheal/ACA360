/* =============================================================================
   065 - Faster validation (and so faster saves in the Error Triage editor)

   sp_Validation_UpdateStatus updated the statistics of StagingRowErrors and of
   every staging table (a 20% sample of all files' rows) each time a file was
   validated. Every save in the Correction Editor re-validates the file, so on a
   5,000-employee file this was 4.3 of the 9.5 seconds a save took, and it grows
   with every file ever uploaded. SQL Server keeps statistics current itself.

   The procedure is the database's current one (script 053) without those steps.
   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE [dbo].[sp_Validation_UpdateStatus]
(
    @FileLogId INT
)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @TableName NVARCHAR(255), @PKColumn NVARCHAR(255), @SQL NVARCHAR(MAX);

    -- 065: no statistics update here. It ran on every validation, over every file's rows
    -- (StagingRowErrors and each staging table, 20% sample), and was most of the time a
    -- save in the Correction Editor took. SQL Server keeps the statistics up to date itself.

    DECLARE TableCursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT TableName, PrimaryKeyColumn FROM StagingTableMetadata;

    OPEN TableCursor;
    FETCH NEXT FROM TableCursor INTO @TableName, @PKColumn;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        -- 3. Invalidate rows with an error-severity issue in this table (053: warnings and
        --    information notes do not keep a row out of the import).
        SET @SQL = '
            UPDATE T
            SET T.IsValid = 0
            FROM ' + QUOTENAME(@TableName) + ' AS T
            INNER JOIN StagingRowErrors AS E
                ON E.StagingRowId = T.' + QUOTENAME(@PKColumn) + '
            LEFT JOIN ValidationRules AS R ON R.RuleId = E.RuleId
            WHERE T.FileLogId = @FileLogId
              AND E.FileLogId = @FileLogId
              -- Row IDs only invalidate their own table
              AND E.StagingTableName = ''' + @TableName + '''
              AND ISNULL(R.Severity, ''Error'') NOT IN (''Warning'', ''Information'', ''Info'')
              AND T.IsValid <> 0
            OPTION (MAXDOP 1);';
        EXEC sp_executesql @SQL, N'@FileLogId INT', @FileLogId = @FileLogId;

        FETCH NEXT FROM TableCursor INTO @TableName, @PKColumn;
    END
    CLOSE TableCursor;
    DEALLOCATE TableCursor;
END
GO
