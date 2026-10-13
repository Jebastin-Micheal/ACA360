/* =============================================================================
   071 - Best safe harbor for each employee (idea #12)

   Advice only: nothing here changes a code. For an employer and tax year it
   tests each month where line 16 needs an affordability safe harbor (the
   employee was offered coverage, did not enrol, and no other line 16 code
   applies) against all three:

     2F  W-2:          premium <= affordability % x W-2 wages / months employed
     2H  Rate of pay:  premium <= affordability % x hourly rate x 130 hours
                       (salaried: x annual salary / 12)
     2G  Federal poverty line: premium <= the year's FPL amount

   from the figures sp_Generate1095Codes kept for that month (script 058:
   EmployeeCodeMonthDetail), so the advice matches the codes as generated.
   The import carries one pay figure ("W-2 income or annual salary"), so the
   W-2 and salaried rate-of-pay tests use the same number.

   sp_SafeHarborAdvice returns
     1. per safe harbor: months it covers, employees fully covered, and
        months the current line 16 codes leave without a safe harbor;
     2. per employee: months tested, months each one covers, the codes used
        now, the best safe harbor and the months it would add.

   Reads only. Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE dbo.sp_SafeHarborAdvice
    @EmployerId BIGINT,
    @Year       INT
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH Months AS (
        SELECT C.employeeId AS EmployeeId, D.MonthNum, D.Line16, D.Premium,
               CAST(CASE WHEN D.Premium <= D.MonthlyFpl THEN 1 ELSE 0 END AS INT) AS FplOk,
               CAST(CASE WHEN D.AnnualW2 > 0 AND D.HiredMonths > 0
                          AND D.Premium <= D.AffordabilityPct * D.AnnualW2 / D.HiredMonths THEN 1 ELSE 0 END AS INT) AS W2Ok,
               CAST(CASE WHEN D.HourlyRate > 0 AND D.Premium <= D.AffordabilityPct * D.HourlyRate * ISNULL(NULLIF(D.FullTimeHours, 0), 130) THEN 1
                         WHEN ISNULL(D.HourlyRate, 0) = 0 AND D.AnnualW2 > 0 AND D.Premium <= D.AffordabilityPct * D.AnnualW2 / 12 THEN 1
                         ELSE 0 END AS INT) AS RopOk
        FROM   dbo.EmployeeCode C
        JOIN   dbo.EmployeeCodeMonthDetail D ON D.EmployeeCodeId = C.id
        JOIN   dbo.Employee E ON E.id = C.employeeId AND ISNULL(E.IsDeleted, 0) = 0
        WHERE  C.employerId = @EmployerId AND C.filingYear = @Year
          AND  D.Employed = 1
          AND  D.Premium IS NOT NULL
          AND  ISNULL(D.Line14, '') NOT IN ('', '1A', '1H')           -- an offer that is not already a qualifying offer
          AND  ISNULL(D.Line16, '') NOT IN ('2A', '2B', '2C', '2D', '2E')   -- employed, full time, not enrolled, no waiting period
    ), PerEmployee AS (
        SELECT EmployeeId,
               COUNT(*) AS MonthsTested,
               SUM(FplOk) AS FplMonths, SUM(W2Ok) AS W2Months, SUM(RopOk) AS RopMonths,
               SUM(CASE WHEN (Line16 = '2G' AND FplOk = 1) OR (Line16 = '2F' AND W2Ok = 1) OR (Line16 = '2H' AND RopOk = 1)
                        THEN 1 ELSE 0 END) AS CurrentMonths,
               STRING_AGG(ISNULL(Line16, '-'), ',') WITHIN GROUP (ORDER BY MonthNum) AS CurrentCodes,
               MIN(Premium) AS MinPremium, MAX(Premium) AS MaxPremium
        FROM   Months
        GROUP  BY EmployeeId
    ), Ranked AS (
        SELECT P.*,
               B.Code AS BestCode, B.Months AS BestMonths
        FROM   PerEmployee P
        CROSS  APPLY (SELECT TOP 1 X.Code, X.Months
                      FROM (VALUES ('2H', P.RopMonths, 1), ('2F', P.W2Months, 2), ('2G', P.FplMonths, 3)) X (Code, Months, Pref)
                      -- a tie keeps the safe harbor already in use
                      ORDER BY X.Months DESC, CASE WHEN CHARINDEX(X.Code, P.CurrentCodes) > 0 THEN 0 ELSE 1 END, X.Pref) B
    )
    SELECT * INTO #Advice FROM Ranked;

    -- 1. Per safe harbor
    SELECT S.Code,
           S.Name,
           ISNULL(SUM(CASE S.Code WHEN '2F' THEN A.W2Months WHEN '2G' THEN A.FplMonths ELSE A.RopMonths END), 0) AS MonthsCovered,
           ISNULL(SUM(CASE WHEN (CASE S.Code WHEN '2F' THEN A.W2Months WHEN '2G' THEN A.FplMonths ELSE A.RopMonths END) = A.MonthsTested
                           THEN 1 ELSE 0 END), 0) AS EmployeesFullyCovered,
           ISNULL(SUM(A.MonthsTested), 0) AS MonthsTested,
           (SELECT COUNT(*) FROM #Advice) AS EmployeesTested,
           ISNULL(SUM(A.CurrentMonths), 0) AS CurrentMonthsCovered
    FROM   (VALUES ('2H', 'Rate of pay'), ('2F', 'W-2'), ('2G', 'Federal poverty line')) S (Code, Name)
    LEFT   JOIN #Advice A ON 1 = 1
    GROUP  BY S.Code, S.Name
    ORDER  BY MonthsCovered DESC, S.Code DESC;

    -- 2. Per employee: those the best safe harbor would help first
    SELECT TOP 1000
           A.EmployeeId, LTRIM(RTRIM(CONCAT(E.firstName, ' ', E.lastName))) AS EmployeeName,
           A.MonthsTested, A.FplMonths, A.W2Months, A.RopMonths, A.CurrentMonths, A.CurrentCodes,
           A.BestCode, A.BestMonths, A.BestMonths - A.CurrentMonths AS MonthsGained,
           A.MinPremium, A.MaxPremium
    FROM   #Advice A
    JOIN   dbo.Employee E ON E.id = A.EmployeeId
    ORDER  BY A.BestMonths - A.CurrentMonths DESC, A.MonthsTested - A.BestMonths DESC, EmployeeName;
END
GO

PRINT '071: safe harbor advice (sp_SafeHarborAdvice).';
GO
