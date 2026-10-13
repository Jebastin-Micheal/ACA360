/* =============================================================================
   064 - Year over year

   The Penalty Risk page compares an employer's filing year with the year before
   (the employer row of the previous year with the same EIN), and the dashboards
   list the employers whose estimated penalty grew the most.

   Figures come from the 1095-C codes (dbo.EmployeeCode), so a year counts once its
   codes are generated: employees, full-time employees, months offered coverage,
   months enrolled (2C), months not offered while full-time and employed (1H with no
   Line 16: the 4980H(a) risk), offered months with no affordability safe harbor
   (Line 16 blank: the 4980H(b) risk), employees at risk, and the estimated penalty.

   - dbo.fn_EmployerYearFigures(@EmployerId, @Year): one row of figures.
   - dbo.sp_PenaltyRisk_YearOverYear(@EmployerId, @Year): this year and last year.
   - dbo.sp_PenaltyExposureGrowth(@Year, @EmployerIds, @Top): the employers among
     @EmployerIds (JSON array) whose estimated penalty grew most since last year.
   Read-only. Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER FUNCTION dbo.fn_EmployerYearFigures (@EmployerId INT, @Year INT)
RETURNS TABLE
AS RETURN
(
    WITH C AS
    (
        SELECT EC.id, EC.employeeId, ISNULL(EC.IsAtPenaltyRisk, 0) AS AtRisk, ISNULL(EC.EstimatedPenalty, 0) AS Penalty,
               EC.ALLM_COC, EC.ALLM_SHC,
               EC.JAN_COC, EC.FEB_COC, EC.MAR_COC, EC.APR_COC, EC.MAY_COC, EC.JUN_COC, EC.JUL_COC, EC.AUG_COC, EC.SEP_COC, EC.OCT_COC, EC.NOV_COC, EC.DEC_COC,
               EC.JAN_SHC, EC.FEB_SHC, EC.MAR_SHC, EC.APR_SHC, EC.MAY_SHC, EC.JUN_SHC, EC.JUL_SHC, EC.AUG_SHC, EC.SEP_SHC, EC.OCT_SHC, EC.NOV_SHC, EC.DEC_SHC
        FROM   dbo.EmployeeCode EC
        JOIN   dbo.Employee E ON E.id = EC.employeeId AND ISNULL(E.IsDeleted, 0) = 0
        WHERE  EC.employerId = @EmployerId AND EC.filingYear = @Year
    ),
    M AS
    (
        SELECT C.id,
               NULLIF(LTRIM(RTRIM(COALESCE(V.Coc, C.ALLM_COC))), '') AS Coc,
               NULLIF(LTRIM(RTRIM(COALESCE(V.Shc, C.ALLM_SHC))), '') AS Shc
        FROM   C
        CROSS  APPLY (VALUES (C.JAN_COC, C.JAN_SHC), (C.FEB_COC, C.FEB_SHC), (C.MAR_COC, C.MAR_SHC), (C.APR_COC, C.APR_SHC),
                             (C.MAY_COC, C.MAY_SHC), (C.JUN_COC, C.JUN_SHC), (C.JUL_COC, C.JUL_SHC), (C.AUG_COC, C.AUG_SHC),
                             (C.SEP_COC, C.SEP_SHC), (C.OCT_COC, C.OCT_SHC), (C.NOV_COC, C.NOV_SHC), (C.DEC_COC, C.DEC_SHC)) V (Coc, Shc)
    )
    SELECT @EmployerId AS EmployerId, @Year AS FilingYear,
           (SELECT COUNT(DISTINCT employeeId) FROM C) AS Employees,
           (SELECT COUNT(DISTINCT C.employeeId) FROM C JOIN dbo.Employee E ON E.id = C.employeeId WHERE E.Status = 1) AS FullTimeEmployees,
           (SELECT COUNT(*) FROM M WHERE Coc IS NOT NULL AND Coc <> '1H') AS OfferedMonths,
           (SELECT COUNT(*) FROM M WHERE Shc = '2C') AS EnrolledMonths,
           (SELECT COUNT(*) FROM M WHERE Coc = '1H' AND Shc IS NULL) AS NotOfferedMonths,
           (SELECT COUNT(*) FROM M WHERE Coc IS NOT NULL AND Coc NOT IN ('1H', '1A', '1G') AND Shc IS NULL) AS UnaffordableMonths,
           (SELECT COUNT(*) FROM C WHERE AtRisk = 1) AS AtRiskEmployees,
           (SELECT CAST(ISNULL(SUM(Penalty), 0) AS DECIMAL(18,2)) FROM C) AS EstimatedPenalty,
           (SELECT COUNT(*) FROM C) AS CodeRows
);
GO

CREATE OR ALTER PROCEDURE dbo.sp_PenaltyRisk_YearOverYear
    @EmployerId INT,
    @Year       INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Ein NVARCHAR(20) = (SELECT taxid FROM dbo.Employer WHERE id = @EmployerId);
    DECLARE @Prior INT = (SELECT TOP (1) id FROM dbo.Employer
                          WHERE taxid = @Ein AND filingYear = @Year - 1 ORDER BY CASE WHEN companyId IS NULL THEN 0 ELSE 1 END, id);

    SELECT * FROM dbo.fn_EmployerYearFigures(@EmployerId, @Year)
    UNION ALL
    SELECT * FROM dbo.fn_EmployerYearFigures(@Prior, @Year - 1) WHERE @Prior IS NOT NULL;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_PenaltyExposureGrowth
    @Year        INT,
    @EmployerIds NVARCHAR(MAX),
    @Top         INT = 5
AS
BEGIN
    SET NOCOUNT ON;
    IF @Top IS NULL OR @Top NOT BETWEEN 1 AND 50 SET @Top = 5;

    CREATE TABLE #E (Id INT PRIMARY KEY);
    INSERT #E SELECT DISTINCT TRY_CONVERT(INT, value) FROM OPENJSON(@EmployerIds) WHERE TRY_CONVERT(INT, value) IS NOT NULL;

    ;WITH P AS
    (
        SELECT ER.id AS EmployerId, ER.name AS EmployerName, ER.taxid AS Ein, PR.id AS PriorEmployerId
        FROM   #E
        JOIN   dbo.Employer ER ON ER.id = #E.Id AND ER.filingYear = @Year
        CROSS  APPLY (SELECT TOP (1) X.id FROM dbo.Employer X WHERE X.taxid = ER.taxid AND X.filingYear = @Year - 1
                      ORDER BY CASE WHEN X.companyId IS NULL THEN 0 ELSE 1 END, X.id) PR
    ),
    T AS
    (
        SELECT P.EmployerId, P.EmployerName, P.Ein,
               (SELECT ISNULL(SUM(EstimatedPenalty), 0) FROM dbo.EmployeeCode WHERE employerId = P.EmployerId AND filingYear = @Year) AS ThisYear,
               (SELECT ISNULL(SUM(EstimatedPenalty), 0) FROM dbo.EmployeeCode WHERE employerId = P.PriorEmployerId AND filingYear = @Year - 1) AS LastYear,
               (SELECT COUNT(*) FROM dbo.EmployeeCode WHERE employerId = P.PriorEmployerId AND filingYear = @Year - 1) AS LastYearCodes
        FROM   P
    )
    SELECT TOP (@Top) EmployerId, EmployerName, Ein,
           CAST(ThisYear AS DECIMAL(18,2)) AS ThisYear, CAST(LastYear AS DECIMAL(18,2)) AS LastYear,
           CAST(ThisYear - LastYear AS DECIMAL(18,2)) AS Growth
    FROM   T
    WHERE  LastYearCodes > 0 AND ThisYear > LastYear
    ORDER  BY ThisYear - LastYear DESC, EmployerName;
END
GO
