/* =============================================================================
   001_F-03_seed_filingyear_penalty_rates.sql

   FIXES   F-03 — code generation cannot run for any filing year before 2025.

   WHY     dbo.sp_Generate1095Codes and dbo.sp_CalculatePotentialPenalties both
           open by requiring all five of fpgPremium, fpgPercent, ropHours,
           PenaltyA_Annual and PenaltyB_Annual to be non-null for the requested
           year, and RAISERROR + RETURN otherwise.

           FilingYear currently has PenaltyA_Annual and PenaltyB_Annual populated
           for 2025 and 2026 only. For 2017-2024 the other three columns are set
           but the two penalty columns are NULL, so those years hard-fail.

           Because sp_ProcessEmployerData calls sp_Generate1095Codes inside its
           calculation transaction, a prior-year import does not merely skip
           coding — it rolls back and the file is stamped 'Calculation Failed'.
           Prior-year corrections are a normal part of ACA work and are currently
           unreachable.

   WHAT    Fills only the NULL penalty columns for 2017-2024 with the published
           section 4980H(a) and 4980H(b) annual amounts. Existing non-null values
           are never overwritten.

   SAFE TO RE-RUN
           Yes. The UPDATE uses COALESCE per column and is scoped to rows that
           still have a NULL, so a second run changes nothing.

   SCOPE   Data only. No schema change, no procedure change.

   NOTE    These figures drive the penalty exposure shown to clients. They were
           confirmed against the caller's own source before this script was
           written; re-confirm before running in production.

   RELATED The System settings page has an "IRS Penalty" tab that calls an action
           named GetIrsPenaltyRates, which does not exist in any controller. The
           two procedures behind it, sp_GetIrsPenaltyRates and
           sp_SaveIrsPenaltyRate, read and write dbo.tbl_IrsPenaltyRates, which
           is not a table in this database. That whole path is inert — the values
           the engines actually read are the FilingYear columns this script sets.
           Tracked separately as F-33; do not expect the admin screen to maintain
           these until it is rebuilt against FilingYear.
   ============================================================================= */

USE [db_ACA360];
GO

SET NOCOUNT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    /* -----------------------------------------------------------------------
       Published annual 4980H amounts. Kept as data rather than a chain of
       UPDATE statements so the whole set can be read and checked at a glance.
       ----------------------------------------------------------------------- */
    DECLARE @Updated INT;

    DECLARE @Rates TABLE
    (
        FilingYear INT PRIMARY KEY,
        PenaltyA   MONEY NOT NULL,   -- 4980H(a), annual, per full-time employee
        PenaltyB   MONEY NOT NULL    -- 4980H(b), annual, per employee-month x 12
    );

    INSERT INTO @Rates (FilingYear, PenaltyA, PenaltyB) VALUES
        (2017, 2260.0000, 3390.0000),
        (2018, 2320.0000, 3480.0000),
        (2019, 2500.0000, 3750.0000),
        (2020, 2570.0000, 3860.0000),
        (2021, 2700.0000, 4060.0000),
        (2022, 2750.0000, 4120.0000),
        (2023, 2880.0000, 4320.0000),
        (2024, 2970.0000, 4460.0000);
    /* 2025 (2900 / 4350) and 2026 (3340 / 5010) are already seeded and are
       deliberately not listed here. */

    /* ---------------------------------------------------- before ----------- */
    PRINT '--- FilingYear penalty columns BEFORE ---';
    SELECT  fy.filingYear,
            fy.PenaltyA_Annual,
            fy.PenaltyB_Annual,
            CanGenerateCodes =
                CASE WHEN fy.fpgPremium      IS NOT NULL
                      AND fy.fpgPercent      IS NOT NULL
                      AND fy.ropHours        IS NOT NULL
                      AND fy.PenaltyA_Annual IS NOT NULL
                      AND fy.PenaltyB_Annual IS NOT NULL
                     THEN 'yes' ELSE 'NO' END
    FROM    dbo.FilingYear fy
    ORDER BY fy.filingYear;

    /* ---------------------------------------------------- update ----------- */
    /* COALESCE per column: a year with only one of the two set keeps the value
       it already has and gains only the missing one. */
    UPDATE  fy
    SET     fy.PenaltyA_Annual = COALESCE(fy.PenaltyA_Annual, r.PenaltyA),
            fy.PenaltyB_Annual = COALESCE(fy.PenaltyB_Annual, r.PenaltyB)
    FROM    dbo.FilingYear fy
    JOIN    @Rates r ON r.FilingYear = fy.filingYear
    WHERE   fy.PenaltyA_Annual IS NULL
       OR   fy.PenaltyB_Annual IS NULL;

    SET @Updated = @@ROWCOUNT;   -- read immediately; any statement in between resets it
    PRINT '--- Rows updated: ' + CAST(@Updated AS VARCHAR(10)) + ' ---';

    /* --------------------------------------- years not present at all ------ */
    /* Nothing is inserted: a filing year that does not exist needs fpgPremium,
       fpgPercent, ropHours and offerPercent too, and those are not this script's
       to invent. Report them so the gap is visible rather than silent. */
    IF EXISTS (SELECT 1 FROM @Rates r
               WHERE NOT EXISTS (SELECT 1 FROM dbo.FilingYear fy
                                 WHERE fy.filingYear = r.FilingYear))
    BEGIN
        PRINT '--- WARNING: expected years with no FilingYear row (not created) ---';
        SELECT  MissingFilingYear = r.FilingYear
        FROM    @Rates r
        WHERE   NOT EXISTS (SELECT 1 FROM dbo.FilingYear fy
                            WHERE fy.filingYear = r.FilingYear)
        ORDER BY r.FilingYear;
    END

    /* ---------------------------------------------------- after ------------ */
    PRINT '--- FilingYear penalty columns AFTER ---';
    SELECT  fy.filingYear,
            fy.PenaltyA_Annual,
            fy.PenaltyB_Annual,
            CanGenerateCodes =
                CASE WHEN fy.fpgPremium      IS NOT NULL
                      AND fy.fpgPercent      IS NOT NULL
                      AND fy.ropHours        IS NOT NULL
                      AND fy.PenaltyA_Annual IS NOT NULL
                      AND fy.PenaltyB_Annual IS NOT NULL
                     THEN 'yes' ELSE 'NO' END
    FROM    dbo.FilingYear fy
    ORDER BY fy.filingYear;

    /* ------------------------------------------- remaining blockers -------- */
    IF EXISTS (SELECT 1 FROM dbo.FilingYear
               WHERE IsActive = 1
                 AND (fpgPremium      IS NULL
                   OR fpgPercent      IS NULL
                   OR ropHours        IS NULL
                   OR PenaltyA_Annual IS NULL
                   OR PenaltyB_Annual IS NULL))
    BEGIN
        PRINT '--- WARNING: active years that still cannot generate codes ---';
        SELECT  fy.filingYear,
                MissingColumns =
                    STUFF(
                        CASE WHEN fy.fpgPremium      IS NULL THEN ', fpgPremium'      ELSE '' END +
                        CASE WHEN fy.fpgPercent      IS NULL THEN ', fpgPercent'      ELSE '' END +
                        CASE WHEN fy.ropHours        IS NULL THEN ', ropHours'        ELSE '' END +
                        CASE WHEN fy.PenaltyA_Annual IS NULL THEN ', PenaltyA_Annual' ELSE '' END +
                        CASE WHEN fy.PenaltyB_Annual IS NULL THEN ', PenaltyB_Annual' ELSE '' END,
                        1, 2, '')
        FROM    dbo.FilingYear fy
        WHERE   fy.IsActive = 1
          AND  (fy.fpgPremium      IS NULL
             OR fy.fpgPercent      IS NULL
             OR fy.ropHours        IS NULL
             OR fy.PenaltyA_Annual IS NULL
             OR fy.PenaltyB_Annual IS NULL)
        ORDER BY fy.filingYear;
    END
    ELSE
    BEGIN
        PRINT '--- All active filing years can now generate codes. ---';
    END

    COMMIT TRANSACTION;
    PRINT '--- Committed. ---';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT '--- Rolled back. No changes were made. ---';
    THROW;
END CATCH
GO
