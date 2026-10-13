/* =============================================================================
   059 - Global search (Ctrl+K): employees of the selected employer

   dbo.sp_GlobalSearch_Employees finds employees the way the Employee list scopes
   them: those with codes for the filing year under the selected employer or one of
   its affiliates (Employer.companyId), not deleted. It matches the name (first,
   last or "first last") or, for 4 digits, the last four of the SSN. It returns the
   last four SSN digits only.

   Employers, files and pages are searched in the app with the checks it already
   has (permitted employers, dbo.fn_FileVisibleToUser, the user's menu).
   Read-only. Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE dbo.sp_GlobalSearch_Employees
    @EmployerId INT,
    @FilingYear INT,
    @Query      NVARCHAR(100),
    @Top        INT = 8
AS
BEGIN
    SET NOCOUNT ON;
    SET @Query = NULLIF(LTRIM(RTRIM(@Query)), N'');
    IF @Query IS NULL OR @EmployerId IS NULL OR @EmployerId <= 0 RETURN;
    IF @Top IS NULL OR @Top NOT BETWEEN 1 AND 50 SET @Top = 8;

    DECLARE @Digits VARCHAR(4) = CASE WHEN @Query NOT LIKE '%[^0-9]%' AND LEN(@Query) = 4 THEN @Query END;
    DECLARE @Like NVARCHAR(110) = N'%' + REPLACE(REPLACE(REPLACE(@Query, N'[', N'[[]'), N'%', N'[%]'), N'_', N'[_]') + N'%';

    SELECT TOP (@Top)
           E.id AS EmployeeId,
           LTRIM(RTRIM(CONCAT(E.firstName, N' ', E.lastName))) AS Name,
           RIGHT(REPLACE(E.ssn, '-', ''), 4) AS SsnLast4,
           ER.name AS EmployerName,
           E.TerminationDate
    FROM   dbo.EmployeeCode EC
    JOIN   dbo.Employee E   ON E.id = EC.employeeId AND E.IsDeleted = 0
    JOIN   dbo.Employer ER  ON ER.id = EC.employerId
    WHERE  EC.filingYear = @FilingYear
      AND  (EC.employerId = @EmployerId OR ER.companyId = @EmployerId)
      AND  (  (@Digits IS NOT NULL AND RIGHT(REPLACE(E.ssn, '-', ''), 4) = @Digits)
           OR (@Digits IS NULL AND (E.firstName LIKE @Like OR E.lastName LIKE @Like
                                    OR CONCAT(E.firstName, N' ', E.lastName) LIKE @Like)))
    ORDER  BY CASE WHEN E.lastName LIKE @Query + N'%' OR E.firstName LIKE @Query + N'%' THEN 0 ELSE 1 END,
              E.lastName, E.firstName, E.id;
END
GO
