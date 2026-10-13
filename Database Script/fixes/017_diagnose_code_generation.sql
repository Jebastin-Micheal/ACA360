/* =============================================================================
   017_diagnose_code_generation.sql

   READ-ONLY. Makes no changes. Run it and send me the output.

   WHY     The 1095-C code report for ATLANTIC HEALTH PARTNERS INC (2024) shows
           three faults against a data file that validated clean:

           1. Line 15 is EMPTY for every employee, including those enrolled in a
              plan carrying a $165 monthly contribution.

           2. Every employee collapsed to a single code for all twelve months.
              Three of them have mid-year events and cannot be uniform:
                 MARCUS DELANEY     hired 11 Mar      -> shows 1E/2C for all 12
                 REBECCA LINDQVIST  COBRA from 1 Jun  -> shows 1H/2A for all 12
                 GLORIA WHITFIELD   retired 31 Jul    -> shows 1H/2A for all 12

           3. Only 12 of the 14 employees appear. The two missing are exactly the
              two carrying the affiliate EIN 45-3387292.

   WHAT THE REPORT ALREADY PROVES
           sp_Generate1095Codes sets Line15 = Prem whenever the employee has a
           plan with minimum value. Sarah Mitchell has both -- Line 14 came out
           1E, which is only reachable when PlanId is not null and mv = 1 -- yet
           her Line 15 is empty. So Prem is NULL, so #PP came back empty:

               INSERT #PP SELECT pr.EmployerPlanId, m.MonthNum, MIN(pr.amount)
               FROM   Premium pr
               JOIN   #M m ON pr.bandingStartDate <= m.ME
                          AND (pr.bandingEndDate IS NULL OR pr.bandingEndDate >= m.MS)
               WHERE  pr.EmployerId = @EmployerId

           A NULL Prem also silently disables all three affordability safe
           harbours, because 2G, 2H and 2F are each decided by comparing against
           it. That is why HELENA VOSS and THOMAS ABERNATHY -- full time, offered
           coverage, not enrolled -- came back with NO Line 16 at all. Section 1
           shows which part of that join failed.

   THE OPEN QUESTION is section 2: whether the month collapse comes from the
           period tables (EmployeeHireSpan / EmployeeStatus / EmployeeEnrollment)
           or from the snapshot fallback. Note the trap in the procedure -- the
           fallback is gated on HasSpan = 0, so ONE unusable span row yields
           "never employed" for all twelve months and the fallback never runs.
   ============================================================================= */

USE [db_ACA360];
GO
SET NOCOUNT ON;
GO

DECLARE @MainEIN VARCHAR(20) = '453387291';
DECLARE @AffEIN  VARCHAR(20) = '453387292';
DECLARE @Year    INT         = 2024;

DECLARE @EmployerId INT =
    (SELECT MIN(id) FROM dbo.Employer
     WHERE REPLACE(ISNULL(taxid, ''), '-', '') = @MainEIN
       AND ISNULL(IsDeleted, 0) = 0);

PRINT '===========================================================================';
PRINT ' 0. WHICH EMPLOYERS CAME OUT OF THE FILE';
PRINT '===========================================================================';
PRINT '    Expect TWO rows: main 45-3387291 and affiliate 45-3387292.';
PRINT '    If only one exists, the affiliate was never created and its two';
PRINT '    employees have no employer to be reported under.';
SELECT  EmployerId    = e.id,
        e.taxid,
        EmployerName  = e.name,
        e.filingYear,
        e.isAggregatedAle,
        e.isAuthoritative,
        e.FileLogId,
        UsedByReport  = CASE WHEN e.id = @EmployerId THEN 'yes' ELSE '' END
FROM    dbo.Employer e
WHERE   REPLACE(ISNULL(e.taxid, ''), '-', '') IN (@MainEIN, @AffEIN)
ORDER BY e.filingYear, e.taxid;

PRINT '';
PRINT '===========================================================================';
PRINT ' 1. WHY LINE 15 IS EMPTY  --  the Premium rows behind #PP';
PRINT '===========================================================================';
PRINT '    Three things must all hold for a premium to reach the codes:';
PRINT '      (a) a Premium row exists carrying this EmployerId';
PRINT '      (b) bandingStartDate is NOT NULL and falls on or before a month end';
PRINT '      (c) EmployerPlanId resolves to a real EmployerPlan row';

SELECT  PremiumRowsForThisEmployer = COUNT(*) FROM dbo.Premium WHERE EmployerId = @EmployerId;
SELECT  PremiumRowsForAnyEmployer  = COUNT(*) FROM dbo.Premium;

SELECT  p.id,
        p.EmployerPlanId,
        PlanName = pl.name,
        p.bandingValueStart,
        p.bandingValueEnd,
        p.bandingStartDate,
        p.bandingEndDate,
        p.amount,
        p.EmployerId,
        p.FileLogId,
        Verdict = CASE
            WHEN p.bandingStartDate IS NULL
                THEN 'DEAD - bandingStartDate is NULL, the join drops the row'
            WHEN p.bandingStartDate > DATEFROMPARTS(@Year, 12, 31)
                THEN 'DEAD - starts after the plan year'
            WHEN p.bandingEndDate IS NOT NULL AND p.bandingEndDate < DATEFROMPARTS(@Year, 1, 1)
                THEN 'DEAD - ends before the plan year'
            WHEN pl.id IS NULL
                THEN 'DEAD - EmployerPlanId does not resolve to a plan'
            ELSE 'usable' END
FROM    dbo.Premium p
LEFT JOIN dbo.EmployerPlan pl ON pl.id = p.EmployerPlanId
WHERE   p.EmployerId = @EmployerId OR p.EmployerId IS NULL
ORDER BY p.EmployerPlanId;

PRINT '    -- The exact #PP the procedure builds, month by month --';
PRINT '       No rows here means Line 15 is blank on every form and no';
PRINT '       affordability safe harbour can ever be assigned.';
;WITH M AS (
    SELECT MonthNum = n,
           MS = DATEFROMPARTS(@Year, n, 1),
           ME = EOMONTH(DATEFROMPARTS(@Year, n, 1))
    FROM  (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12)) v(n)
)
SELECT  pr.EmployerPlanId, m.MonthNum, Prem = MIN(pr.amount)
FROM    dbo.Premium pr
JOIN    M m ON pr.bandingStartDate <= m.ME
           AND (pr.bandingEndDate IS NULL OR pr.bandingEndDate >= m.MS)
WHERE   pr.EmployerId = @EmployerId
GROUP BY pr.EmployerPlanId, m.MonthNum
ORDER BY pr.EmployerPlanId, m.MonthNum;

PRINT '    -- The plans themselves, to confirm the offer side imported --';
SELECT  pl.id, PlanName = pl.name, pl.medicalPlan, pl.minimumValue,
        pl.offeredSpouse, pl.conditionallyOffSpouse, pl.offeredDependents,
        pl.fundingType, pl.waitingDays, pl.planRenewal,
        pl.code1A, pl.code2F, pl.code2G, pl.code2H
FROM    dbo.EmployerPlan pl
WHERE   pl.employerId = @EmployerId
ORDER BY pl.name;

PRINT '    -- What the spreadsheet supplied, for comparison --';
SELECT  sp.PlanName, sp.BandingType, sp.[Start], sp.[End],
        sp.StartDate, sp.EndDate, sp.EEMonthlyContribution
FROM    dbo.Staging_Premiums sp
WHERE   sp.FileLogId = (SELECT MAX(FileLogId) FROM dbo.UploadedFileLog
                        WHERE PlanYear = @Year AND IsDeleted = 0)
ORDER BY sp.PlanName;

PRINT '';
PRINT '===========================================================================';
PRINT ' 2. WHY EVERY EMPLOYEE COLLAPSED TO ONE CODE  --  the period tables';
PRINT '===========================================================================';
PRINT '    HasSpan / HasStatus / HasEnr decide whether the procedure uses the';
PRINT '    period tables or the flat snapshot columns. The fallback runs ONLY';
PRINT '    when the period table is empty for that employee, so one wrong span';
PRINT '    row silently overrides otherwise-correct snapshot dates.';

SELECT  Employee     = e.firstName + ' ' + e.lastName,
        e.ssn,
        SnapHire     = e.HireDate,
        SnapTerm     = e.TerminationDate,
        SnapStatus   = e.[Status],
        SnapPlan     = e.LowestCostPlanOffered,
        SnapElected  = e.CoverageElected,
        SnapCovStart = e.CoverageStartDate,
        SnapCovEnd   = e.CoverageEndDate,
        SnapCOBRA    = e.COBRABenefitsElected,
        SnapCobStart = e.COBRAEffectiveDate,
        SnapCobEnd   = e.COBRAEndDate,
        SnapUnion    = e.IsUnionEmployee,
        HasSpan      = CASE WHEN EXISTS (SELECT 1 FROM dbo.EmployeeHireSpan   x WHERE x.employeeCodeId = ec.id) THEN 1 ELSE 0 END,
        HasStatus    = CASE WHEN EXISTS (SELECT 1 FROM dbo.EmployeeStatus     x WHERE x.employeeCodeId = ec.id) THEN 1 ELSE 0 END,
        HasEnr       = CASE WHEN EXISTS (SELECT 1 FROM dbo.EmployeeEnrollment x WHERE x.employeeCodeId = ec.id) THEN 1 ELSE 0 END,
        HasPayroll   = CASE WHEN EXISTS (SELECT 1 FROM dbo.EmployeePayroll    x WHERE x.employeeCodeId = ec.id) THEN 1 ELSE 0 END,
        CodeRow      = ec.id
FROM    dbo.Employee e
LEFT JOIN dbo.EmployeeCode ec ON ec.employeeId = e.id AND ec.filingYear = @Year
WHERE   e.EmployerId = @EmployerId AND ISNULL(e.IsDeleted, 0) = 0
ORDER BY e.lastName;

PRINT '    -- EmployeeHireSpan. These decide the Employed flag month by month. --';
PRINT '       DELANEY should start 2024-03-11; LINDQVIST should end 2024-05-31;';
PRINT '       WHITFIELD should end 2024-07-31.';
SELECT  Employee = e.firstName + ' ' + e.lastName,
        h.startDate, h.endDate,
        CoversJan = CASE WHEN h.startDate <= DATEFROMPARTS(@Year,1,31)
                          AND (h.endDate IS NULL OR h.endDate >= DATEFROMPARTS(@Year,1,1))
                         THEN 'yes' ELSE 'no' END,
        CoversJun = CASE WHEN h.startDate <= DATEFROMPARTS(@Year,6,30)
                          AND (h.endDate IS NULL OR h.endDate >= DATEFROMPARTS(@Year,6,1))
                         THEN 'yes' ELSE 'no' END,
        CoversDec = CASE WHEN h.startDate <= DATEFROMPARTS(@Year,12,31)
                          AND (h.endDate IS NULL OR h.endDate >= DATEFROMPARTS(@Year,12,1))
                         THEN 'yes' ELSE 'no' END
FROM    dbo.EmployeeHireSpan h
JOIN    dbo.EmployeeCode ec ON ec.id = h.employeeCodeId
JOIN    dbo.Employee e ON e.id = ec.employeeId
WHERE   e.EmployerId = @EmployerId
ORDER BY e.lastName, h.startDate;

PRINT '    -- EmployeeStatus. status = 1 is full time. --';
SELECT  Employee = e.firstName + ' ' + e.lastName,
        s.[status], s.startDate, s.endDate
FROM    dbo.EmployeeStatus s
JOIN    dbo.EmployeeCode ec ON ec.id = s.employeeCodeId
JOIN    dbo.Employee e ON e.id = ec.employeeId
WHERE   e.EmployerId = @EmployerId
ORDER BY e.lastName, s.startDate;

PRINT '    -- EmployeeEnrollment, including the COBRA and retiree windows --';
SELECT  Employee = e.firstName + ' ' + e.lastName,
        en.planId, en.isEnrolled, en.coverageStartDate, en.coverageEndDate,
        en.unionMember,
        en.COBRAEnrolled, en.COBRAStartDate, en.COBRAEndDate,
        en.RetireeEnrolled, en.RetireeStartDate, en.RetireeEndDate
FROM    dbo.EmployeeEnrollment en
JOIN    dbo.EmployeeCode ec ON ec.id = en.employeeCodeId
JOIN    dbo.Employee e ON e.id = ec.employeeId
WHERE   e.EmployerId = @EmployerId
ORDER BY e.lastName, en.coverageStartDate;

PRINT '';
PRINT '===========================================================================';
PRINT ' 3. THE PAYROLL ROWS BEHIND #Pay';
PRINT '===========================================================================';
PRINT '    2H needs Hourly > 0 and 2F needs AnnualW2 > 0, but both ALSO compare';
PRINT '    against Prem -- so if section 1 came back empty neither can fire no';
PRINT '    matter what is here.';
PRINT '    THOMAS ABERNATHY supplied an hourly rate of 21.25 and HELENA VOSS a';
PRINT '    W-2 of 47000. sp_Generate1095Codes reads payPeriodTotalHours as the';
PRINT '    hourly RATE (its own comment says so). Note that this table also has a';
PRINT '    payPeriodHourlyAmount column, and sp_AffordabilityExport reads the two';
PRINT '    the other way round -- rate from payPeriodHourlyAmount, hours summed';
PRINT '    from payPeriodTotalHours. Whichever the importer actually fills, one of';
PRINT '    those two procedures is reading the wrong column. These values settle';
PRINT '    which one.';
SELECT  Employee = e.firstName + ' ' + e.lastName,
        pr.payPeriodStartDate, pr.payPeriodEndDate,
        pr.payPeriodTotalHours,
        pr.payPeriodHourlyAmount,
        pr.payPeriodSalaryAmount,
        pr.payPeriodAdditional
FROM    dbo.EmployeePayroll pr
JOIN    dbo.EmployeeCode ec ON ec.id = pr.employeeCodeId
JOIN    dbo.Employee e ON e.id = ec.employeeId
WHERE   e.EmployerId = @EmployerId
ORDER BY e.lastName;

PRINT '';
PRINT '===========================================================================';
PRINT ' 4. WHERE THE TWO AFFILIATE EMPLOYEES WENT';
PRINT '===========================================================================';
PRINT '    CARMEN DELACROIX and VICTOR NAKASHIMA carry EIN 45-3387292.';
PRINT '    Victor is also the only union employee, so 2E is untested until he';
PRINT '    appears somewhere.';
SELECT  Employee     = e.firstName + ' ' + e.lastName,
        e.ssn,
        e.EmployerId,
        EmployerTaxId = er.taxid,
        EmployerName  = er.name,
        e.FileLogId,
        HasCodeRow    = CASE WHEN ec.id IS NULL THEN 'NO - no EmployeeCode row' ELSE 'yes' END
FROM    dbo.Employee e
LEFT JOIN dbo.Employer er ON er.id = e.EmployerId
LEFT JOIN dbo.EmployeeCode ec ON ec.employeeId = e.id AND ec.filingYear = @Year
WHERE   e.lastName IN ('DELACROIX', 'NAKASHIMA');

PRINT '    -- and what staging held for them --';
SELECT  se.EmployeeLegalFirstName, se.EmployeeLegalLastName,
        se.PrimaryEIN, se.EINAssociatedWithEE, se.EmployeeSSN
FROM    dbo.Staging_Employees se
WHERE   se.FileLogId = (SELECT MAX(FileLogId) FROM dbo.UploadedFileLog
                        WHERE PlanYear = @Year AND IsDeleted = 0)
  AND   ISNULL(se.EINAssociatedWithEE, '') <> ISNULL(se.PrimaryEIN, '');

PRINT '';
PRINT '===========================================================================';
PRINT ' 5. THE FILING YEAR CONSTANTS THE CODES WERE BUILT FROM';
PRINT '===========================================================================';
PRINT '    sp_Generate1095Codes raises an error if any of these is NULL and it';
PRINT '    did not raise, so they are populated. Printed to confirm the values';
PRINT '    are the real 2024 figures. fpgPremium is the monthly FPL amount that';
PRINT '    decides 2G, fpgPercent the affordability rate, ropHours the';
PRINT '    rate-of-pay hours (130) behind 2H.';
SELECT  filingYear, fpgPremium, fpgPercent, ropHours, offerPercent,
        PenaltyA_Annual, PenaltyB_Annual, IsActive
FROM    dbo.FilingYear
WHERE   filingYear BETWEEN @Year - 1 AND @Year + 1
ORDER BY filingYear;

PRINT '';
PRINT '===========================================================================';
PRINT ' 6. THE GENERATED CODES AS STORED';
PRINT '===========================================================================';
PRINT '    The ALL columns are only populated when all twelve months agree.';
PRINT '    Any employee with a value in ALLM_COC and nothing in JAN..DEC has';
PRINT '    genuinely been coded identically for the whole year.';
SELECT  Employee = e.firstName + ' ' + e.lastName,
        ec.ALLM_COC, ec.ALLM_LCMP, ec.ALLM_SHC,
        ec.JAN_COC, ec.FEB_COC, ec.MAR_COC, ec.APR_COC, ec.MAY_COC, ec.JUN_COC,
        ec.JUL_COC, ec.AUG_COC, ec.SEP_COC, ec.OCT_COC, ec.NOV_COC, ec.DEC_COC,
        ec.JAN_LCMP, ec.JUN_LCMP, ec.DEC_LCMP,
        ec.JAN_SHC, ec.JUN_SHC, ec.DEC_SHC,
        ec.IsAtPenaltyRisk, ec.PenaltyMonths, ec.EstimatedPenalty
FROM    dbo.EmployeeCode ec
JOIN    dbo.Employee e ON e.id = ec.employeeId
WHERE   ec.employerId = @EmployerId AND ec.filingYear = @Year
ORDER BY e.lastName;
GO
