/* =============================================================================
   053_warnings_do_not_exclude_rows.sql

   A staging row with only a warning (or an information note) was marked invalid
   (IsValid = 0), the same as a row with an error. sp_Validation_UpdateStatus set
   IsValid = 0 for any issue logged against the row, whatever its severity. So:
     - the file was "Valid with warnings", but the Import Analysis counted those
       rows as Excluded and "Confirm & Import" stayed disabled ("Resolve errors
       and excluded rows before importing."), and
     - had the import gone ahead, sp_ImportData_FromStaging (IsValid = 1 rows
       only) would have left those rows out without saying so.
   Example: a file with 156 "Zip Code Mismatch with State" warnings could not be
   imported, and those 156 employees' 726 dependents were excluded with them.

     1. sp_Validation_UpdateStatus: a row is invalid only when an issue against it
        has an error severity (Error, Fatal, Critical; an issue whose rule is not
        found counts as an error). Warnings and information notes leave it valid.
     2. Repair: rows already made invalid by warnings / information only become
        valid again, in files that have not been imported (not Importing,
        Complete or Superseded, and not marked Imported).

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
    DECLARE @TableName NVARCHAR(255), @PKColumn NVARCHAR(255), @SQL NVARCHAR(MAX), @StatsSQL NVARCHAR(MAX);

    -- 1. Force serial stats update for the error table to optimize the join
    UPDATE STATISTICS [dbo].[StagingRowErrors] WITH MAXDOP = 1;

    DECLARE TableCursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT TableName, PrimaryKeyColumn FROM StagingTableMetadata;

    OPEN TableCursor;
    FETCH NEXT FROM TableCursor INTO @TableName, @PKColumn;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        -- 2. Refresh stats for the current staging table
        SET @StatsSQL = 'UPDATE STATISTICS ' + QUOTENAME(@TableName) + ' WITH SAMPLE 20 PERCENT, MAXDOP = 1;';
        EXEC sp_executesql @StatsSQL;

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

-- Repair rows that warnings alone made invalid, in files not yet imported. Each row's
-- issues are summed up once (worst severity), then the rows are updated in one join.
IF OBJECT_ID('tempdb..#RowIssue') IS NOT NULL DROP TABLE #RowIssue;
SELECT E.FileLogId, E.StagingTableName, E.StagingRowId,
       MAX(CASE WHEN ISNULL(R.Severity, 'Error') NOT IN ('Warning', 'Information', 'Info') THEN 1 ELSE 0 END) AS HasError
INTO   #RowIssue
FROM   dbo.StagingRowErrors E
JOIN   dbo.UploadedFileLog F ON F.FileLogId = E.FileLogId
LEFT JOIN dbo.ValidationRules R ON R.RuleId = E.RuleId
WHERE  ISNULL(F.WorkflowStatusId, 0) NOT IN (251, 260, 299)
  AND  ISNULL(F.ValidationStatusId, 0) <> 90
GROUP BY E.FileLogId, E.StagingTableName, E.StagingRowId;
DELETE FROM #RowIssue WHERE HasError = 1;
CREATE CLUSTERED INDEX IX_RowIssue ON #RowIssue (StagingTableName, FileLogId, StagingRowId);

DECLARE @TableName NVARCHAR(255), @PKColumn NVARCHAR(255), @SQL NVARCHAR(MAX), @Fixed INT, @Total INT = 0;
DECLARE TableCursor CURSOR LOCAL FAST_FORWARD FOR SELECT TableName, PrimaryKeyColumn FROM StagingTableMetadata;
OPEN TableCursor;
FETCH NEXT FROM TableCursor INTO @TableName, @PKColumn;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @SQL = '
        UPDATE T SET T.IsValid = 1
        FROM ' + QUOTENAME(@TableName) + ' AS T
        JOIN #RowIssue AS I ON I.StagingTableName = @TableName AND I.FileLogId = T.FileLogId
                           AND I.StagingRowId = T.' + QUOTENAME(@PKColumn) + '
        WHERE T.IsValid = 0;
        SET @Fixed = @@ROWCOUNT;';
    EXEC sp_executesql @SQL, N'@TableName NVARCHAR(255), @Fixed INT OUTPUT', @TableName = @TableName, @Fixed = @Fixed OUTPUT;
    IF @Fixed > 0 PRINT '  ' + @TableName + ': ' + CAST(@Fixed AS VARCHAR(10)) + ' row(s) valid again';
    SET @Total += @Fixed;
    FETCH NEXT FROM TableCursor INTO @TableName, @PKColumn;
END
CLOSE TableCursor;
DEALLOCATE TableCursor;
DROP TABLE #RowIssue;

PRINT '053: warnings and information notes no longer exclude rows from the import (' + CAST(@Total AS VARCHAR(10)) + ' row(s) repaired).';
GO
