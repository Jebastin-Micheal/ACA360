/* =============================================================================
   058 - Why this code? (1095-C Line 14 / 15 / 16, month by month)

   sp_Generate1095Codes works each month out from the employee's hire spans,
   full-time status, plan offered, its premium, enrolment, union, COBRA, ICHRA
   and pay, and then threw that work away. It now keeps it, per employee code and
   month, in dbo.EmployeeCodeMonthDetail, with the year's limits it compared
   against (FPL, affordability %, full-time hours). The app explains each month's
   codes from these values, and says when the filed codes were changed by hand.

   Employees whose codes are locked or have coding switched off are not
   recalculated, so they keep the explanation of their last calculation (none
   until codes are generated once after this script).

   sp_Generate1095Codes is the database's current one with only the 058 block
   added before its temp tables are dropped. Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

IF OBJECT_ID('dbo.EmployeeCodeMonthDetail') IS NULL
BEGIN
    CREATE TABLE dbo.EmployeeCodeMonthDetail
    (
        EmployeeCodeId    INT            NOT NULL,
        MonthNum          TINYINT        NOT NULL,
        Employed          BIT            NULL,
        IsFullTime        BIT            NULL,
        PlanId            INT            NULL,
        PlanName          NVARCHAR(200)  NULL,
        MinimumValue      BIT            NULL,
        OfferedSpouse     INT            NULL,
        ConditionalSpouse INT            NULL,
        OfferedDependents INT            NULL,
        FundingType       NVARCHAR(10)   NULL,
        Code1A            BIT            NULL,
        Premium           DECIMAL(18,2)  NULL,
        Enrolled          BIT            NULL,
        UnionMonth        BIT            NULL,
        CobraMonth        BIT            NULL,
        IsIchra           BIT            NULL,
        IchraLcsp         DECIMAL(18,2)  NULL,
        IchraAllowance    DECIMAL(18,2)  NULL,
        IchraNetCost      DECIMAL(18,2)  NULL,
        HourlyRate        DECIMAL(18,4)  NULL,
        AnnualW2          DECIMAL(18,4)  NULL,
        HiredMonths       INT            NULL,
        MonthlyFpl        DECIMAL(10,5)  NULL,
        AffordabilityPct  DECIMAL(10,5)  NULL,
        FullTimeHours     INT            NULL,
        HireDate          DATE           NULL,
        TerminationDate   DATE           NULL,
        EligibleDate      DATE           NULL,
        Line14            VARCHAR(2)     NULL,
        Line15            DECIMAL(18,2)  NULL,
        Line16            VARCHAR(2)     NULL,
        CalculatedAt      DATETIME2(0)   NOT NULL CONSTRAINT DF_EmployeeCodeMonthDetail_At DEFAULT SYSUTCDATETIME(),
        CalculatedBy      NVARCHAR(128)  NULL,
        CONSTRAINT PK_EmployeeCodeMonthDetail PRIMARY KEY (EmployeeCodeId, MonthNum)
    );
END
GO

------------------------------------------------------------
-- One employee's year: the 12 months as calculated, and the codes on file now.
------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_GetCodeExplanation
    @EmployeeId INT,
    @FilingYear INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (1) EC.id AS EmployeeCodeId, EC.employerId AS EmployerId, EC.filingYear AS FilingYear,
           CAST(ISNULL(EC.IsLocked, 0) AS BIT) AS IsLocked, CAST(ISNULL(EC.disableCoding, 0) AS BIT) AS DisableCoding,
           LTRIM(RTRIM(CONCAT(E.firstName, N' ', E.lastName))) AS EmployeeName, ER.name AS EmployerName,
           EC.JAN_COC, EC.FEB_COC, EC.MAR_COC, EC.APR_COC, EC.MAY_COC, EC.JUN_COC, EC.JUL_COC, EC.AUG_COC, EC.SEP_COC, EC.OCT_COC, EC.NOV_COC, EC.DEC_COC,
           EC.JAN_LCMP, EC.FEB_LCMP, EC.MAR_LCMP, EC.APR_LCMP, EC.MAY_LCMP, EC.JUN_LCMP, EC.JUL_LCMP, EC.AUG_LCMP, EC.SEP_LCMP, EC.OCT_LCMP, EC.NOV_LCMP, EC.DEC_LCMP,
           EC.JAN_SHC, EC.FEB_SHC, EC.MAR_SHC, EC.APR_SHC, EC.MAY_SHC, EC.JUN_SHC, EC.JUL_SHC, EC.AUG_SHC, EC.SEP_SHC, EC.OCT_SHC, EC.NOV_SHC, EC.DEC_SHC
    FROM   dbo.EmployeeCode EC
    JOIN   dbo.Employee E ON E.id = EC.employeeId
    LEFT   JOIN dbo.Employer ER ON ER.id = EC.employerId
    WHERE  EC.employeeId = @EmployeeId AND EC.filingYear = @FilingYear
    ORDER  BY EC.id;

    SELECT D.*
    FROM   dbo.EmployeeCodeMonthDetail D
    WHERE  D.EmployeeCodeId = (SELECT TOP (1) id FROM dbo.EmployeeCode WHERE employeeId = @EmployeeId AND filingYear = @FilingYear ORDER BY id)
    ORDER  BY D.MonthNum;
END
GO

------------------------------------------------------------
-- sp_Generate1095Codes (current, with the 058 block)
------------------------------------------------------------
/* =============================================================================
   PART 2 -- sp_Generate1095Codes: build the premium lookup and the snapshot
             plan fallback from the whole group.

   Patched from the live 265-line definition by string replacement. Four hunks:
   CREATE -> CREATE OR ALTER, the @Group table variable, the #PP filter, and the
   plan fallback filter. Nothing else moves.
   ============================================================================= */
CREATE OR ALTER PROCEDURE [dbo].[sp_Generate1095Codes]
    @EmployerId INT, @FilingYear INT, @CurrentUserId NVARCHAR(128)='SYSTEM'
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;   -- any error auto-aborts the transaction so it can never be left open
    DECLARE @TranStarted BIT=0; IF @@TRANCOUNT=0 BEGIN BEGIN TRANSACTION; SET @TranStarted=1; END
    BEGIN TRY
    DECLARE @AffordPct DECIMAL(10,5), @MonthlyFPL DECIMAL(10,5), @FT INT;
    DECLARE @PenA DECIMAL(18,2), @PenB DECIMAL(18,2);

    /* 019-B: the aggregated ALE group this employer belongs to.
       Staging_Plans is keyed on the Primary EIN only, so plans and premiums are
       created once, under the parent. An affiliate employer therefore owns none
       of either, and without widening these lookups every one of its employees
       resolves to a NULL plan and reports 1H -- no offer of coverage -- with a
       blank Line 15. Employer.companyId is the parent pointer the importer
       already sets; the parent's own companyId is NULL.
       Includes @EmployerId itself, so a standalone employer behaves exactly as
       it did before. */
    DECLARE @Group TABLE (id INT PRIMARY KEY);
    INSERT @Group (id)
    SELECT g.id
    FROM   Employer g
    WHERE  g.id = @EmployerId
       OR  g.id = (SELECT p.companyId FROM Employer p WHERE p.id = @EmployerId)
       OR  g.companyId = (SELECT p.companyId FROM Employer p WHERE p.id = @EmployerId)
       OR  g.companyId = @EmployerId;
    SELECT @MonthlyFPL=fpgPremium, @AffordPct=fpgPercent, @FT=ropHours,
           @PenA=PenaltyA_Annual, @PenB=PenaltyB_Annual FROM FilingYear WHERE filingYear=@FilingYear;

    IF @MonthlyFPL IS NULL OR @AffordPct IS NULL OR @FT IS NULL OR @PenA IS NULL OR @PenB IS NULL
    BEGIN
        RAISERROR('FilingYear row for %d is missing or incomplete (fpgPremium/fpgPercent/ropHours/PenaltyA_Annual/PenaltyB_Annual must all be set). Seed FilingYear for this year before generating codes.', 16, 1, @FilingYear);
        IF @TranStarted=1 ROLLBACK TRANSACTION;
        RETURN;
    END

    CREATE TABLE #M (MonthNum INT PRIMARY KEY, MS DATE, ME DATE);
    INSERT #M SELECT n, DATEFROMPARTS(@FilingYear,n,1), EOMONTH(DATEFROMPARTS(@FilingYear,n,1))
    FROM (VALUES(1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12)) v(n);

    CREATE TABLE #E (EmployeeId INT PRIMARY KEY, CodeId INT, HireDate DATE, TermDate DATE, Status INT,
        Elected BIT, CovStart DATE, CovEnd DATE, PlanName NVARCHAR(200), COBRAElected BIT, Eligible DATE,
        COBRAEffDate DATE, COBRAEndDate DATE, IsUnion BIT,
        HasSpan BIT DEFAULT 0, HasStatus BIT DEFAULT 0, HasEnr BIT DEFAULT 0);
    INSERT #E (EmployeeId,CodeId,HireDate,TermDate,Status,Elected,CovStart,CovEnd,PlanName,COBRAElected,Eligible,
               COBRAEffDate,COBRAEndDate,IsUnion)
    SELECT E.id, EC.id, E.HireDate, E.TerminationDate, E.Status, E.CoverageElected, E.CoverageStartDate, E.CoverageEndDate,
           E.LowestCostPlanOffered, E.COBRABenefitsElected, E.DateEligibleForCoverage,
           E.COBRAEffectiveDate, E.COBRAEndDate, E.IsUnionEmployee
    FROM Employee E JOIN EmployeeCode EC ON EC.employeeId=E.id AND EC.filingYear=@FilingYear
    WHERE E.EmployerId=@EmployerId AND E.IsDeleted=0 AND EC.IsLocked=0 AND EC.disableCoding=0;
    UPDATE e SET HasSpan=1 FROM #E e WHERE EXISTS(SELECT 1 FROM EmployeeHireSpan h WHERE h.employeeCodeId=e.CodeId);
    UPDATE e SET HasStatus=1 FROM #E e WHERE EXISTS(SELECT 1 FROM EmployeeStatus s WHERE s.employeeCodeId=e.CodeId);
    UPDATE e SET HasEnr=1 FROM #E e WHERE EXISTS(SELECT 1 FROM EmployeeEnrollment n WHERE n.employeeCodeId=e.CodeId);

    CREATE TABLE #PP (PlanId INT, MonthNum INT, Prem DECIMAL(18,2), PRIMARY KEY(PlanId,MonthNum));
    INSERT #PP SELECT pr.EmployerPlanId, m.MonthNum, MIN(pr.amount)
    FROM Premium pr JOIN #M m ON pr.bandingStartDate<=m.ME AND (pr.bandingEndDate IS NULL OR pr.bandingEndDate>=m.MS)
    WHERE pr.EmployerId IN (SELECT id FROM @Group) GROUP BY pr.EmployerPlanId, m.MonthNum;

    CREATE TABLE #Pay (EmployeeId INT PRIMARY KEY, Hourly DECIMAL(18,4), AnnualW2 DECIMAL(18,4));
    INSERT #Pay SELECT e.EmployeeId, ISNULL(MAX(pr.payPeriodTotalHours),0),
        ISNULL(MAX(pr.payPeriodSalaryAmount),0)+ISNULL(MAX(pr.payPeriodAdditional),0)
    FROM #E e JOIN EmployeePayroll pr ON pr.employeeCodeId=e.CodeId GROUP BY e.EmployeeId;

    CREATE TABLE #C (EmployeeId INT, MonthNum INT, Employed BIT DEFAULT 0, IsFT BIT DEFAULT 0,
        PlanId INT, mv BIT, sp INT, cond INT, dep INT, funding NVARCHAR(10), code1A BIT, Prem DECIMAL(18,2),
        EnrolledMo BIT DEFAULT 0, UnionMo BIT DEFAULT 0, CobraMo BIT DEFAULT 0,
        -- >>> ICHRA working columns
        IsIchra BIT DEFAULT 0, Basis CHAR(1), Lcsp DECIMAL(18,2), Allow DECIMAL(18,2),
        NetCost DECIMAL(18,2), IchraZip VARCHAR(10),
        Line14 VARCHAR(2) DEFAULT '1H', Line15 DECIMAL(18,2), Line16 VARCHAR(2), PRIMARY KEY(EmployeeId,MonthNum));
    INSERT #C (EmployeeId,MonthNum) SELECT e.EmployeeId, m.MonthNum FROM #E e CROSS JOIN #M m;

    UPDATE c SET Employed=1 FROM #C c JOIN #E e ON e.EmployeeId=c.EmployeeId AND e.HasSpan=1 JOIN #M m ON m.MonthNum=c.MonthNum
      JOIN EmployeeHireSpan h ON h.employeeCodeId=e.CodeId AND h.startDate<=m.ME AND (h.endDate IS NULL OR h.endDate>=m.MS);
    UPDATE c SET Employed=1 FROM #C c JOIN #E e ON e.EmployeeId=c.EmployeeId AND e.HasSpan=0 JOIN #M m ON m.MonthNum=c.MonthNum
      WHERE e.HireDate<=m.ME AND (e.TermDate IS NULL OR e.TermDate>=m.MS);
    UPDATE c SET IsFT=1 FROM #C c JOIN #E e ON e.EmployeeId=c.EmployeeId AND e.HasStatus=1 JOIN #M m ON m.MonthNum=c.MonthNum
      JOIN EmployeeStatus s ON s.employeeCodeId=e.CodeId AND s.status=1 AND s.startDate<=m.ME AND (s.endDate IS NULL OR s.endDate>=m.MS);
    UPDATE c SET IsFT=1 FROM #C c JOIN #E e ON e.EmployeeId=c.EmployeeId AND e.HasStatus=0 WHERE e.Status=1;

    ;WITH R AS (
      SELECT e.EmployeeId, m.MonthNum, EE.planId,
        ROW_NUMBER() OVER (PARTITION BY e.EmployeeId, m.MonthNum ORDER BY EE.coverageStartDate DESC, EE.coverageOfferDate DESC) rn
      FROM #E e JOIN EmployeeEnrollment EE ON EE.employeeCodeId=e.CodeId AND EE.planId IS NOT NULL AND e.HasEnr=1
      JOIN #M m ON ISNULL(EE.coverageOfferDate,ISNULL(EE.coverageStartDate,'1900-01-01'))<=m.ME AND (EE.coverageEndDate IS NULL OR EE.coverageEndDate>=m.MS))
    UPDATE c SET PlanId=R.planId FROM #C c JOIN R ON R.EmployeeId=c.EmployeeId AND R.MonthNum=c.MonthNum AND R.rn=1;
    UPDATE c SET PlanId=P.id FROM #C c JOIN #E e ON e.EmployeeId=c.EmployeeId AND e.HasEnr=0
      JOIN EmployerPlan P ON REPLACE(LTRIM(RTRIM(P.name)),CHAR(160),'')=REPLACE(LTRIM(RTRIM(e.PlanName)),CHAR(160),'') AND P.employerId IN (SELECT id FROM @Group)
      WHERE c.PlanId IS NULL;
    UPDATE c SET mv=P.minimumValue, sp=P.offeredSpouse, cond=P.conditionallyOffSpouse, dep=P.offeredDependents,
                 funding=P.fundingType, code1A=P.code1A FROM #C c JOIN EmployerPlan P ON P.id=c.PlanId;
    UPDATE c SET Prem=pp.Prem FROM #C c JOIN #PP pp ON pp.PlanId=c.PlanId AND pp.MonthNum=c.MonthNum;

    /* ---- F-06: banded plans price per employee, not per plan ----------------
       #PP above takes MIN(amount) per plan and month, which is right for an
       unbanded plan (one row per date range) and wrong for a banded one: every
       employee is handed the cheapest tier. On an inverse salary scale that
       understates line 15 for exactly the people it matters most for, and an
       understated contribution passes the affordability test, so 2F/2G/2H get
       claimed where no safe harbour applies.

       Overrides Prem for plans banded on a numeric range. Employee.birthday and
       hire date are read here rather than added to #E, so no existing statement
       changes. Bands whose boundaries are not numeric -- 'Monthly' plans carry
       month names in these columns -- never match and keep the #PP value.
       Ties break to the higher amount: never understate. */
    UPDATE c
    SET    Prem = b.Amount
    FROM   #C c
    JOIN   EmployerPlan P  ON P.id = c.PlanId
    JOIN   #M m            ON m.MonthNum = c.MonthNum
    JOIN   Employee emp    ON emp.id = c.EmployeeId
    LEFT   JOIN #Pay pay   ON pay.EmployeeId = c.EmployeeId
    CROSS APPLY (
        SELECT TOP 1 Amount = pr.amount
        FROM   Premium pr
        WHERE  pr.EmployerPlanId = c.PlanId
          AND  pr.bandingStartDate <= m.ME
          AND  (pr.bandingEndDate IS NULL OR pr.bandingEndDate >= m.MS)
          AND  TRY_CAST(pr.bandingValueStart AS DECIMAL(18,2)) IS NOT NULL
          AND  TRY_CAST(pr.bandingValueEnd   AS DECIMAL(18,2)) IS NOT NULL
          AND  CASE UPPER(LTRIM(RTRIM(ISNULL(P.bandingType, ''))))
                    WHEN 'SALARY'           THEN ISNULL(pay.AnnualW2, 0)
                    WHEN 'HOURLY'           THEN ISNULL(pay.Hourly, 0)
                    WHEN 'AGE'              THEN CASE WHEN emp.birthday IS NULL THEN NULL
                                                      ELSE DATEDIFF(YEAR, emp.birthday, m.MS)
                                                         - CASE WHEN DATEADD(YEAR, DATEDIFF(YEAR, emp.birthday, m.MS), emp.birthday) > m.MS
                                                                THEN 1 ELSE 0 END END
                    WHEN 'YEARS OF SERVICE' THEN CASE WHEN emp.HireDate IS NULL THEN NULL
                                                      ELSE DATEDIFF(YEAR, emp.HireDate, m.MS)
                                                         - CASE WHEN DATEADD(YEAR, DATEDIFF(YEAR, emp.HireDate, m.MS), emp.HireDate) > m.MS
                                                                THEN 1 ELSE 0 END END
               END BETWEEN TRY_CAST(pr.bandingValueStart AS DECIMAL(18,2))
                       AND TRY_CAST(pr.bandingValueEnd   AS DECIMAL(18,2))
        ORDER BY pr.amount DESC
    ) b
    WHERE UPPER(LTRIM(RTRIM(ISNULL(P.bandingType, '')))) IN ('SALARY', 'HOURLY', 'AGE', 'YEARS OF SERVICE');

    -- >>> ICHRA (1) populate ICHRA inputs on the grid --------------------------------
    UPDATE c SET IsIchra=1, Basis=P.ichraLocationBasis, Allow=P.ichraSelfOnlyAllow
    FROM #C c JOIN EmployerPlan P ON P.id=c.PlanId WHERE P.isIchra=1;
    UPDATE c SET Allow=ISNULL(x.ichraAllowance,c.Allow), Lcsp=x.lcspSelfOnly,
                 IchraZip=COALESCE(x.applicableZip, CASE WHEN c.Basis='W' THEN emp.workSiteZip ELSE emp.zip END)
    FROM #C c JOIN #E e ON e.EmployeeId=c.EmployeeId JOIN Employee emp ON emp.id=c.EmployeeId
      OUTER APPLY (SELECT TOP 1 ei.ichraAllowance, ei.lcspSelfOnly, ei.applicableZip
                   FROM EmployeeIchra ei WHERE ei.employeeCodeId=e.CodeId AND ei.monthNum IN (0,c.MonthNum)
                   ORDER BY ei.monthNum DESC) x
    WHERE c.IsIchra=1;
    UPDATE #C SET NetCost = CASE WHEN Lcsp IS NULL THEN NULL
                                 WHEN Lcsp-ISNULL(Allow,0) < 0 THEN 0 ELSE Lcsp-ISNULL(Allow,0) END
    WHERE IsIchra=1;
    -- <<< ICHRA (1)

    UPDATE c SET EnrolledMo=1 FROM #C c JOIN #E e ON e.EmployeeId=c.EmployeeId AND e.HasEnr=1 JOIN #M m ON m.MonthNum=c.MonthNum
      JOIN EmployeeEnrollment EE ON EE.employeeCodeId=e.CodeId AND EE.isEnrolled=1 AND EE.coverageStartDate<=m.ME AND ISNULL(EE.coverageEndDate,'2099-12-31')>=m.MS;
    UPDATE c SET EnrolledMo=1 FROM #C c JOIN #E e ON e.EmployeeId=c.EmployeeId AND e.HasEnr=0 JOIN #M m ON m.MonthNum=c.MonthNum
      WHERE e.Elected=1 AND e.CovStart<=m.ME AND ISNULL(e.CovEnd,'2099-12-31')>=m.MS;
    UPDATE c SET UnionMo=1 FROM #C c JOIN #E e ON e.EmployeeId=c.EmployeeId JOIN #M m ON m.MonthNum=c.MonthNum
      JOIN EmployeeEnrollment EE ON EE.employeeCodeId=e.CodeId AND EE.unionMember=1 AND ISNULL(EE.contributionStartDate,ISNULL(EE.coverageOfferDate,'1900-01-01'))<=m.ME AND (EE.contributionEndDate IS NULL OR EE.contributionEndDate>=m.MS);
    UPDATE c SET UnionMo=1 FROM #C c JOIN #E e ON e.EmployeeId=c.EmployeeId JOIN #M m ON m.MonthNum=c.MonthNum
      WHERE e.HasEnr=0 AND e.IsUnion=1;
    UPDATE c SET CobraMo=1 FROM #C c JOIN #E e ON e.EmployeeId=c.EmployeeId JOIN #M m ON m.MonthNum=c.MonthNum
      JOIN EmployeeEnrollment EE ON EE.employeeCodeId=e.CodeId AND EE.COBRAEnrolled=1 AND ISNULL(EE.COBRAStartDate,'1900-01-01')<=m.ME AND (EE.COBRAEndDate IS NULL OR EE.COBRAEndDate>=m.MS);
    UPDATE c SET CobraMo=1 FROM #C c JOIN #E e ON e.EmployeeId=c.EmployeeId JOIN #M m ON m.MonthNum=c.MonthNum
      WHERE e.HasEnr=0 AND e.COBRAElected=1
        AND ISNULL(e.COBRAEffDate,'1900-01-01')<=m.ME AND ISNULL(e.COBRAEndDate,'2099-12-31')>=m.MS;

    -- LINE 14 / 15 (group-plan path - unchanged)
    UPDATE #C SET Line14 = CASE
        WHEN PlanId IS NULL THEN '1H' WHEN mv=0 THEN '1F'
        WHEN cond=1 AND dep=1 THEN '1K' WHEN cond=1 AND dep=0 THEN '1J'
        WHEN sp=1 AND dep=1 AND code1A=1 AND Prem IS NOT NULL AND Prem<=@MonthlyFPL THEN '1A'
        WHEN sp=1 AND dep=1 THEN '1E' WHEN sp=1 AND dep=0 THEN '1D'
        WHEN sp=0 AND dep=1 THEN '1C' ELSE '1B' END,
      Line15 = CASE WHEN PlanId IS NULL OR mv=0 THEN NULL
                    WHEN sp=1 AND dep=1 AND code1A=1 AND Prem<=@MonthlyFPL THEN NULL ELSE Prem END;
    UPDATE #C SET Line14='1G', Line15=NULL WHERE IsFT=0 AND EnrolledMo=1 AND funding='2';

    -- LINE 16 (precedence - group-plan path unchanged)
    UPDATE #C SET Line16='2A', Line14='1H', Line15=NULL WHERE Employed=0;
    UPDATE #C SET Line16='2A' WHERE Line16 IS NULL AND CobraMo=1;
    UPDATE #C SET Line16='2E' WHERE Line16 IS NULL AND UnionMo=1;
    UPDATE #C SET Line16='2C' WHERE Line16 IS NULL AND EnrolledMo=1;
    UPDATE c SET Line16='2D' FROM #C c JOIN #E e ON e.EmployeeId=c.EmployeeId JOIN #M m ON m.MonthNum=c.MonthNum
      WHERE c.Line16 IS NULL AND ((MONTH(e.HireDate)=c.MonthNum AND YEAR(e.HireDate)=@FilingYear AND DAY(e.HireDate)>1)
         OR (e.Eligible IS NOT NULL AND m.ME<e.Eligible AND m.ME>=e.HireDate));
    UPDATE #C SET Line16='2B' WHERE Line16 IS NULL AND IsFT=0;
    ;WITH HM AS (SELECT EmployeeId, SUM(CAST(Employed AS INT)) HiredMonths FROM #C GROUP BY EmployeeId)
    UPDATE c SET Line16 = CASE
        WHEN c.Prem<=@MonthlyFPL THEN '2G'
        WHEN p.Hourly>0 AND c.Prem<=(p.Hourly*@FT)*@AffordPct THEN '2H'
        WHEN p.AnnualW2>0 AND hm.HiredMonths>0 AND c.Prem<=(p.AnnualW2/hm.HiredMonths)*@AffordPct THEN '2F' ELSE NULL END
    FROM #C c LEFT JOIN #Pay p ON p.EmployeeId=c.EmployeeId LEFT JOIN HM hm ON hm.EmployeeId=c.EmployeeId
    WHERE c.Line16 IS NULL AND c.IsFT=1 AND c.PlanId IS NOT NULL AND c.mv=1 AND c.EnrolledMo=0;
    UPDATE #C SET Line16=NULL WHERE Line14 IN ('1A','1G');

    -- >>> ICHRA (2) override Line 14/15/16 for ICHRA offer months (1L-1U / 1R / 1S) ----
    ;WITH HM AS (SELECT EmployeeId, SUM(CAST(Employed AS INT)) HiredMonths FROM #C GROUP BY EmployeeId)
    UPDATE c SET
      Line16 = CASE
          WHEN c.IsFT=0 THEN NULL
          WHEN c.EnrolledMo=1 THEN '2C'
          WHEN c.NetCost<=@MonthlyFPL THEN '2G'
          WHEN p.Hourly>0 AND c.NetCost<=(p.Hourly*@FT)*@AffordPct THEN '2H'
          WHEN p.AnnualW2>0 AND hm.HiredMonths>0 AND c.NetCost<=(p.AnnualW2/hm.HiredMonths)*@AffordPct THEN '2F'
          ELSE NULL END,
      Line14 = CASE
          WHEN c.IsFT=0 THEN '1S'
          WHEN c.NetCost IS NULL THEN '1H'
          WHEN NOT (c.NetCost<=@MonthlyFPL
                    OR (p.Hourly>0 AND c.NetCost<=(p.Hourly*@FT)*@AffordPct)
                    OR (p.AnnualW2>0 AND hm.HiredMonths>0 AND c.NetCost<=(p.AnnualW2/hm.HiredMonths)*@AffordPct))
               THEN '1R'
          WHEN c.Basis='W' AND (c.sp=1 OR c.cond=1) AND c.dep=1 THEN '1Q'
          WHEN c.Basis='W' AND (c.sp=1 OR c.cond=1) AND c.dep=0 THEN '1U'
          WHEN c.Basis='W' AND c.dep=1 THEN '1P'
          WHEN c.Basis='W' THEN '1O'
          WHEN (c.sp=1 OR c.cond=1) AND c.dep=1 THEN '1N'
          WHEN (c.sp=1 OR c.cond=1) AND c.dep=0 THEN '1T'
          WHEN c.dep=1 THEN '1M'
          ELSE '1L' END,
      Line15 = CASE WHEN c.IsFT=0 THEN NULL ELSE c.NetCost END
    FROM #C c LEFT JOIN #Pay p ON p.EmployeeId=c.EmployeeId LEFT JOIN HM hm ON hm.EmployeeId=c.EmployeeId
    WHERE c.IsIchra=1 AND c.Employed=1 AND ISNULL(c.Line16,'') NOT IN ('2D','2E');
    -- <<< ICHRA (2)

    DECLARE @DefRenew VARCHAR(2) = (SELECT TOP 1 RIGHT('0'+CAST(ISNULL(planRenewal,1) AS VARCHAR(2)),2)
                                    FROM EmployerPlan WHERE employerId=@EmployerId ORDER BY id);
    SET @DefRenew = ISNULL(@DefRenew,'01');
    CREATE TABLE #PSM (EmployeeId INT PRIMARY KEY, StartMonth VARCHAR(2));
    INSERT #PSM SELECT c.EmployeeId,
        ISNULL(RIGHT('0'+CAST(MAX(P.planRenewal) AS VARCHAR(2)),2), @DefRenew)
    FROM #C c LEFT JOIN EmployerPlan P ON P.id=c.PlanId GROUP BY c.EmployeeId;

    ;WITH MP AS (SELECT EmployeeId,
        MAX(CASE WHEN MonthNum=1 THEN Line14 END) C1,MAX(CASE WHEN MonthNum=2 THEN Line14 END) C2,MAX(CASE WHEN MonthNum=3 THEN Line14 END) C3,MAX(CASE WHEN MonthNum=4 THEN Line14 END) C4,MAX(CASE WHEN MonthNum=5 THEN Line14 END) C5,MAX(CASE WHEN MonthNum=6 THEN Line14 END) C6,MAX(CASE WHEN MonthNum=7 THEN Line14 END) C7,MAX(CASE WHEN MonthNum=8 THEN Line14 END) C8,MAX(CASE WHEN MonthNum=9 THEN Line14 END) C9,MAX(CASE WHEN MonthNum=10 THEN Line14 END) C10,MAX(CASE WHEN MonthNum=11 THEN Line14 END) C11,MAX(CASE WHEN MonthNum=12 THEN Line14 END) C12,
        MAX(CASE WHEN MonthNum=1 THEN Line15 END) L1,MAX(CASE WHEN MonthNum=2 THEN Line15 END) L2,MAX(CASE WHEN MonthNum=3 THEN Line15 END) L3,MAX(CASE WHEN MonthNum=4 THEN Line15 END) L4,MAX(CASE WHEN MonthNum=5 THEN Line15 END) L5,MAX(CASE WHEN MonthNum=6 THEN Line15 END) L6,MAX(CASE WHEN MonthNum=7 THEN Line15 END) L7,MAX(CASE WHEN MonthNum=8 THEN Line15 END) L8,MAX(CASE WHEN MonthNum=9 THEN Line15 END) L9,MAX(CASE WHEN MonthNum=10 THEN Line15 END) L10,MAX(CASE WHEN MonthNum=11 THEN Line15 END) L11,MAX(CASE WHEN MonthNum=12 THEN Line15 END) L12,
        MAX(CASE WHEN MonthNum=1 THEN Line16 END) S1,MAX(CASE WHEN MonthNum=2 THEN Line16 END) S2,MAX(CASE WHEN MonthNum=3 THEN Line16 END) S3,MAX(CASE WHEN MonthNum=4 THEN Line16 END) S4,MAX(CASE WHEN MonthNum=5 THEN Line16 END) S5,MAX(CASE WHEN MonthNum=6 THEN Line16 END) S6,MAX(CASE WHEN MonthNum=7 THEN Line16 END) S7,MAX(CASE WHEN MonthNum=8 THEN Line16 END) S8,MAX(CASE WHEN MonthNum=9 THEN Line16 END) S9,MAX(CASE WHEN MonthNum=10 THEN Line16 END) S10,MAX(CASE WHEN MonthNum=11 THEN Line16 END) S11,MAX(CASE WHEN MonthNum=12 THEN Line16 END) S12,
        -- >>> ICHRA (3) pivot Line 17 ZIP
        MAX(CASE WHEN MonthNum=1 THEN IchraZip END) Z1,MAX(CASE WHEN MonthNum=2 THEN IchraZip END) Z2,MAX(CASE WHEN MonthNum=3 THEN IchraZip END) Z3,MAX(CASE WHEN MonthNum=4 THEN IchraZip END) Z4,MAX(CASE WHEN MonthNum=5 THEN IchraZip END) Z5,MAX(CASE WHEN MonthNum=6 THEN IchraZip END) Z6,MAX(CASE WHEN MonthNum=7 THEN IchraZip END) Z7,MAX(CASE WHEN MonthNum=8 THEN IchraZip END) Z8,MAX(CASE WHEN MonthNum=9 THEN IchraZip END) Z9,MAX(CASE WHEN MonthNum=10 THEN IchraZip END) Z10,MAX(CASE WHEN MonthNum=11 THEN IchraZip END) Z11,MAX(CASE WHEN MonthNum=12 THEN IchraZip END) Z12,
        -- <<< ICHRA (3)
        MAX(CASE WHEN Line14='1H' AND Line16 IS NULL THEN 1 ELSE 0 END) Risk,
        SUM(CASE WHEN Line14='1H' AND Line16 IS NULL THEN 1 ELSE 0 END) PMonths
      FROM #C GROUP BY EmployeeId)
    UPDATE EC SET JAN_COC=P.C1,FEB_COC=P.C2,MAR_COC=P.C3,APR_COC=P.C4,MAY_COC=P.C5,JUN_COC=P.C6,JUL_COC=P.C7,AUG_COC=P.C8,SEP_COC=P.C9,OCT_COC=P.C10,NOV_COC=P.C11,DEC_COC=P.C12,
        JAN_LCMP=P.L1,FEB_LCMP=P.L2,MAR_LCMP=P.L3,APR_LCMP=P.L4,MAY_LCMP=P.L5,JUN_LCMP=P.L6,JUL_LCMP=P.L7,AUG_LCMP=P.L8,SEP_LCMP=P.L9,OCT_LCMP=P.L10,NOV_LCMP=P.L11,DEC_LCMP=P.L12,
        JAN_SHC=P.S1,FEB_SHC=P.S2,MAR_SHC=P.S3,APR_SHC=P.S4,MAY_SHC=P.S5,JUN_SHC=P.S6,JUL_SHC=P.S7,AUG_SHC=P.S8,SEP_SHC=P.S9,OCT_SHC=P.S10,NOV_SHC=P.S11,DEC_SHC=P.S12,
        -- >>> ICHRA (4) write Line 17 ZIP columns
        JAN_ZIP=P.Z1,FEB_ZIP=P.Z2,MAR_ZIP=P.Z3,APR_ZIP=P.Z4,MAY_ZIP=P.Z5,JUN_ZIP=P.Z6,JUL_ZIP=P.Z7,AUG_ZIP=P.Z8,SEP_ZIP=P.Z9,OCT_ZIP=P.Z10,NOV_ZIP=P.Z11,DEC_ZIP=P.Z12,
        ALLM_ZIP=CASE WHEN P.Z1=P.Z2 AND P.Z1=P.Z3 AND P.Z1=P.Z4 AND P.Z1=P.Z5 AND P.Z1=P.Z6 AND P.Z1=P.Z7 AND P.Z1=P.Z8 AND P.Z1=P.Z9 AND P.Z1=P.Z10 AND P.Z1=P.Z11 AND P.Z1=P.Z12 THEN P.Z1 ELSE NULL END,
        -- <<< ICHRA (4)
        ALLM_COC=CASE WHEN P.C1=P.C2 AND P.C1=P.C3 AND P.C1=P.C4 AND P.C1=P.C5 AND P.C1=P.C6 AND P.C1=P.C7 AND P.C1=P.C8 AND P.C1=P.C9 AND P.C1=P.C10 AND P.C1=P.C11 AND P.C1=P.C12 THEN P.C1 ELSE NULL END,
        ALLM_LCMP=CASE WHEN P.L1=P.L2 AND P.L1=P.L3 AND P.L1=P.L4 AND P.L1=P.L5 AND P.L1=P.L6 AND P.L1=P.L7 AND P.L1=P.L8 AND P.L1=P.L9 AND P.L1=P.L10 AND P.L1=P.L11 AND P.L1=P.L12 THEN P.L1 ELSE NULL END,
        ALLM_SHC=CASE WHEN P.S1=P.S2 AND P.S1=P.S3 AND P.S1=P.S4 AND P.S1=P.S5 AND P.S1=P.S6 AND P.S1=P.S7 AND P.S1=P.S8 AND P.S1=P.S9 AND P.S1=P.S10 AND P.S1=P.S11 AND P.S1=P.S12 THEN P.S1 ELSE NULL END,
        PlanStartMonth=PSM.StartMonth,
        IsAtPenaltyRisk=P.Risk, PenaltyMonths=P.PMonths,
        EstimatedPenalty = P.PMonths * (@PenA / 12.0)
    FROM EmployeeCode EC JOIN MP P ON EC.employeeId=P.EmployeeId
    LEFT JOIN #PSM PSM ON PSM.EmployeeId=EC.employeeId
    WHERE EC.employerId=@EmployerId AND EC.filingYear=@FilingYear AND EC.IsLocked=0 AND EC.disableCoding=0;

    INSERT INTO EmployeeCodeAuditTrail
        (EmployeeId, EmployerId, FilingYear, MonthCode, AppliesToAllMonths,
         LineNumber, FieldName, OldValue, NewValue,
         ChangeSource, ChangeReason, SafeHarborApplied,
         ChangedByUserId, ChangedByUserName, IsSystemGenerated, ChangedOn, SourceIP, Notes)
    SELECT EC.employeeId, @EmployerId, @FilingYear, 'ALL', 0,
           0, 'All Codes', 'PREVIOUS_CALC', 'RE-CALCULATED',
           'SYSTEM', 'Automatic Code Generation Triggered', NULL,
           NULL, @CurrentUserId, 1, GETDATE(), 'INTERNAL', 'Codes recalculated by sp_Generate1095Codes (v8+ICHRA)'
    FROM EmployeeCode EC
    WHERE EC.employerId=@EmployerId AND EC.filingYear=@FilingYear AND EC.IsLocked=0;

    -- 058: keep each month's working values, so the codes can be explained ("Why this code?").
    -- Locked employees and those with coding switched off are not recalculated and keep theirs.
    DELETE D FROM dbo.EmployeeCodeMonthDetail D JOIN #E e ON e.CodeId = D.EmployeeCodeId;
    ;WITH HM AS (SELECT EmployeeId, SUM(CAST(Employed AS INT)) HiredMonths FROM #C GROUP BY EmployeeId)
    INSERT dbo.EmployeeCodeMonthDetail
        (EmployeeCodeId, MonthNum, Employed, IsFullTime, PlanId, PlanName, MinimumValue, OfferedSpouse, ConditionalSpouse,
         OfferedDependents, FundingType, Code1A, Premium, Enrolled, UnionMonth, CobraMonth, IsIchra, IchraLcsp, IchraAllowance,
         IchraNetCost, HourlyRate, AnnualW2, HiredMonths, MonthlyFpl, AffordabilityPct, FullTimeHours,
         HireDate, TerminationDate, EligibleDate, Line14, Line15, Line16, CalculatedBy)
    SELECT e.CodeId, c.MonthNum, c.Employed, c.IsFT, c.PlanId, P.name, c.mv, c.sp, c.cond,
           c.dep, c.funding, c.code1A, c.Prem, c.EnrolledMo, c.UnionMo, c.CobraMo, c.IsIchra, c.Lcsp, c.Allow,
           c.NetCost, pay.Hourly, pay.AnnualW2, hm.HiredMonths, @MonthlyFPL, @AffordPct, @FT,
           e.HireDate, e.TermDate, e.Eligible, c.Line14, c.Line15, c.Line16, @CurrentUserId
    FROM   #C c
    JOIN   #E e ON e.EmployeeId = c.EmployeeId
    LEFT   JOIN EmployerPlan P ON P.id = c.PlanId
    LEFT   JOIN #Pay pay ON pay.EmployeeId = c.EmployeeId
    LEFT   JOIN HM hm ON hm.EmployeeId = c.EmployeeId;

    DROP TABLE #M; DROP TABLE #E; DROP TABLE #PP; DROP TABLE #Pay; DROP TABLE #C; DROP TABLE #PSM;
    IF @TranStarted=1 COMMIT TRANSACTION;
    END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK TRANSACTION; THROW; END CATCH
END
GO
