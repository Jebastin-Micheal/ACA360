/* =============================================================================
   007_post_migration_recalculate.sql

   RUN THIS AFTER 004, 005 and 006.

   WHY     Scripts 004 and 005 corrected stored data and the procedures that read
           it, but nothing recalculates by itself. EmployeeCode still holds the
           line 14/15/16 values from the last run, EmployerPenaltyCalculation
           still holds penalties derived from the old line 15, and Employer still
           holds 1094-C monthly counts derived from the old line 14.

   CORRECTION TO SCRIPT 004

           004's PART 4 emitted only sp_Generate1095Codes. That was incomplete.
           Line 15 feeds the 4980H(b) calculation and line 14 feeds the 1094-C
           minimum-essential-coverage test, so regenerating codes alone leaves
           both stale and internally inconsistent with the codes beside them.

           The correct sequence is the one sp_ProcessEmployerData itself runs
           after an import, in this order:

               sp_Generate1095Codes
               sp_RunDynamicFlagEngine
               sp_CalculatePotentialPenalties
               sp_Generate1094CValues

           PART 1 below runs exactly that, per employer and filing year.

   SCOPE   Every employer and filing year that has EmployeeCode rows. Deliberately
           not narrowed to banded plans only: 005 changed dependent coverage as
           well, and the flag engine reads those, so a full pass is the honest
           thing to do. On a database this size that is cheap; on a much larger
           one, narrow the cursor with the WHERE clause noted inline.

   LOCKED FILINGS ARE SKIPPED

           sp_Generate1095Codes already ignores EmployeeCode rows with IsLocked=1
           or disableCoding=1, so a finalised filing is not disturbed. Those rows
           keep their old line 15. PART 3 lists them, because a locked filing that
           was locked BEFORE these fixes still carries the understated figures and
           needs a deliberate decision — unlock and regenerate, or file a
           correction.

   SAFE TO RE-RUN
           Yes. Every procedure it calls is idempotent for a given employer/year.

   NOT TRANSACTIONAL ACROSS EMPLOYERS
           Each procedure manages its own transaction. If one employer fails, the
           loop records it and carries on rather than rolling back the rest; PART 2
           reports anything that failed.
   ============================================================================= */

USE [db_ACA360];
GO

SET NOCOUNT ON;
GO

/* ---------------------------------------------------------------- PART 1 ----
   Recalculate. One employer and filing year at a time, in the same order the
   import pipeline uses.
   ---------------------------------------------------------------------------- */

IF OBJECT_ID('tempdb..#Recalc') IS NOT NULL DROP TABLE #Recalc;
CREATE TABLE #Recalc
(
    EmployerId   INT,
    FilingYear   INT,
    EmployerName VARCHAR(255),
    Employees    INT,
    Status       VARCHAR(20)  NULL,
    FailedStep   VARCHAR(60)  NULL,
    ErrorMessage NVARCHAR(2000) NULL,
    PRIMARY KEY (EmployerId, FilingYear)
);

INSERT INTO #Recalc (EmployerId, FilingYear, EmployerName, Employees)
SELECT  EC.employerId,
        EC.filingYear,
        MAX(E.name),
        COUNT(DISTINCT EC.employeeId)
FROM    dbo.EmployeeCode EC
JOIN    dbo.Employer E ON E.id = EC.employerId
/* Narrow here if the estate is large, e.g.:
   WHERE EXISTS (SELECT 1 FROM dbo.EmployerPlan EP JOIN dbo.Premium PR ON PR.EmployerPlanId = EP.id
                 WHERE EP.employerId = EC.employerId
                   AND TRY_CAST(PR.bandingValueStart AS DECIMAL(18,2)) IS NOT NULL) */
GROUP BY EC.employerId, EC.filingYear;

PRINT '--- Employer/year combinations to recalculate ---';
SELECT EmployerId, FilingYear, EmployerName, Employees FROM #Recalc ORDER BY EmployerId, FilingYear;

DECLARE @EmployerId INT, @FilingYear INT, @Name VARCHAR(255), @Step VARCHAR(60);

DECLARE recalc_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT EmployerId, FilingYear, EmployerName FROM #Recalc ORDER BY EmployerId, FilingYear;

OPEN recalc_cursor;
FETCH NEXT FROM recalc_cursor INTO @EmployerId, @FilingYear, @Name;

WHILE @@FETCH_STATUS = 0
BEGIN
    BEGIN TRY
        SET @Step = 'sp_Generate1095Codes';
        EXEC dbo.sp_Generate1095Codes
             @EmployerId   = @EmployerId,
             @FilingYear   = @FilingYear,
             @CurrentUserId = 'MIGRATION-007';

        SET @Step = 'sp_RunDynamicFlagEngine';
        EXEC dbo.sp_RunDynamicFlagEngine @EmployerId = @EmployerId, @TaxYear = @FilingYear;

        SET @Step = 'sp_CalculatePotentialPenalties';
        EXEC dbo.sp_CalculatePotentialPenalties @EmployerId = @EmployerId, @TaxYear = @FilingYear;

        SET @Step = 'sp_Generate1094CValues';
        EXEC dbo.sp_Generate1094CValues @EmployerId = @EmployerId, @FilingYear = @FilingYear;

        UPDATE #Recalc SET Status = 'OK'
        WHERE EmployerId = @EmployerId AND FilingYear = @FilingYear;

        PRINT '  OK   ' + CAST(@EmployerId AS VARCHAR(10)) + ' / ' + CAST(@FilingYear AS VARCHAR(4))
            + '  ' + ISNULL(@Name, '');
    END TRY
    BEGIN CATCH
        /* A procedure that rolled its own transaction back can leave the batch
           doomed. Bail out rather than pressing on against an unusable session. */
        IF XACT_STATE() = -1
        BEGIN
            UPDATE #Recalc SET Status = 'FAILED', FailedStep = @Step, ErrorMessage = ERROR_MESSAGE()
            WHERE EmployerId = @EmployerId AND FilingYear = @FilingYear;

            PRINT '  ABORT at ' + CAST(@EmployerId AS VARCHAR(10)) + ' / ' + CAST(@FilingYear AS VARCHAR(4))
                + ' -- transaction is doomed, stopping.';
            BREAK;
        END

        UPDATE #Recalc SET Status = 'FAILED', FailedStep = @Step, ErrorMessage = ERROR_MESSAGE()
        WHERE EmployerId = @EmployerId AND FilingYear = @FilingYear;

        PRINT '  FAIL ' + CAST(@EmployerId AS VARCHAR(10)) + ' / ' + CAST(@FilingYear AS VARCHAR(4))
            + '  at ' + @Step + ': ' + ERROR_MESSAGE();
    END CATCH

    FETCH NEXT FROM recalc_cursor INTO @EmployerId, @FilingYear, @Name;
END

CLOSE recalc_cursor;
DEALLOCATE recalc_cursor;
GO

/* ---------------------------------------------------------------- PART 2 ----
   Outcome.
   ---------------------------------------------------------------------------- */
PRINT '--- Recalculation summary ---';
SELECT  Status = ISNULL(Status, 'NOT REACHED'), Combinations = COUNT(*), Employees = SUM(Employees)
FROM    #Recalc
GROUP BY ISNULL(Status, 'NOT REACHED');

IF EXISTS (SELECT 1 FROM #Recalc WHERE ISNULL(Status, '') <> 'OK')
BEGIN
    PRINT '--- Not recalculated: investigate before filing ---';
    SELECT EmployerId, FilingYear, EmployerName, Employees,
           Status = ISNULL(Status, 'NOT REACHED'), FailedStep, ErrorMessage
    FROM   #Recalc
    WHERE  ISNULL(Status, '') <> 'OK'
    ORDER BY EmployerId, FilingYear;
END
ELSE
    PRINT '--- All employer/year combinations recalculated. ---';
GO

/* ---------------------------------------------------------------- PART 3 ----
   Locked rows the recalculation deliberately skipped.

   sp_Generate1095Codes excludes IsLocked=1 and disableCoding=1. That is correct
   for a finalised filing, but a filing locked BEFORE these fixes still carries
   the understated line 15 from F-06 and the employee-span dependent coverage
   from F-07. Each needs a decision: unlock and regenerate, or file a correction.
   ---------------------------------------------------------------------------- */
PRINT '--- Locked or coding-disabled rows, skipped by the recalculation ---';
SELECT  EC.employerId,
        EC.filingYear,
        EmployerName    = E.name,
        LockedRows      = SUM(CASE WHEN EC.IsLocked = 1 THEN 1 ELSE 0 END),
        CodingDisabled  = SUM(CASE WHEN ISNULL(EC.disableCoding, 0) = 1 THEN 1 ELSE 0 END),
        OnBandedPlan    = SUM(CASE WHEN EXISTS (
                                        SELECT 1
                                        FROM   dbo.EmployerPlan EP
                                        JOIN   dbo.Premium PR ON PR.EmployerPlanId = EP.id
                                        WHERE  EP.employerId = EC.employerId
                                          AND  TRY_CAST(PR.bandingValueStart AS DECIMAL(18,2)) IS NOT NULL)
                                   THEN 1 ELSE 0 END)
FROM    dbo.EmployeeCode EC
JOIN    dbo.Employer E ON E.id = EC.employerId
WHERE   EC.IsLocked = 1 OR ISNULL(EC.disableCoding, 0) = 1
GROUP BY EC.employerId, EC.filingYear, E.name
ORDER BY EC.employerId, EC.filingYear;
PRINT '    Rows counted under OnBandedPlan are the ones whose line 15 is still understated.';

DROP TABLE #Recalc;
GO
