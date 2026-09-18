/* =============================================================================
   020_import_stage2_per_employer.sql

   SQL ONLY. No application changes are needed to run this.

   ---------------------------------------------------------------------------
   THE PROBLEM

   Stage 2 of sp_ProcessEmployerData wrapped EVERY employer and ALL FOUR
   calculation procedures in a single transaction:

       BEGIN TRANSACTION;
           cursor over all employers:
               EXEC sp_Generate1095Codes
               EXEC sp_RunDynamicFlagEngine
               EXEC sp_CalculatePotentialPenalties
               EXEC sp_Generate1094CValues
       COMMIT TRANSACTION;

   On a real client that holds locks on Employee, EmployeeCode, EmployerPlan and
   the rest for the whole run. Anyone opening that employer's screens blocks
   behind it until the last employer finishes.

   Moving the import onto Hangfire took the wait off the request thread, but the
   locking stayed exactly where it was -- it only became invisible. This is the
   change that actually shortens it.

   ---------------------------------------------------------------------------
   WHAT CHANGES

   1. ONE TRANSACTION PER EMPLOYER instead of one for the whole file.

      An employer's codes, flags, penalties and 1094-C values are the meaningful
      atomic unit -- you do not want codes written without the penalties derived
      from them. Lock duration drops from "every employer" to "one employer",
      which is the whole win. Going further and letting each of the four commit
      separately would shave a little more, but it would allow an employer to be
      left half-calculated, and that is not worth the extra few seconds.

      Safe because all four procedures already use the same discipline: each
      opens a transaction only when none exists (IF @@TRANCOUNT = 0) and rolls
      back only what it opened. Nesting depth from here is 1, exactly as before.

   2. THE CURSOR IS GONE. A table variable and a counter do the same job without
      holding cursor resources for the duration, and it gives an ordinal for the
      progress display. The parent employer is processed first, then affiliates.

   3. PROGRESS IS REPORTED between employers, into the four columns already on
      UploadedFileLog that nothing has ever written:

          ValidationTotalRows       how many employers there are
          ValidationRowsProcessed   how many are done
          ValidationProgressPercent 0-100
          ValidationCurrentStep     e.g. "Calculating ATLANTIC COASTAL (2 of 3)"

      Written OUTSIDE any transaction, so a poller reading the row sees it
      immediately rather than blocking on the calculation's locks.

      NOTE: PollStatus does not return these yet, so nothing surfaces in the UI
      until it does. The data is here and correct in the meantime.

   4. THE INVALID WORKFLOW STATUS WRITES ARE REMOVED.

      The procedure used to write WorkflowStatusId = 40 on success and -1 on
      failure. Neither is a workflow status: that range is 200-299, and 40 is a
      VALIDATION status (StructureError). Both were masked because the caller
      overwrote them a moment later -- but once the import runs in the
      background, an interrupted job leaves the file parked on 40, a state no UI
      branch handles.

      This procedure no longer touches WorkflowStatusId at all. The Hangfire job
      owns it: WS.Importing before, WS.Complete or WS.ImportFailed after. One
      writer, and the only writer.

   5. THE FAILING EMPLOYER IS NAMED. The CATCH block previously reported the
      error with no indication of which of the employers it came from.

   6. RE-RAISE USES THROW INSTEAD OF RAISERROR. RAISERROR treats its first
      argument as a format string, so an error message containing a percent sign
      -- entirely possible, these carry employer names and column values --
      would itself fail to format. THROW re-raises the original error number,
      severity and message untouched.

   ---------------------------------------------------------------------------
   STAGE 1 IS UNCHANGED. The merge from staging keeps its own single
   transaction, which is correct: a half-merged file is not a recoverable state.

   RE-RUNNING IS SAFE. All four calculation procedures are idempotent -- that is
   what script 007 relied on when it recalculated already-imported data. If the
   run fails on employer 3 of 7, employers 1 and 2 stay committed and a re-run
   simply redoes them.

   SAFE TO RE-RUN THIS SCRIPT: yes, CREATE OR ALTER.
   ============================================================================= */

USE [db_ACA360];
GO

SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE [dbo].[sp_ProcessEmployerData]
    @FileLogId  INT,
    @ImportMode VARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;

    /* Makes transaction state deterministic: any error dooms the transaction, so
       the CATCH below always has exactly one thing to do. Without it some errors
       leave a transaction alive but unusable. */
    SET XACT_ABORT ON;

    DECLARE @TaxYear            INT,
            @CurrentEmployerId  INT,
            @CurrentEmployerNm  NVARCHAR(255),
            @ErrorMessage       NVARCHAR(4000),
            @EmpName            NVARCHAR(255),
            @TaxID              NVARCHAR(20),
            @Seq                INT = 1,
            @Total              INT = 0,
            /* Tracks which phase we are in, so the CATCH can name it. Set before
               each phase rather than cleared after, which would leave a failure
               in the final stamping blaming the staging merge. */
            @FailedOn           NVARCHAR(300) = N'the staging merge';

    ---------------------------------------------------------
    -- 1. INITIAL LOG STAMPING
    ---------------------------------------------------------
    UPDATE  dbo.UploadedFileLog
    SET     ProcessingStartTime      = GETDATE(),
            ProcessingEndTime        = NULL,
            UploadStatus             = 'Processing',
            ValidationStatus         = 'In-Progress',
            ValidationCurrentStep    = 'Merging staged data into live tables',
            ValidationProgressPercent = 0,
            ValidationRowsProcessed  = 0,
            ValidationTotalRows      = 0
    WHERE   FileLogId = @FileLogId;

    BEGIN TRY
        ---------------------------------------------------------
        -- STAGE 1: IMPORT (Atomic -- unchanged)
        ---------------------------------------------------------
        BEGIN TRANSACTION;
            EXEC dbo.sp_ImportData_FromStaging @FileLogId, @ImportMode;
        COMMIT TRANSACTION;

        -- REFRESH CONTEXT AFTER COMMIT
        SELECT @TaxYear = PlanYear FROM dbo.UploadedFileLog WHERE FileLogId = @FileLogId;

        SELECT TOP 1 @EmpName = name, @TaxID = taxid
        FROM   dbo.Employer
        WHERE  FileLogId = @FileLogId AND companyId IS NULL;

        ---------------------------------------------------------
        -- STAGE 2: CALCULATIONS (one transaction per employer)
        ---------------------------------------------------------
        DECLARE @Employers TABLE (Seq INT IDENTITY(1,1) PRIMARY KEY,
                                  EmployerId INT,
                                  EmployerNm NVARCHAR(255));

        -- Parent first, then affiliates. Order is not required for correctness
        -- but makes the progress text read sensibly.
        INSERT  @Employers (EmployerId, EmployerNm)
        SELECT  id, name
        FROM    dbo.Employer
        WHERE   FileLogId = @FileLogId
          AND   filingYear = @TaxYear
        ORDER BY CASE WHEN companyId IS NULL THEN 0 ELSE 1 END, id;

        SELECT @Total = COUNT(*) FROM @Employers;

        UPDATE  dbo.UploadedFileLog
        SET     ValidationTotalRows   = @Total,
                ValidationCurrentStep = 'Calculating ACA codes'
        WHERE   FileLogId = @FileLogId;

        WHILE @Seq <= @Total
        BEGIN
            SELECT  @CurrentEmployerId = EmployerId,
                    @CurrentEmployerNm = EmployerNm
            FROM    @Employers
            WHERE   Seq = @Seq;

            SET @FailedOn = @CurrentEmployerNm
                          + ' (employer ' + CAST(@Seq AS VARCHAR(10))
                          + ' of ' + CAST(@Total AS VARCHAR(10)) + ')';

            /* One employer, one transaction. Every lock this takes is released
               before the next employer starts, instead of accumulating across
               the whole file. */
            BEGIN TRANSACTION;
                EXEC dbo.sp_Generate1095Codes          @CurrentEmployerId, @TaxYear;
                EXEC dbo.sp_RunDynamicFlagEngine       @CurrentEmployerId, @TaxYear;
                EXEC dbo.sp_CalculatePotentialPenalties @CurrentEmployerId, @TaxYear;
                EXEC dbo.sp_Generate1094CValues        @CurrentEmployerId, @TaxYear;
            COMMIT TRANSACTION;

            /* Deliberately outside the transaction: a poller reading this row
               must not have to wait on the calculation's locks to see progress. */
            UPDATE  dbo.UploadedFileLog
            SET     ValidationRowsProcessed  = @Seq,
                    ValidationProgressPercent = CASE WHEN @Total > 0
                                                     THEN CAST((@Seq * 100.0) / @Total AS DECIMAL(5,2))
                                                     ELSE 0 END,
                    ValidationCurrentStep    = LEFT('Calculated ' + @CurrentEmployerNm
                                                  + ' (' + CAST(@Seq AS VARCHAR(10))
                                                  + ' of ' + CAST(@Total AS VARCHAR(10)) + ')', 100)
            WHERE   FileLogId = @FileLogId;

            SET @Seq = @Seq + 1;
        END

        SET @FailedOn = N'the final status update';

        ---------------------------------------------------------
        -- SUCCESS STAMPING
        -- WorkflowStatusId is deliberately NOT written here. The Hangfire job
        -- owns it and is the only writer.
        ---------------------------------------------------------
        UPDATE  dbo.UploadedFileLog
        SET     ValidationStatus          = 'Processed',
                UploadStatus              = 'Completed',
                EmployerName              = ISNULL(@EmpName, EmployerName),
                TaxID                     = ISNULL(@TaxID, TaxID),
                ProcessingEndTime         = GETDATE(),
                ValidationProgressPercent = 100,
                ValidationRowsProcessed   = @Total,
                ValidationCurrentStep     = 'Complete'
        WHERE   FileLogId = @FileLogId;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;

        SET @ErrorMessage = ERROR_MESSAGE();

        /* Records which employer the failure came from. Everything calculated
           before it stays committed, so a re-run picks up from a known point. */
        UPDATE  dbo.UploadedFileLog
        SET     ValidationStatus      = 'Calculation Failed',
                UploadStatus          = 'Error',
                ProcessingEndTime     = GETDATE(),
                ValidationCurrentStep = LEFT('Failed on ' + @FailedOn, 100),
                Notes = 'Failed on ' + @FailedOn + '. Error: ' + @ErrorMessage
        WHERE   FileLogId = @FileLogId;

        /* THROW, not RAISERROR: RAISERROR treats its first argument as a format
           string, so an error message containing a percent sign would itself
           fail to format. THROW re-raises the original untouched. */
        THROW;
    END CATCH
END
GO

/* -------------------------------------------------------------- VERIFY ----- */

PRINT '';
PRINT '=== 1. Stage 2 now commits per employer ===';
SELECT Result = CASE
    WHEN OBJECT_DEFINITION(OBJECT_ID('dbo.sp_ProcessEmployerData')) LIKE '%One employer, one transaction%'
    THEN 'PASS' ELSE 'FAIL' END;

PRINT '=== 2. The invalid workflow status writes are gone ===';
PRINT '    40 is a validation status, not a workflow status; -1 is nothing at all.';
SELECT  Check_ = 'writes WorkflowStatusId = 40',
        Result = CASE WHEN OBJECT_DEFINITION(OBJECT_ID('dbo.sp_ProcessEmployerData')) LIKE '%WorkflowStatusId = 40%'
                      THEN 'FAIL - still there' ELSE 'PASS' END
UNION ALL
SELECT  'writes WorkflowStatusId = -1',
        CASE WHEN OBJECT_DEFINITION(OBJECT_ID('dbo.sp_ProcessEmployerData')) LIKE '%WorkflowStatusId = -1%'
             THEN 'FAIL - still there' ELSE 'PASS' END
UNION ALL
SELECT  'cursor removed',
        CASE WHEN OBJECT_DEFINITION(OBJECT_ID('dbo.sp_ProcessEmployerData')) LIKE '%employer_cursor%'
             THEN 'FAIL - still there' ELSE 'PASS' END;

PRINT '=== 3. Any file left parked on an invalid workflow status ===';
PRINT '    Rows here were stranded by the old procedure. 40 and -1 are not';
PRINT '    workflow statuses, so no screen knows what to do with them.';
SELECT  l.FileLogId, l.OriginalFileName, l.WorkflowStatusId, l.ValidationStatusId,
        l.UploadStatus, l.ValidationStatus, l.ProcessingStartTime, l.ProcessingEndTime
FROM    dbo.UploadedFileLog l
WHERE   ISNULL(l.IsDeleted, 0) = 0
  AND   (l.WorkflowStatusId NOT BETWEEN 200 AND 299 OR l.WorkflowStatusId IS NULL)
ORDER BY l.FileLogId;
PRINT '    No rows is what you want. If any appear, decide per file whether it';
PRINT '    belongs at 220 (back to the analyst) or 260 (already imported) --';
PRINT '    check ProcessingEndTime and whether its employers have codes.';

PRINT '=== 4. Progress columns after the next import ===';
SELECT  l.FileLogId, l.OriginalFileName,
        l.ValidationCurrentStep, l.ValidationProgressPercent,
        l.ValidationRowsProcessed, l.ValidationTotalRows,
        Elapsed = DATEDIFF(SECOND, l.ProcessingStartTime, ISNULL(l.ProcessingEndTime, GETDATE()))
FROM    dbo.UploadedFileLog l
WHERE   ISNULL(l.IsDeleted, 0) = 0
  AND   l.ProcessingStartTime IS NOT NULL
ORDER BY l.ProcessingStartTime DESC;
GO
