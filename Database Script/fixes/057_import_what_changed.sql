/* =============================================================================
   057 - Import Analysis: "What changed"

   dbo.sp_ImportAnalysis_Changes compares a validated file with what the system
   already holds for its employer and filing year, the way fn_ImportAnalysis_Impact
   matches rows (employer by EIN and year, employee by employer and SSN, plan by
   name), and says what an import would change:
     - new employees, leavers and termination dates, full-time / part-time changes,
       hire dates, coverage, names / addresses / other details (each field, before
       and after);
     - employees the system has for that employer and year that are not in the file
       (an import leaves them as they are: a sign of a partial file);
     - new dependents, plan changes, premium changes, employer details.
   Only rows that will be imported are compared (IsValid = 1).

   Result sets: 1) Kind, Label, Changes (every kind, in order, zero included);
                2) one page of changes: Kind, Entity, RowNumber, Name, Field,
                   OldValue, NewValue, Note, TotalCount.
   @PageSize NULL returns every change (the Excel export).
   Read-only. Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE dbo.sp_ImportAnalysis_Changes
    @FileLogId  INT,
    @Kind       VARCHAR(20)   = NULL,
    @Search     NVARCHAR(200) = NULL,
    @PageIndex  INT           = 1,
    @PageSize   INT           = 25
AS
BEGIN
    SET NOCOUNT ON;
    IF @PageIndex IS NULL OR @PageIndex < 1 SET @PageIndex = 1;
    IF @PageSize IS NOT NULL AND @PageSize NOT BETWEEN 1 AND 500 SET @PageSize = 25;
    SET @Search = NULLIF(LTRIM(RTRIM(@Search)), N'');
    SET @Kind = NULLIF(@Kind, '');

    DECLARE @PlanYear INT, @Parent INT;
    SELECT @PlanYear = PlanYear, @Parent = ParentEmployerId FROM dbo.fn_ImportAnalysis_Context(@FileLogId);

    CREATE TABLE #K (Kind VARCHAR(20) PRIMARY KEY, Label NVARCHAR(100), Seq INT);
    INSERT #K VALUES
        ('NEW_EMPLOYEE', N'New employees', 1),
        ('TERMINATED',   N'Leavers and termination dates', 2),
        ('STATUS',       N'Full-time / part-time changes', 3),
        ('HIRE',         N'Hire date changes', 4),
        ('COVERAGE',     N'Coverage changes', 5),
        ('DETAILS',      N'Names, addresses and other details', 6),
        ('MISSING',      N'In the system but not in this file', 7),
        ('DEPENDENT',    N'New dependents', 8),
        ('PLAN',         N'Plan changes', 9),
        ('PREMIUM',      N'Premium changes', 10),
        ('EMPLOYER',     N'Employer details', 11);

    CREATE TABLE #C (Kind VARCHAR(20) NOT NULL, Entity VARCHAR(20) NOT NULL, RowNumber INT NULL, Name NVARCHAR(300) NULL,
                     Field NVARCHAR(100) NULL, OldValue NVARCHAR(400) NULL, NewValue NVARCHAR(400) NULL, Note NVARCHAR(300) NULL);

    -- Display forms: dates as MM/DD/YYYY, status as words, yes/no flags as Yes/No.
    -- ---------------------------------------------------------------- employees
    ;WITH S AS
    (
        SELECT S.StagingEmployeeId, S.RowNumber, S.EmployeeSSN,
               LTRIM(RTRIM(CONCAT(S.EmployeeLegalFirstName, N' ', S.EmployeeLegalLastName))) AS PersonName,
               S.EmployeeLegalFirstName, S.EmployeeMiddleInitial, S.EmployeeLegalLastName, S.EmployeeSuffix,
               TRY_CONVERT(date, S.EmployeeBirthdate) AS Birth, S.Address1, S.Address2, S.City, S.StateOrProvince, S.Zip, S.EmailAddress,
               TRY_CONVERT(date, S.HireDate) AS Hire, TRY_CONVERT(date, S.TerminationDate) AS Term, TRY_CONVERT(int, S.Status) AS Status,
               TRY_CONVERT(bit, S.CoverageElected) AS Elected, TRY_CONVERT(date, S.CoverageStartDate) AS CovStart,
               TRY_CONVERT(date, S.CoverageEndDate) AS CovEnd, TRY_CONVERT(date, S.DateEligibleForCoverage) AS Eligible,
               S.LowestCostPlanOffered,
               T.id AS EmployeeId, T.firstName AS O_First, T.middleName AS O_Middle, T.lastName AS O_Last, T.suffix AS O_Suffix, T.birthday AS O_Birth, T.address AS O_Address, T.address2 AS O_Address2, T.city AS O_City,
               T.state AS O_State, T.zip AS O_Zip, T.email AS O_Email, T.HireDate AS O_Hire, T.TerminationDate AS O_Term, T.Status AS OldStatus, T.CoverageElected AS O_Elected,
               T.CoverageStartDate AS O_CovStart, T.CoverageEndDate AS O_CovEnd, T.DateEligibleForCoverage AS O_Eligible, T.LowestCostPlanOffered AS OldPlan
        FROM   dbo.Staging_Employees S
        OUTER  APPLY (SELECT TOP (1) E.id FROM dbo.Employer E WHERE E.taxid = S.EINAssociatedWithEE AND E.filingYear = @PlanYear ORDER BY E.id) A
        OUTER  APPLY (SELECT TOP (1) E.* FROM dbo.Employee E WHERE E.EmployerId = COALESCE(A.id, @Parent)
                      AND E.ssn = CONVERT(varchar(50), S.EmployeeSSN) ORDER BY E.id) T
        WHERE  S.FileLogId = @FileLogId AND S.IsValid = 1
    )
    INSERT #C (Kind, Entity, RowNumber, Name, Field, OldValue, NewValue, Note)
    SELECT 'NEW_EMPLOYEE', 'Employee', S.RowNumber, S.PersonName + N' ' + NCHAR(183) + N' ' + RIGHT(REPLACE(S.EmployeeSSN, '-', ''), 4),
           N'Hire date', NULL, CONVERT(varchar(10), S.Hire, 101), NULL
    FROM   S WHERE S.EmployeeId IS NULL
    UNION ALL
    SELECT V.Kind, 'Employee', S.RowNumber, S.PersonName + N' ' + NCHAR(183) + N' ' + RIGHT(REPLACE(S.EmployeeSSN, '-', ''), 4),
           V.Field, V.OldValue, V.NewValue,
           CASE WHEN V.Kind = 'TERMINATED' AND V.OldValue IS NULL THEN N'Leaving'
                WHEN V.Kind = 'TERMINATED' AND V.NewValue IS NULL THEN N'Termination date removed' END
    FROM   S
    CROSS  APPLY (VALUES
        ('DETAILS',    N'First name',       S.O_First,    S.EmployeeLegalFirstName),
        ('DETAILS',    N'Middle initial',   S.O_Middle,   S.EmployeeMiddleInitial),
        ('DETAILS',    N'Last name',        S.O_Last,     S.EmployeeLegalLastName),
        ('DETAILS',    N'Suffix',           S.O_Suffix,   S.EmployeeSuffix),
        ('DETAILS',    N'Birth date',       CONVERT(varchar(10), S.O_Birth, 101), CONVERT(varchar(10), S.Birth, 101)),
        ('DETAILS',    N'Address',          S.O_Address,  S.Address1),
        ('DETAILS',    N'Address 2',        S.O_Address2, S.Address2),
        ('DETAILS',    N'City',             S.O_City,     S.City),
        ('DETAILS',    N'State',            S.O_State,    S.StateOrProvince),
        ('DETAILS',    N'ZIP',              S.O_Zip,      S.Zip),
        ('DETAILS',    N'Email',            S.O_Email,    S.EmailAddress),
        ('HIRE',       N'Hire date',        CONVERT(varchar(10), S.O_Hire, 101), CONVERT(varchar(10), S.Hire, 101)),
        ('TERMINATED', N'Termination date', CONVERT(varchar(10), S.O_Term, 101), CONVERT(varchar(10), S.Term, 101)),
        ('STATUS',     N'Status',
                       CASE S.OldStatus WHEN 1 THEN N'Full-time' WHEN 2 THEN N'Part-time' ELSE CONVERT(nvarchar(10), S.OldStatus) END,
                       CASE S.Status    WHEN 1 THEN N'Full-time' WHEN 2 THEN N'Part-time' ELSE CONVERT(nvarchar(10), S.Status) END),
        ('COVERAGE',   N'Coverage elected',
                       CASE S.O_Elected WHEN 1 THEN N'Yes' WHEN 0 THEN N'No' END,
                       CASE S.Elected         WHEN 1 THEN N'Yes' WHEN 0 THEN N'No' END),
        ('COVERAGE',   N'Coverage start',   CONVERT(varchar(10), S.O_CovStart, 101), CONVERT(varchar(10), S.CovStart, 101)),
        ('COVERAGE',   N'Coverage end',     CONVERT(varchar(10), S.O_CovEnd, 101), CONVERT(varchar(10), S.CovEnd, 101)),
        ('COVERAGE',   N'Eligible from',    CONVERT(varchar(10), S.O_Eligible, 101), CONVERT(varchar(10), S.Eligible, 101)),
        ('COVERAGE',   N'Lowest cost plan', S.OldPlan,    S.LowestCostPlanOffered)
    ) V (Kind, Field, OldValue, NewValue)
    WHERE  S.EmployeeId IS NOT NULL
      AND  ISNULL(NULLIF(LTRIM(RTRIM(V.OldValue)), N''), N'') <> ISNULL(NULLIF(LTRIM(RTRIM(V.NewValue)), N''), N'');

    -- ------------------------------------------- in the system, not in the file
    INSERT #C (Kind, Entity, RowNumber, Name, Field, OldValue, NewValue, Note)
    SELECT 'MISSING', 'Employee', NULL,
           LTRIM(RTRIM(CONCAT(E.firstName, N' ', E.lastName))) + N' ' + NCHAR(183) + N' ' + RIGHT(REPLACE(E.ssn, '-', ''), 4),
           N'Employer', G.name, NULL, N'Not in this file. The import leaves this employee as it is.'
    FROM   dbo.Employer G
    JOIN   dbo.Employee E ON E.EmployerId = G.id AND E.IsDeleted = 0
    WHERE  (G.id = @Parent OR G.companyId = @Parent) AND G.filingYear = @PlanYear
      AND  EXISTS (SELECT 1 FROM dbo.EmployeeCode X WHERE X.employeeId = E.id AND X.filingYear = @PlanYear)
      AND  NOT EXISTS (SELECT 1 FROM dbo.Staging_Employees S
                       WHERE S.FileLogId = @FileLogId AND CONVERT(varchar(50), S.EmployeeSSN) = E.ssn);

    -- ------------------------------------------------------------ dependents
    INSERT #C (Kind, Entity, RowNumber, Name, Field, OldValue, NewValue, Note)
    SELECT 'DEPENDENT', 'Dependent', D.RowNumber,
           LTRIM(RTRIM(CONCAT(D.DependentLegalFirstName, N' ', D.DependentLegalLastName))),
           N'Employee SSN ending', NULL, RIGHT(REPLACE(D.EmployeeSSN, '-', ''), 4), NULL
    FROM   dbo.fn_ImportAnalysis_Impact(@FileLogId, 'Dependent', 'Merge') A
    JOIN   dbo.Staging_Dependents D ON D.StagingDependentId = A.RowId
    WHERE  A.RowSource = 'Upload' AND A.ActionType = 'INSERT' AND D.IsValid = 1;

    -- ----------------------------------------------------------------- plans
    ;WITH P AS
    (
        SELECT S.RowNumber, S.PlanName, T.id AS PlanId,
               T.offeredSpouse, T.offeredDependents, T.minimumValue, T.fundingType, T.bandingType, T.planRenewal, T.waitingDays,
               T.eligibile1stOfMonth, T.planTermTermination,
               TRY_CONVERT(smallint, S.OfferedToSpouse) AS Spouse, TRY_CONVERT(smallint, S.OfferedToDependents) AS Dependents,
               TRY_CONVERT(smallint, S.MinimumValue) AS MV, S.FundingType AS NewFunding, S.BandingType AS NewBanding, TRY_CONVERT(int, S.PlanRenewalMonth) AS Renewal,
               TRY_CONVERT(int, S.WaitingPeriodNumberOfDays) AS Waiting, TRY_CONVERT(smallint, S.EligibleFirstOfTheMonth) AS FirstOfMonth,
               TRY_CONVERT(smallint, S.PlanTerminatesOnDateOfTermination) AS EndsAtTermination
        FROM   dbo.Staging_Plans S
        OUTER  APPLY (SELECT TOP (1) EP.* FROM dbo.Employer E JOIN dbo.EmployerPlan EP ON EP.employerId = E.id
                      WHERE E.taxid = S.PrimaryEIN AND E.filingYear = @PlanYear AND EP.name = S.PlanName ORDER BY EP.id) T
        WHERE  S.FileLogId = @FileLogId AND S.IsValid = 1
    )
    INSERT #C (Kind, Entity, RowNumber, Name, Field, OldValue, NewValue, Note)
    SELECT 'PLAN', 'Plan', P.RowNumber, P.PlanName, N'Plan', NULL, P.PlanName, N'New plan'
    FROM   P WHERE P.PlanId IS NULL
    UNION ALL
    SELECT 'PLAN', 'Plan', P.RowNumber, P.PlanName, V.Field, V.OldValue, V.NewValue, NULL
    FROM   P
    CROSS  APPLY (VALUES
        (N'Offered to spouse',      CONVERT(nvarchar(20), P.offeredSpouse),       CONVERT(nvarchar(20), P.Spouse)),
        (N'Offered to dependents',  CONVERT(nvarchar(20), P.offeredDependents),   CONVERT(nvarchar(20), P.Dependents)),
        (N'Minimum value',          CONVERT(nvarchar(20), P.minimumValue),        CONVERT(nvarchar(20), P.MV)),
        (N'Funding type',           CONVERT(nvarchar(20), P.fundingType),         CONVERT(nvarchar(20), P.NewFunding)),
        (N'Banding type',           CONVERT(nvarchar(50), P.bandingType),         CONVERT(nvarchar(50), P.NewBanding)),
        (N'Renewal month',          CONVERT(nvarchar(20), P.planRenewal),         CONVERT(nvarchar(20), P.Renewal)),
        (N'Waiting days',           CONVERT(nvarchar(20), P.waitingDays),         CONVERT(nvarchar(20), P.Waiting)),
        (N'Eligible first of month', CONVERT(nvarchar(20), P.eligibile1stOfMonth), CONVERT(nvarchar(20), P.FirstOfMonth)),
        (N'Ends at termination',    CONVERT(nvarchar(20), P.planTermTermination), CONVERT(nvarchar(20), P.EndsAtTermination))
    ) V (Field, OldValue, NewValue)
    WHERE  P.PlanId IS NOT NULL AND ISNULL(V.OldValue, N'') <> ISNULL(V.NewValue, N'');

    -- -------------------------------------------------------------- premiums
    INSERT #C (Kind, Entity, RowNumber, Name, Field, OldValue, NewValue, Note)
    SELECT 'PREMIUM', 'Premium', S.RowNumber, S.PlanName,
           N'EE monthly contribution from ' + ISNULL(CONVERT(varchar(10), TRY_CONVERT(date, S.StartDate), 101), N'?'),
           (SELECT TOP (1) CONVERT(nvarchar(20), T.amount) FROM dbo.Premium T JOIN dbo.EmployerPlan EP ON EP.id = T.EmployerPlanId
            JOIN dbo.Employer E ON E.id = EP.employerId
            WHERE E.taxid = S.PrimaryEIN AND E.filingYear = @PlanYear AND EP.name = S.PlanName
              AND T.bandingStartDate = TRY_CONVERT(date, S.StartDate) ORDER BY T.id),
           CONVERT(nvarchar(20), TRY_CONVERT(decimal(9,2), S.EEMonthlyContribution)),
           CASE WHEN ISNULL(S.Start, N'') <> N'' AND ISNUMERIC(S.Start) = 1 THEN N'Band ' + S.Start + NCHAR(8211) + ISNULL(S.[End], N'') END
    FROM   dbo.fn_ImportAnalysis_Impact(@FileLogId, 'Premium', 'Merge') A
    JOIN   dbo.Staging_Premiums S ON S.StagingPremiumId = A.RowId
    WHERE  A.RowSource = 'Upload' AND A.ActionType = 'INSERT' AND S.IsValid = 1;

    -- -------------------------------------------------------------- employer
    INSERT #C (Kind, Entity, RowNumber, Name, Field, OldValue, NewValue, Note)
    SELECT 'EMPLOYER', 'Employer', S.RowNumber, ISNULL(T.name, S.EmployerName), V.Field, V.OldValue, V.NewValue,
           CASE WHEN T.id IS NULL THEN N'New employer' END
    FROM   dbo.Staging_Employers S
    OUTER  APPLY (SELECT TOP (1) E.* FROM dbo.Employer E WHERE E.taxid = COALESCE(NULLIF(S.AffiliatedEIN, ''), S.PrimaryEIN)
                  AND E.filingYear = @PlanYear ORDER BY E.id) T
    CROSS  APPLY (VALUES
        (N'Name',      T.name,        S.EmployerName),
        (N'Address',   T.address,     S.Address),
        (N'Address 2', T.address2,    S.Address2),
        (N'City',      T.city,        S.City),
        (N'State',     T.state,       S.StateOrProvince),
        (N'ZIP',       T.zip,         S.Zip),
        (N'Contact',   T.contactName, S.Contact),
        (N'Phone',     T.phoneNumber, S.Phone),
        (N'Title',     T.signTitle,   S.Title)
    ) V (Field, OldValue, NewValue)
    WHERE  S.FileLogId = @FileLogId AND S.IsValid = 1
      AND  (T.id IS NULL AND V.Field = N'Name'
            OR T.id IS NOT NULL AND V.NewValue IS NOT NULL
               AND ISNULL(LTRIM(RTRIM(V.OldValue)), N'') <> ISNULL(LTRIM(RTRIM(V.NewValue)), N''));

    -- ---------------------------------------------------------------- results
    SELECT K.Kind, K.Label, COUNT(C.Kind) AS Changes
    FROM   #K K LEFT JOIN #C C ON C.Kind = K.Kind
    GROUP  BY K.Kind, K.Label, K.Seq
    ORDER  BY K.Seq;

    SELECT C.Kind, C.Entity, C.RowNumber, C.Name, C.Field, C.OldValue, C.NewValue, C.Note, COUNT(*) OVER () AS TotalCount
    FROM   #C C JOIN #K K ON K.Kind = C.Kind
    WHERE  (@Kind IS NULL OR C.Kind = @Kind)
      AND  (@Search IS NULL OR C.Name LIKE N'%' + @Search + N'%' OR C.Field LIKE N'%' + @Search + N'%'
            OR C.OldValue LIKE N'%' + @Search + N'%' OR C.NewValue LIKE N'%' + @Search + N'%')
    ORDER  BY K.Seq, C.RowNumber, C.Name, C.Field
    OFFSET CASE WHEN @PageSize IS NULL THEN 0 ELSE (@PageIndex - 1) * @PageSize END ROWS
    FETCH  NEXT ISNULL(@PageSize, 2147483647) ROWS ONLY;
END
GO
