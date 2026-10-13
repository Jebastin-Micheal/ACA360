/* =============================================================================
   035_drop_archive_schema.sql

   Permanently drops the [archive] schema that script 034 created, with the 22
   unused tables and the one procedure (sp_ReorderUserShortcuts) it moved there.

   RUN ONLY WHEN
     - script 034 has been in place in this environment long enough to be sure
       nothing needed those objects (no errors naming them in the application log,
       no one asking for the data), and
     - a full database backup has been taken. After this script the data can only
       come back from that backup.

   Safety
     1. Does nothing if the [archive] schema does not exist (already dropped, or 034
        never ran here).
     2. Stops before changing anything if [archive] holds any object that is not one
        of the 23 that 034 moved (someone may have parked something else there), or
        if anything outside [archive] still depends on them: a foreign key from a
        table that stays, or a procedure / function / view / trigger naming one.
     3. Lists every archived table with its row count, then drops foreign keys,
        tables, the procedure and the schema in one transaction.
   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF SCHEMA_ID(N'archive') IS NULL
BEGIN
    PRINT N'035: there is no [archive] schema here - nothing to drop.';
    RETURN;
END;

-- The 23 objects script 034 moved (22 tables + 1 procedure).
DECLARE @Expected TABLE (ObjName SYSNAME PRIMARY KEY);
INSERT INTO @Expected (ObjName) VALUES
    (N'tbl_Medtracker_Account_Manager_History'), (N'tbl_Medtracker_Brokertbl_Medtracker_Contact'),
    (N'tbl_Medtracker_Data_Analyst_History'), (N'tbl_Medtracker_DataAnalyst'), (N'tbl_Medtracker_Firm'),
    (N'tbl_Medtracker_Location'), (N'tbl_Medtracker_ImplementationCategory'), (N'tbl_Medtracker_Permission'),
    (N'CommunicationsLog'), (N'tbl_AccountManager'), (N'Payments'), (N'UserLoginHistory'),
    (N'ServiceEngagements'), (N'WorkflowItems'), (N'WorkflowTemplates'), (N'EmployerACAProfile'),
    (N'ImportDiffLog'), (N'tbl_FileImportImpactAnalysis'), (N'Tracker_BillingContact'),
    (N'tbl_IntegrationSettings'), (N'mcMailServer'), (N'_ParamMigration_Backup'),
    (N'sp_ReorderUserShortcuts');

DECLARE @Blocked TABLE (Reason NVARCHAR(400));

-- 2a. Anything in [archive] that 034 did not put there (constraints belong to their tables).
INSERT INTO @Blocked (Reason)
SELECT N'archive.' + o.name + N' (' + o.type_desc + N') was not moved by script 034'
FROM sys.objects o
WHERE o.schema_id = SCHEMA_ID(N'archive')
  AND o.parent_object_id = 0
  AND o.name NOT IN (SELECT ObjName FROM @Expected);

-- 2b. A foreign key from a table outside [archive] to one inside it.
INSERT INTO @Blocked (Reason)
SELECT N'FK ' + fk.name + N' on ' + OBJECT_SCHEMA_NAME(fk.parent_object_id) + N'.' + OBJECT_NAME(fk.parent_object_id)
       + N' references archive.' + OBJECT_NAME(fk.referenced_object_id)
FROM sys.foreign_keys fk
WHERE OBJECT_SCHEMA_NAME(fk.referenced_object_id) = N'archive'
  AND OBJECT_SCHEMA_NAME(fk.parent_object_id) <> N'archive';

-- 2c. A procedure / function / view / trigger outside [archive] that names one of them
--     (substring first, then a whole-word match; '_' is escaped for LIKE). The
--     sp_ResetOperationalData keep-list only lists names and reports missing ones.
INSERT INTO @Blocked (Reason)
SELECT N'archive.' + e.ObjName + N' is named by ' + OBJECT_SCHEMA_NAME(m.object_id) + N'.' + OBJECT_NAME(m.object_id)
FROM @Expected e
JOIN sys.sql_modules m ON CHARINDEX(e.ObjName, m.definition) > 0
WHERE OBJECT_SCHEMA_NAME(m.object_id) <> N'archive'
  AND OBJECT_NAME(m.object_id) <> N'sp_ResetOperationalData'
  AND N' ' + m.definition + N' ' LIKE N'%[^A-Za-z0-9_]' + REPLACE(e.ObjName, N'_', N'[_]') + N'[^A-Za-z0-9_]%';

IF EXISTS (SELECT 1 FROM @Blocked)
BEGIN
    SELECT Reason AS BlockedBecause FROM @Blocked ORDER BY Reason;
    RAISERROR(N'035: [archive] is not safe to drop (see result above). Nothing was dropped.', 16, 1);
    RETURN;
END;

-- 3a. What is about to go.
SELECT t.name AS ArchivedTable,
       (SELECT SUM(p.rows) FROM sys.partitions p WHERE p.object_id = t.object_id AND p.index_id IN (0, 1)) AS RowsBeingDropped
FROM sys.tables t
WHERE t.schema_id = SCHEMA_ID(N'archive')
ORDER BY t.name;

-- 3b. Drop.
BEGIN TRANSACTION;

DECLARE @Sql NVARCHAR(MAX) = N'';

-- foreign keys on archived tables first, so the tables can go in any order
SELECT @Sql += N'ALTER TABLE archive.' + QUOTENAME(OBJECT_NAME(fk.parent_object_id))
             + N' DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';' + CHAR(10)
FROM sys.foreign_keys fk
WHERE OBJECT_SCHEMA_NAME(fk.parent_object_id) = N'archive';

SELECT @Sql += N'DROP TABLE archive.' + QUOTENAME(t.name) + N';' + CHAR(10)
FROM sys.tables t
WHERE t.schema_id = SCHEMA_ID(N'archive');

SELECT @Sql += N'DROP PROCEDURE archive.' + QUOTENAME(p.name) + N';' + CHAR(10)
FROM sys.procedures p
WHERE p.schema_id = SCHEMA_ID(N'archive');

EXEC sys.sp_executesql @Sql;
EXEC (N'DROP SCHEMA archive;');

COMMIT TRANSACTION;

SELECT CASE WHEN SCHEMA_ID(N'archive') IS NULL THEN N'archive schema dropped' ELSE N'archive schema still present' END AS Result;
GO
