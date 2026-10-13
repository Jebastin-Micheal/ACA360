/* =============================================================================
   034_archive_unused_tables.sql

   Moves 22 tables and one procedure that nothing uses into a new [archive]
   schema. Clean-up step 6 of the unused-code inventory.

   Moved, not dropped: ALTER SCHEMA ... TRANSFER keeps each table's rows, keys,
   indexes and constraints exactly as they are; the app simply stops seeing them
   under dbo. Once production has run without them, a later script can drop the
   [archive] schema.

   How "unused" was decided (September 2026, against master + db_ACA360)
     - No C#, Razor or JavaScript file names the table, and no procedure / function /
       view / trigger that is used names it.
     - No table name is built at run time in the code.
     - None of the kept tables has a foreign key to them (checked below).

   Kept on purpose
     - HangFire.AggregatedCounter / JobParameter / JobQueue (the Hangfire library's).
     - tbl_AccessPermissions_Backup_030, tbl_DroppedProcedures_Backup_033 (rollback
       copies from scripts 030 and 033).
     - tbl_Medtracker_AcctManager, tbl_Medtracker_Bricktbl_Medtracker_Broker: the live
       tbl_Medtracker_Employer has foreign keys to them (sp_AddEmployerCommon inserts
       into it).

   Safety
     1. Stops before changing anything if a table that is NOT being moved has a
        foreign key to one that is, or if a procedure / function / view / trigger
        that is not being moved still names one. sp_ResetOperationalData is exempt:
        it only lists table names in its keep-list and reports names that no longer
        exist.
     2. Moves everything inside one transaction. Objects already in [archive] are
        skipped, so it is safe to re-run.

   To move one back:
       ALTER SCHEMA dbo TRANSFER archive.[TableName];
   ============================================================================= */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF SCHEMA_ID(N'archive') IS NULL
    EXEC (N'CREATE SCHEMA archive AUTHORIZATION dbo;');
GO

DECLARE @Move TABLE (ObjName SYSNAME PRIMARY KEY, Grp VARCHAR(40) NOT NULL);
INSERT INTO @Move (ObjName, Grp) VALUES
    -- Old Medtracker tables (the parents tbl_Medtracker_AcctManager and
    -- tbl_Medtracker_Bricktbl_Medtracker_Broker stay; see header)
    (N'tbl_Medtracker_Account_Manager_History', 'Medtracker'),
    (N'tbl_Medtracker_Brokertbl_Medtracker_Contact', 'Medtracker'),
    (N'tbl_Medtracker_Data_Analyst_History', 'Medtracker'),
    (N'tbl_Medtracker_DataAnalyst', 'Medtracker'),
    (N'tbl_Medtracker_Firm', 'Medtracker'),
    (N'tbl_Medtracker_Location', 'Medtracker'),
    (N'tbl_Medtracker_ImplementationCategory', 'Medtracker'),
    (N'tbl_Medtracker_Permission', 'Medtracker'),

    -- Duplicates of, or replaced by, tables the app uses
    (N'CommunicationsLog', 'Duplicate'),          -- the app uses CommunicationLog
    (N'tbl_AccountManager', 'Duplicate'),
    (N'Payments', 'Duplicate'),
    (N'UserLoginHistory', 'Duplicate'),           -- login tracking removed from the code

    -- Features that were never finished or were removed
    (N'ServiceEngagements', 'Unused feature'),
    (N'WorkflowItems', 'Unused feature'),
    (N'WorkflowTemplates', 'Unused feature'),
    (N'EmployerACAProfile', 'Unused feature'),
    (N'ImportDiffLog', 'Unused feature'),
    (N'tbl_FileImportImpactAnalysis', 'Unused feature'),
    (N'Tracker_BillingContact', 'Unused feature'),
    (N'tbl_IntegrationSettings', 'Unused feature'),
    (N'mcMailServer', 'Unused feature'),

    -- Old migration backup
    (N'_ParamMigration_Backup', 'Old backup'),

    -- Procedure whose last caller (ShortcutController.Reorder) was removed
    (N'sp_ReorderUserShortcuts', 'Procedure');

-- 1a. Stop if a table that stays has a foreign key to a table that moves.
DECLARE @Blocked TABLE (Reason NVARCHAR(400));
INSERT INTO @Blocked (Reason)
SELECT N'FK ' + fk.name + N' on ' + OBJECT_SCHEMA_NAME(fk.parent_object_id) + N'.' + OBJECT_NAME(fk.parent_object_id)
       + N' references dbo.' + m.ObjName
FROM @Move m
JOIN sys.foreign_keys fk ON fk.referenced_object_id = OBJECT_ID(N'dbo.' + QUOTENAME(m.ObjName))
WHERE NOT (OBJECT_SCHEMA_NAME(fk.parent_object_id) = N'dbo'
           AND OBJECT_NAME(fk.parent_object_id) IN (SELECT ObjName FROM @Move));

-- 1b. Stop if a module that stays still names one of them (substring first, then a
--     whole-word match; '_' is escaped because it is a LIKE wildcard).
INSERT INTO @Blocked (Reason)
SELECT N'dbo.' + m.ObjName + N' is named by ' + OBJECT_SCHEMA_NAME(sm.object_id) + N'.' + OBJECT_NAME(sm.object_id)
FROM @Move m
JOIN sys.sql_modules sm ON CHARINDEX(m.ObjName, sm.definition) > 0
WHERE OBJECT_NAME(sm.object_id) NOT IN (SELECT ObjName FROM @Move)
  AND OBJECT_NAME(sm.object_id) <> N'sp_ResetOperationalData'
  AND OBJECT_SCHEMA_NAME(sm.object_id) <> N'archive'
  AND N' ' + sm.definition + N' ' LIKE N'%[^A-Za-z0-9_]' + REPLACE(m.ObjName, N'_', N'[_]') + N'[^A-Za-z0-9_]%';

IF EXISTS (SELECT 1 FROM @Blocked)
BEGIN
    SELECT Reason AS BlockedBecause FROM @Blocked ORDER BY Reason;
    RAISERROR(N'034: something that stays still depends on an object on the list (see result above). Nothing was moved.', 16, 1);
    RETURN;
END;

-- 2. Move.
BEGIN TRANSACTION;

DECLARE @Name SYSNAME, @Sql NVARCHAR(400), @Moved INT = 0;
DECLARE move_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT m.ObjName FROM @Move m
    WHERE OBJECT_ID(N'dbo.' + QUOTENAME(m.ObjName)) IS NOT NULL
      AND OBJECT_ID(N'archive.' + QUOTENAME(m.ObjName)) IS NULL
    ORDER BY m.ObjName;
OPEN move_cursor;
FETCH NEXT FROM move_cursor INTO @Name;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @Sql = N'ALTER SCHEMA archive TRANSFER dbo.' + QUOTENAME(@Name) + N';';
    EXEC sys.sp_executesql @Sql;
    SET @Moved += 1;
    FETCH NEXT FROM move_cursor INTO @Name;
END;
CLOSE move_cursor;
DEALLOCATE move_cursor;

COMMIT TRANSACTION;

-- 3. Report.
SELECT @Moved AS MovedNow,
       (SELECT COUNT(*) FROM @Move) AS OnList,
       (SELECT COUNT(*) FROM @Move m WHERE OBJECT_ID(N'dbo.' + QUOTENAME(m.ObjName)) IS NOT NULL) AS StillInDbo,
       (SELECT COUNT(*) FROM @Move m WHERE OBJECT_ID(N'archive.' + QUOTENAME(m.ObjName)) IS NOT NULL) AS InArchive;
GO
