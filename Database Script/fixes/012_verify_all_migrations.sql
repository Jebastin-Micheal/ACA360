/* =============================================================================
   012_verify_all_migrations.sql

   Read-only. Changes nothing. Run it any time to confirm scripts 001-011 are
   still in place — after a restore, before a filing run, or when something looks
   wrong and you want to rule the migrations in or out.

   Every check prints PASS or FAIL with what it actually found, so a FAIL tells
   you which script to re-run rather than just that something is off.

   Procedure checks read OBJECT_DEFINITION and look for the marker comments and
   code the migrations introduced. That detects a procedure reverted by a restore
   or overwritten by a later deployment.
   ============================================================================= */

USE [db_ACA360];
GO

SET NOCOUNT ON;
GO

DECLARE @R TABLE
(
    Seq     INT IDENTITY(1,1),
    Script  VARCHAR(10),
    Finding VARCHAR(80),
    Status  VARCHAR(6),
    Detail  NVARCHAR(400)
);

DECLARE @n INT, @m INT, @txt NVARCHAR(MAX);

/* ---------------------------------------------------------------- 001 ----- */
SELECT @n = COUNT(*) FROM dbo.FilingYear
WHERE IsActive = 1 AND (PenaltyA_Annual IS NULL OR PenaltyB_Annual IS NULL
                        OR fpgPremium IS NULL OR fpgPercent IS NULL OR ropHours IS NULL);
INSERT @R VALUES ('001','F-03 filing years can generate codes',
    CASE WHEN @n = 0 THEN 'PASS' ELSE 'FAIL' END,
    CAST(@n AS VARCHAR(10)) + ' active year(s) still incomplete');

/* ---------------------------------------------------------------- 002 ----- */
SELECT @n = COUNT(*) FROM dbo.tbl_User WHERE Flag IS NULL;
INSERT @R VALUES ('002','F-02 no NULL Flag remains',
    CASE WHEN @n = 0 THEN 'PASS' ELSE 'FAIL' END,
    CAST(@n AS VARCHAR(10)) + ' user(s) with NULL Flag');

SELECT @n = COUNT(*) FROM sys.default_constraints dc
JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
WHERE dc.parent_object_id = OBJECT_ID(N'dbo.tbl_User') AND c.name = 'Flag';
INSERT @R VALUES ('002','F-02 DEFAULT on tbl_User.Flag',
    CASE WHEN @n = 1 THEN 'PASS' ELSE 'FAIL' END,
    CAST(@n AS VARCHAR(10)) + ' default constraint(s)');

SET @txt = OBJECT_DEFINITION(OBJECT_ID('dbo.sp_Login_access'));
INSERT @R VALUES ('002','F-02 login rejects deactivated accounts',
    CASE WHEN @txt LIKE '%ISNULL(u.Flag, 1) <> 0%' THEN 'PASS' ELSE 'FAIL' END,
    CASE WHEN @txt LIKE '%ISNULL(u.Flag, 1) <> 0%' THEN 'predicate present'
         ELSE 'sp_Login_access has no Flag predicate' END);

/* ---------------------------------------------------------------- 003 ----- */
SELECT @n = COUNT(*) FROM sys.parameters
WHERE object_id = OBJECT_ID('dbo.sp_GetDownloadHistory');
INSERT @R VALUES ('003','F-30 download history is scoped',
    CASE WHEN @n = 3 THEN 'PASS' ELSE 'FAIL' END,
    CAST(@n AS VARCHAR(10)) + ' parameter(s); expected 3');

/* ---------------------------------------------------------------- 004 ----- */
SELECT @n = COUNT(*) FROM dbo.Premium P
JOIN dbo.EmployerPlan EP ON EP.id = P.EmployerPlanId
WHERE UPPER(LTRIM(RTRIM(ISNULL(EP.bandingType,'')))) IN ('SALARY','HOURLY','AGE','YEARS OF SERVICE')
  AND TRY_CAST(P.bandingValueStart AS DECIMAL(18,2)) IS NULL;
INSERT @R VALUES ('004','F-06 banded premiums have real boundaries',
    CASE WHEN @n = 0 THEN 'PASS' ELSE 'FAIL' END,
    CAST(@n AS VARCHAR(10)) + ' banded row(s) without a numeric boundary');

SET @txt = OBJECT_DEFINITION(OBJECT_ID('dbo.sp_Generate1095Codes'));
INSERT @R VALUES ('004','F-06 engine prices per employee',
    CASE WHEN @txt LIKE '%F-06: banded plans price per employee%' THEN 'PASS' ELSE 'FAIL' END,
    CASE WHEN @txt LIKE '%F-06: banded plans price per employee%' THEN 'band matching present'
         ELSE 'sp_Generate1095Codes reverted to MIN(amount) only' END);

/* ---------------------------------------------------------------- 005 ----- */
SET @txt = OBJECT_DEFINITION(OBJECT_ID('dbo.sp_ImportData_FromStaging'));
INSERT @R VALUES ('005','F-07 import carries dependent coverage dates',
    CASE WHEN @txt LIKE '%F-07: carry the dependent%' THEN 'PASS' ELSE 'FAIL' END,
    CASE WHEN @txt LIKE '%F-07: carry the dependent%' THEN 'all five columns carried'
         ELSE 'import reverted to name/ssn/dob only' END);

SELECT @n = COUNT(*), @m = SUM(CASE WHEN coverageStartDate IS NOT NULL THEN 1 ELSE 0 END)
FROM dbo.CoveredIndividual WHERE ISNULL(IsDeleted,0) = 0;
INSERT @R VALUES ('005','F-07 dependents have their own dates',
    CASE WHEN @n = 0 OR @m > 0 THEN 'PASS' ELSE 'FAIL' END,
    CAST(ISNULL(@m,0) AS VARCHAR(10)) + ' of ' + CAST(@n AS VARCHAR(10)) + ' with a start date');

/* ---------------------------------------------------------------- 006 ----- */
SET @txt = OBJECT_DEFINITION(OBJECT_ID('dbo.sp_Validation_ExecuteRules'));
SET @n = CASE WHEN @txt LIKE '%GREATER_THAN_COLUMN%' THEN 1 ELSE 0 END
       + CASE WHEN @txt LIKE '%EXISTS_IN_TABLE%'     THEN 1 ELSE 0 END
       + CASE WHEN @txt LIKE '%REQUIRED_IF_EQUALS%'  THEN 1 ELSE 0 END
       + CASE WHEN @txt LIKE '%LESS_THAN_VALUE%'     THEN 1 ELSE 0 END
       + CASE WHEN @txt LIKE '%REQUIRED_IF_NULL%'    THEN 1 ELSE 0 END
       + CASE WHEN @txt LIKE '%LENGTH_EQUALS%'       THEN 1 ELSE 0 END
       + CASE WHEN @txt LIKE '%IS_NULL%'             THEN 1 ELSE 0 END
       + CASE WHEN @txt LIKE '%GREATER_THAN_VALUE%'  THEN 1 ELSE 0 END;
INSERT @R VALUES ('006','F-08 eight missing handlers present',
    CASE WHEN @n = 8 THEN 'PASS' ELSE 'FAIL' END,
    CAST(@n AS VARCHAR(10)) + ' of 8 handlers found');

/* Counting apostrophes here was a mistake and this check reported a false FAIL.
   The pattern '%''''''''%' looks like eight apostrophes but T-SQL collapses each
   doubled pair, so it searched for FOUR — and four consecutive apostrophes are
   the ordinary way to write an empty-string literal inside generated SQL. This
   procedure contains 37 of them legitimately, e.g.

       SET @Param1 = REPLACE(ISNULL(@Param1, ''), '''', '''''');

   The defect was EIGHT consecutive apostrophes, which would need sixteen in the
   pattern below. Rather than count quotes at all, test the two unambiguous
   things: the specific broken construct is absent, and the repair marker from
   script 006 is present. */
INSERT @R VALUES ('006','F-08 malformed literals repaired',
    CASE WHEN @txt LIKE '%''''''''2''''''''%'                        THEN 'FAIL'
         WHEN @txt LIKE '%the literals here were double-escaped%'    THEN 'PASS'
         ELSE 'FAIL' END,
    CASE WHEN @txt LIKE '%''''''''2''''''''%'
              THEN 'broken FundingType literal still present - re-run 006'
         WHEN @txt LIKE '%the literals here were double-escaped%'
              THEN 'repaired; both blocks carry the fix'
         ELSE 'repair marker absent - script 006 may have been reverted' END);

/* ---------------------------------------------------------------- 007 ----- */
SELECT @n = COUNT(DISTINCT Prem) FROM (
    SELECT Prem = EC.JAN_LCMP
    FROM dbo.EmployeeCode EC
    JOIN dbo.Employee E ON E.id = EC.employeeId
    JOIN dbo.EmployerPlan EP ON EP.employerId = EC.employerId
       AND REPLACE(LTRIM(RTRIM(EP.name)),CHAR(160),'')
         = REPLACE(LTRIM(RTRIM(ISNULL(E.LowestCostPlanOffered,''))),CHAR(160),'')
    WHERE UPPER(LTRIM(RTRIM(ISNULL(EP.bandingType,'')))) IN ('SALARY','HOURLY','AGE','YEARS OF SERVICE')
      AND EC.JAN_LCMP IS NOT NULL
) x;
INSERT @R VALUES ('007','F-06 line 15 varies by band',
    CASE WHEN @n = 0 THEN 'N/A' WHEN @n > 1 THEN 'PASS' ELSE 'FAIL' END,
    CASE WHEN @n = 0 THEN 'no employees on a banded plan'
         WHEN @n > 1 THEN CAST(@n AS VARCHAR(10)) + ' distinct premium(s) in use'
         ELSE 'only one premium in use - recalculation may not have run' END);

/* ---------------------------------------------------------------- 008 ----- */
INSERT @R VALUES ('008','F-16 Overwrite clears EmployeeStatus',
    CASE WHEN @txt IS NULL THEN 'FAIL' ELSE
    CASE WHEN OBJECT_DEFINITION(OBJECT_ID('dbo.sp_ImportData_FromStaging'))
              LIKE '%DELETE FROM EmployeeStatus WHERE employeeCodeId IN%'
         THEN 'PASS' ELSE 'FAIL' END END,
    'checks the Overwrite cleanup block');

INSERT @R VALUES ('008','F-17 import summary is derived',
    CASE WHEN OBJECT_DEFINITION(OBJECT_ID('dbo.sp_ImportData_FromStaging'))
              LIKE '%@MissingEmployees%' THEN 'PASS' ELSE 'FAIL' END,
    'IsImportComplete computed rather than hardcoded');

SET @n = CASE WHEN OBJECT_ID('dbo.sp_ReorderUserShortcuts','P') IS NOT NULL THEN 1 ELSE 0 END
       + CASE WHEN OBJECT_ID('dbo.sp_FilingYear_Delete','P')    IS NOT NULL THEN 1 ELSE 0 END;
INSERT @R VALUES ('008','F-24 two live procedures created',
    CASE WHEN @n = 2 THEN 'PASS' ELSE 'FAIL' END,
    CAST(@n AS VARCHAR(10)) + ' of 2 present');

/* ---------------------------------------------------------------- 009 ----- */
SELECT @n = COUNT(*) FROM dbo.tbl_User WHERE ISNULL(isMFA,0) = 1;
INSERT @R VALUES ('009','F-01 no account locked by the MFA flag',
    CASE WHEN @n = 0 THEN 'PASS' ELSE 'FAIL' END,
    CAST(@n AS VARCHAR(10)) + ' account(s) still flagged');

/* ---------------------------------------------------------------- 011 ----- */
SELECT @n = COUNT(*) FROM dbo.ValidationRules
WHERE RuleId = 27 AND RuleParameter1 = 'Affiliates_MustHaveEmployees' AND ISNULL(IsAutoCorrect,0) = 0;
INSERT @R VALUES ('011','F-08 rule 27 keyword and autocorrect',
    CASE WHEN @n = 1 THEN 'PASS' ELSE 'FAIL' END,
    CASE WHEN @n = 1 THEN 'keyword restored, autocorrect cleared'
         ELSE 'rule 27 still misconfigured' END);

/* --------------------------------------------------- cross-cutting checks -- */
SELECT @n = COUNT(*) FROM dbo.ValidationRules vr
WHERE vr.IsActive = 1 AND vr.ValidationType = 'CUSTOM_LOGIC'
  AND OBJECT_DEFINITION(OBJECT_ID('dbo.sp_Validation_ExecuteRules'))
      NOT LIKE '%@Param1 = ''' + vr.RuleParameter1 + '''%';
INSERT @R VALUES ('006/011','every CUSTOM_LOGIC keyword has a handler',
    CASE WHEN @n = 0 THEN 'PASS' ELSE 'FAIL' END,
    CAST(@n AS VARCHAR(10)) + ' active rule(s) with no handler');

SELECT @n = COUNT(*) FROM dbo.DB_Errors
WHERE ErrorProcedure = 'sp_Validation_ExecuteRules' AND ErrorNumber IN (50002, 50003)
  AND ErrorDateTime > DATEADD(DAY, -1, GETDATE());
INSERT @R VALUES ('006','no rule skipped in the last 24h',
    CASE WHEN @n = 0 THEN 'PASS' ELSE 'WARN' END,
    CAST(@n AS VARCHAR(10)) + ' skip(s) recorded; re-run 010 after fixing');

SELECT @n = COUNT(*) FROM dbo.ValidationRules
WHERE IsActive = 1 AND ISNULL(IsAutoCorrect,0) = 1 AND ValidationType = 'CUSTOM_LOGIC';
INSERT @R VALUES ('011','no inert-but-armed autocorrect rules',
    CASE WHEN @n = 0 THEN 'PASS' ELSE 'WARN' END,
    CAST(@n AS VARCHAR(10)) + ' CUSTOM_LOGIC rule(s) with IsAutoCorrect=1');

/* -------------------------------------------------------------- RESULTS ---- */
PRINT '=== Migration health check ===';
SELECT Script, Finding, Status, Detail FROM @R ORDER BY Seq;

PRINT '';
SELECT  Overall = CASE WHEN EXISTS (SELECT 1 FROM @R WHERE Status = 'FAIL')
                       THEN 'FAIL - see the rows above'
                       WHEN EXISTS (SELECT 1 FROM @R WHERE Status = 'WARN')
                       THEN 'PASS with warnings'
                       ELSE 'PASS - all migrations in place' END,
        Passed  = (SELECT COUNT(*) FROM @R WHERE Status = 'PASS'),
        Warned  = (SELECT COUNT(*) FROM @R WHERE Status = 'WARN'),
        Failed  = (SELECT COUNT(*) FROM @R WHERE Status = 'FAIL'),
        NotApplicable = (SELECT COUNT(*) FROM @R WHERE Status = 'N/A');
GO
