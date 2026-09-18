/* =============================================================================
   005_F-07_covered_individual_coverage_dates.sql

   FIXES   F-07 — dependents import without their own coverage dates,
           relationship, middle name or suffix.

   CORRECTION TO THE ORIGINAL FINDING

           The audit said Part III "has no month-level coverage to report". That
           overstated it. ACALogicService.GetCoveredIndividualsAsync falls back to
           the EMPLOYEE's enrolment dates when the dependent's are null:

               CalculateMonths(year, empStart, empEnd,
                               model.CoverageStartDate ?? empStart,
                               model.CoverageEndDate   ?? empEnd)

           So Part III is populated — with the wrong span. Every dependent is
           reported as covered for exactly the months the employee was, which is
           right only while the two happen to agree.

   WHAT IT COSTS

           It is wrong in both directions, and both are misstatements on a filed
           form and on the copy the employee receives:

             - a child added mid-year at birth is reported as covered from
               January, including months before they existed on the plan
             - a dependent dropped mid-year at 26, or on divorce, is reported as
               covered to the end of the employee's span

           Staging_Dependents carries DependentCoverageStartDate and
           DependentCoverageEndDate and the import read neither. The sample rows
           in this database do populate the start date, so the correct values were
           present and discarded.

           Relationship being dropped also disables flag 'CheckDependentAge26',
           which scopes on Relationship NOT IN ('', 'Spouse', 'Domestic Partner').
           With the column always null that test excludes everyone. (That rule is
           separately broken by a quoting fault — see F-08.)

   WHAT    PART 1  Backfills coverage dates, relationship, middle name and suffix
                   onto existing CoveredIndividual rows from the staging rows they
                   came from.
           PART 2  ALTERs sp_ImportData_FromStaging to carry all five columns, and
                   fixes the duplicate guard for dependents with no SSN.

   THE DUPLICATE GUARD

           The guard was CI.ssn = S.DependentSSN alone. A dependent with no SSN —
           routine for young children — compares NULL to NULL, which is never
           true, so NOT EXISTS always passed and the row was inserted again on
           every re-import. It now falls back to name plus date of birth.

           PART 1 reports any duplicates already created; they are left in place
           because choosing which row to keep is not this script's call.

   ALSO INCLUDED

           PART 2 reproduces the F-06 premium band change as well, since both live
           in the same procedure. Running 004 first is not required, and running
           it afterwards is harmless — the bodies agree.

   COMPANION C# CHANGE — already applied in the working tree

           ACALogicService.CalculateMonths collapsed the covered window to
           DateTime.MaxValue whenever the subscriber span was null, because
           comparing a real date against a null is false in C#. That was harmless
           only while dependents had no dates of their own. Once this script lands
           them, any employee without an EmployeeEnrollment row would have had
           every dependent reported as covered for zero months. The window is now
           a proper intersection with null meaning "no limit from that side",
           verified over twelve cases.

   AFTER RUNNING THIS

           Nothing needs regenerating. Part III months are computed at read time
           from CoveredIndividual, so corrected dates take effect on the next PDF
           or XML generation. PART 3 previews the change.

   SAFE TO RE-RUN
           Yes. The backfill only touches rows whose coverage dates are still null,
           and ALTER PROCEDURE is idempotent.

   DRIFT WARNING
           PART 2 replaces the whole body of sp_ImportData_FromStaging, reproduced
           from the 20 Aug snapshot with only the F-06 and F-07 changes. The diff
           was checked: two lines for F-06, one replaced statement for F-07.
   ============================================================================= */

USE [db_ACA360];
GO

SET NOCOUNT ON;
GO

/* ---------------------------------------------------------------- PART 1 ----
   Backfill from the staging rows the dependents came from. Matched on the
   employee's SSN and the dependent's SSN, or on name and date of birth when the
   dependent has no SSN. Only rows whose coverage dates are still null are
   touched, and only where exactly one staging row matches.
   ---------------------------------------------------------------------------- */
PRINT '--- PART 1: dependents with no coverage dates of their own, BEFORE ---';
SELECT  Dependents        = COUNT(*),
        WithStartDate     = SUM(CASE WHEN CI.coverageStartDate IS NOT NULL THEN 1 ELSE 0 END),
        WithRelationship  = SUM(CASE WHEN NULLIF(LTRIM(RTRIM(CI.Relationship)), '') IS NOT NULL THEN 1 ELSE 0 END)
FROM    dbo.CoveredIndividual CI
WHERE   ISNULL(CI.IsDeleted, 0) = 0;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @Filled INT;

    ;WITH Matched AS (
        SELECT  CoveredId = CI.id,
                NewStart  = MIN(TRY_CAST(S.DependentCoverageStartDate AS DATE)),
                NewEnd    = MIN(TRY_CAST(S.DependentCoverageEndDate   AS DATE)),
                NewRel    = MIN(NULLIF(LTRIM(RTRIM(S.Relationship)), '')),
                NewMiddle = MIN(NULLIF(LTRIM(RTRIM(S.DependentMiddleInitial)), '')),
                NewSuffix = MIN(NULLIF(LTRIM(RTRIM(S.DependentSuffix)), '')),
                Variants  = COUNT(DISTINCT ISNULL(CONVERT(VARCHAR(10), TRY_CAST(S.DependentCoverageStartDate AS DATE), 120), '~')
                                         + '|'
                                         + ISNULL(CONVERT(VARCHAR(10), TRY_CAST(S.DependentCoverageEndDate AS DATE), 120), '~'))
        FROM    dbo.CoveredIndividual CI
        JOIN    dbo.EmployeeCode EC ON EC.id = CI.employeeCodeId
        JOIN    dbo.Employee     E  ON E.id  = EC.employeeId
        JOIN    dbo.Staging_Dependents S
                ON  S.EmployeeSSN = E.ssn
                AND (
                      (   NULLIF(LTRIM(RTRIM(S.DependentSSN)), '') IS NOT NULL
                      AND CI.ssn = S.DependentSSN )
                   OR (   NULLIF(LTRIM(RTRIM(S.DependentSSN)), '') IS NULL
                      AND ISNULL(LTRIM(RTRIM(CI.ssn)), '') = ''
                      AND ISNULL(LTRIM(RTRIM(CI.firstName)), '') = ISNULL(LTRIM(RTRIM(S.DependentLegalFirstName)), '')
                      AND ISNULL(LTRIM(RTRIM(CI.lastName)),  '') = ISNULL(LTRIM(RTRIM(S.DependentLegalLastName)),  '') )
                    )
        WHERE   ISNULL(CI.IsDeleted, 0) = 0
          AND   CI.coverageStartDate IS NULL
          AND   TRY_CAST(S.DependentCoverageStartDate AS DATE) IS NOT NULL
        GROUP BY CI.id
    )
    UPDATE  CI
    SET     CI.coverageStartDate = M.NewStart,
            CI.coverageEndDate   = M.NewEnd,
            CI.Relationship      = COALESCE(CI.Relationship, M.NewRel),
            CI.middleName        = COALESCE(CI.middleName,   M.NewMiddle),
            CI.suffix            = COALESCE(CI.suffix,       M.NewSuffix)
    FROM    dbo.CoveredIndividual CI
    JOIN    Matched M ON M.CoveredId = CI.id
    WHERE   M.Variants = 1;

    SET @Filled = @@ROWCOUNT;
    PRINT '--- Dependents given their own coverage dates: ' + CAST(@Filled AS VARCHAR(10)) + ' ---';

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT '--- PART 1 rolled back. ---';
    THROW;
END CATCH
GO

PRINT '--- Duplicate dependents already created by the old NULL-SSN guard ---';
PRINT '    Left in place: choosing which row to keep is not this script''s call.';
SELECT  EC.employerId, EC.filingYear,
        Employee   = E.firstName + ' ' + E.lastName,
        Dependent  = CI.firstName + ' ' + CI.lastName,
        CI.birthday,
        Copies     = COUNT(*)
FROM    dbo.CoveredIndividual CI
JOIN    dbo.EmployeeCode EC ON EC.id = CI.employeeCodeId
JOIN    dbo.Employee     E  ON E.id  = EC.employeeId
WHERE   ISNULL(CI.IsDeleted, 0) = 0
  AND   ISNULL(LTRIM(RTRIM(CI.ssn)), '') = ''
GROUP BY EC.employerId, EC.filingYear, E.firstName, E.lastName,
         CI.firstName, CI.lastName, CI.birthday
HAVING  COUNT(*) > 1
ORDER BY EC.employerId, Employee, Dependent;
GO

/* ---------------------------------------------------------------- PART 2 ----
   sp_ImportData_FromStaging — carry all five dependent columns, and fix the
   duplicate guard. Includes the F-06 premium band change from script 004.
   ---------------------------------------------------------------------------- */
 ALTER PROCEDURE [dbo].[sp_ImportData_FromStaging]  
    @FileLogId INT,  
    @ImportMode VARCHAR(20) = 'Merge'   
AS  
BEGIN  
    SET NOCOUNT ON;  
    DECLARE @TranStarted BIT = 0;  
  
    IF @@TRANCOUNT = 0  
    BEGIN  
        BEGIN TRANSACTION;  
        SET @TranStarted = 1;  
    END  
  
    BEGIN TRY  
        DECLARE      
            @PlanYear INT,      
            @PrimaryEIN VARCHAR(15),  
            @ParentEmployerId INT,  
            @ParentNameFromLog NVARCHAR(255);      
  
        SELECT      
            @PlanYear   = PlanYear,      
            @PrimaryEIN = LTRIM(RTRIM(TaxID)),  
            @ParentNameFromLog = EmployerName  
        FROM UploadedFileLog      
        WHERE FileLogId = @FileLogId;      
  
        IF @PrimaryEIN IS NULL OR @PrimaryEIN = ''  
        BEGIN  
            SELECT TOP 1 @PrimaryEIN = LTRIM(RTRIM(PrimaryEIN))  
            FROM Staging_Employers   
            WHERE FileLogId = @FileLogId AND PrimaryEIN IS NOT NULL AND LTRIM(RTRIM(PrimaryEIN)) <> '';  
              
            IF @PrimaryEIN IS NULL OR @PrimaryEIN = ''  
            BEGIN  
                SELECT TOP 1 @PrimaryEIN = LTRIM(RTRIM(AffiliatedEIN))  
                FROM Staging_Employers   
                WHERE FileLogId = @FileLogId AND AffiliatedEIN IS NOT NULL AND LTRIM(RTRIM(AffiliatedEIN)) <> '';  
            END  
        END  
  
        IF @PrimaryEIN IS NULL OR @PrimaryEIN = ''  
        BEGIN  
            THROW 50011, 'Critical Error: Primary EIN is missing. Import aborted.', 1;  
        END  
  
        IF @PlanYear IS NULL OR @PlanYear = 0  
        BEGIN  
            SELECT TOP 1 @PlanYear = YEAR(StartDate) FROM Staging_Premiums WHERE FileLogId = @FileLogId AND StartDate IS NOT NULL;  
            IF @PlanYear IS NULL  
                SELECT TOP 1 @PlanYear = YEAR(StatusStartDate) FROM Staging_Employees WHERE FileLogId = @FileLogId AND StatusStartDate IS NOT NULL;  
        END  
        SET @PlanYear = ISNULL(@PlanYear, YEAR(GETDATE()));  
  
        UPDATE UploadedFileLog SET TaxID = @PrimaryEIN, PlanYear = @PlanYear WHERE FileLogId = @FileLogId;  
  
        /* 1. ESTABLISH PARENT EMPLOYER */   
        MERGE Employer AS T  
        USING (  
            SELECT TOP 1   
                PrimaryEIN as taxid, EmployerName, Address, Address2, City, StateOrProvince, Zip, Contact, Phone, Title  
            FROM Staging_Employers   
            WHERE FileLogId = @FileLogId AND PrimaryEIN = @PrimaryEIN  
        ) AS S  
        ON T.taxid = S.taxid AND T.filingYear = @PlanYear  
        WHEN MATCHED THEN   
            UPDATE SET   
                T.name = ISNULL(S.EmployerName, T.name),  
                T.address = ISNULL(S.Address, T.address),  
                T.address2 = ISNULL(S.Address2, T.address2),  
                T.city = ISNULL(S.City, T.city),  
                T.state = ISNULL(S.StateOrProvince, T.state),  
                T.zip = ISNULL(S.Zip, T.zip),  
                T.contactName = ISNULL(S.Contact, T.contactName),  
                T.phoneNumber = ISNULL(S.Phone, T.phoneNumber),  
                T.signTitle = ISNULL(S.Title, T.signTitle),  
                T.FileLogId = @FileLogId,  
                T.companyId = NULL  
        WHEN NOT MATCHED THEN  
            INSERT (FileLogId, taxid, name, filingYear, address, address2, city, state, zip, contactName, phoneNumber, signTitle, companyId, isAuthoritative)  
            VALUES (@FileLogId, @PrimaryEIN, S.EmployerName, @PlanYear, S.Address, S.Address2, S.City, S.StateOrProvince, S.Zip, S.Contact, S.Phone, S.Title, NULL, 1);  
  
        SELECT @ParentEmployerId = id FROM Employer WHERE taxid = @PrimaryEIN AND filingYear = @PlanYear;  
  
        IF @ParentEmployerId IS NULL  
            THROW 50010, 'Critical Error: Could not establish ParentEmployerId. Import aborted.', 1;  
  
        /* 0.5. OVERWRITE CLEANUP */   
        IF @ImportMode = 'Overwrite'  
        BEGIN  
            DECLARE @TargetEmployeeCodes TABLE (Id INT);  
            INSERT INTO @TargetEmployeeCodes (Id) SELECT id FROM EmployeeCode WHERE employerId = @ParentEmployerId AND filingYear = @PlanYear;  
  
            DELETE FROM EmployeeEnrollment WHERE employeeCodeId IN (SELECT Id FROM @TargetEmployeeCodes);  
            DELETE FROM EmployeePayroll WHERE employeeCodeId IN (SELECT Id FROM @TargetEmployeeCodes);  
            DELETE FROM CoveredIndividual WHERE employeeCodeId IN (SELECT Id FROM @TargetEmployeeCodes);  
            DELETE FROM EmployeeHireSpan WHERE employeeCodeId IN (SELECT Id FROM @TargetEmployeeCodes);  
            DELETE FROM Premium WHERE EmployerId = @ParentEmployerId;  
            DELETE FROM Employer_AggregatedGroupMembers WHERE EmployerId = @ParentEmployerId;  
        END  
  
        /* 2. AFFILIATE EMPLOYERS (FIXED DUPLICATE CRASH) */   
        MERGE Employer AS T      
        USING (      
            SELECT AffiliatedEIN, EmployerName, Address, Address2, City, StateOrProvince, Zip, Contact, Phone, Title
            FROM (
                SELECT AffiliatedEIN, EmployerName, Address, Address2, City, StateOrProvince, Zip, Contact, Phone, Title,
                       ROW_NUMBER() OVER(PARTITION BY AffiliatedEIN ORDER BY (SELECT NULL)) as rn
                FROM Staging_Employers
                WHERE FileLogId = @FileLogId AND IsValid = 1 AND AffiliatedEIN <> @PrimaryEIN
            ) Sub WHERE rn = 1
        ) AS S      
        ON T.taxid = S.AffiliatedEIN AND T.filingYear = @PlanYear      
        WHEN MATCHED THEN      
            UPDATE SET   
                name = S.EmployerName, address = S.Address, address2 = S.Address2, city = S.City, state = S.StateOrProvince, zip = S.Zip,  
                companyId = @ParentEmployerId, FileLogId = @FileLogId      
        WHEN NOT MATCHED THEN      
            INSERT (FileLogId, taxid, name, filingYear, address, address2, city, state, zip, contactName, phoneNumber, signTitle, companyId, isAuthoritative)      
            VALUES (@FileLogId, S.AffiliatedEIN, S.EmployerName, @PlanYear, S.Address, S.Address2, S.City, S.StateOrProvince, S.Zip, S.Contact, S.Phone, S.Title, @ParentEmployerId, 0);      
        
       /* 2.5. TRACKER EXTENSION HEALING (FIXED) */   
        INSERT INTO [dbo].[Tracker_EmployerExtension] (EmployerId)
        SELECT DISTINCT E.id
        FROM [dbo].[Employer] E
        LEFT JOIN [dbo].[Tracker_EmployerExtension] EE ON E.id = EE.EmployerId
        WHERE (E.FileLogId = @FileLogId OR E.id = @ParentEmployerId)
          AND EE.EmployerId IS NULL;
  
        /* 3. AGGREGATED GROUP MEMBERS */      
        INSERT INTO Employer_AggregatedGroupMembers (EmployerId, MemberName, MemberEIN)      
        SELECT DISTINCT @ParentEmployerId, S.EmployerName, S.AffiliatedEIN      
        FROM Staging_Employers S      
        WHERE S.FileLogId = @FileLogId      
          AND NOT EXISTS (SELECT 1 FROM Employer_AggregatedGroupMembers AG WHERE AG.MemberEIN = S.AffiliatedEIN AND AG.EmployerId = @ParentEmployerId);      
  
        /* 4. EMPLOYEES (FIXED DUPLICATE CRASH) */      
        MERGE Employee AS T      
        USING (      
            SELECT * FROM (
                SELECT      
                    S.EmployeeSSN, S.EmployeeLegalFirstName, S.EmployeeLegalLastName, S.EmployeeMiddleInitial, S.EmployeeSuffix,  
                    S.Address1, S.Address2, S.City, S.StateOrProvince, S.Zip, S.Country,  
                    S.EmailAddress,  
                    S.CoverageElected,   
                    S.LowestCostPlanOffered,  
                    TRY_CAST(S.CoverageStartDate AS DATE) AS CoverageStartDate,  
                    TRY_CAST(S.CoverageEndDate AS DATE) AS CoverageEndDate,  
                    TRY_CAST(S.DateEligibleForCoverage AS DATE) AS DateEligibleForCoverage,  
                    TRY_CAST(S.EmployeeBirthdate AS DATE) AS BirthDate,      
                    TRY_CAST(S.HireDate AS DATE) AS HireDate,      
                    TRY_CAST(S.TerminationDate AS DATE) AS TerminationDate,      
                    TRY_CAST(S.Status AS INT) AS Status,      
                    COALESCE(E.id, @ParentEmployerId) AS FinalEmployerId,
                    ROW_NUMBER() OVER(PARTITION BY S.EmployeeSSN, COALESCE(E.id, @ParentEmployerId) ORDER BY ISNULL(TRY_CAST(S.StatusStartDate AS DATE), TRY_CAST(S.HireDate AS DATE)) DESC) as rn
                FROM Staging_Employees S      
                LEFT JOIN Employer E ON S.EINAssociatedWithEE = E.taxid AND E.filingYear = @PlanYear      
                WHERE S.FileLogId = @FileLogId AND S.IsValid = 1  
            ) Sub WHERE rn = 1
        ) AS S      
        ON T.ssn = S.EmployeeSSN AND T.EmployerId = S.FinalEmployerId      
        WHEN MATCHED THEN      
            UPDATE SET   
                firstName = S.EmployeeLegalFirstName,   
                lastName = S.EmployeeLegalLastName,   
                middleName = S.EmployeeMiddleInitial,   
                suffix = S.EmployeeSuffix,  
                address = S.Address1,   
                address2 = S.Address2,   
                city = S.City,   
                state = S.StateOrProvince,   
                zip = S.Zip,  
                birthday = S.BirthDate,   
                HireDate = S.HireDate,   
                TerminationDate = S.TerminationDate,   
                Status = S.Status,  
                CoverageElected = S.CoverageElected,  
                CoverageStartDate = S.CoverageStartDate,  
                CoverageEndDate = S.CoverageEndDate,  
                LowestCostPlanOffered = S.LowestCostPlanOffered,  
                DateEligibleForCoverage = S.DateEligibleForCoverage,  
                email = S.EmailAddress,  
                IsDeleted = 0      
        WHEN NOT MATCHED THEN      
            INSERT (  
                FileLogId, EmployerId, fullName, firstName, lastName, middleName, suffix,   
                ssn, address, address2, city, state, zip, birthday, HireDate, TerminationDate,   
                Status, CoverageElected, CoverageStartDate, CoverageEndDate,   
                LowestCostPlanOffered, DateEligibleForCoverage, email, IsDeleted  
            )      
            VALUES (  
                @FileLogId, S.FinalEmployerId, S.EmployeeLegalFirstName + ' ' + S.EmployeeLegalLastName,   
                S.EmployeeLegalFirstName, S.EmployeeLegalLastName, S.EmployeeMiddleInitial, S.EmployeeSuffix,   
                S.EmployeeSSN, S.Address1, S.Address2, S.City, S.StateOrProvince, S.Zip,   
                S.BirthDate, S.HireDate, S.TerminationDate, S.Status,   
                S.CoverageElected, S.CoverageStartDate, S.CoverageEndDate,   
                S.LowestCostPlanOffered, S.DateEligibleForCoverage, S.EmailAddress, 0  
            );  

        /* 5. EMPLOYEE CODES & HIRE SPANS (FIXED MULTI-SPAN ISSUE) */      
        INSERT INTO EmployeeCode (employeeId, employerId, filingYear, getForm, isCorrected, isVoid, disableCoding)      
        SELECT E.id, E.EmployerId, @PlanYear, 1, 0, 0, 0      
        FROM Employee E WHERE E.FileLogId = @FileLogId      
        AND NOT EXISTS (SELECT 1 FROM EmployeeCode EC WHERE EC.employeeId = E.id AND EC.filingYear = @PlanYear);      
  
               INSERT INTO EmployeeHireSpan (employeeCodeId, startDate, endDate)
        SELECT DISTINCT EC.id, TRY_CAST(S.HireDate AS DATE), TRY_CAST(S.TerminationDate AS DATE)
        FROM Staging_Employees S
        LEFT JOIN Employer Aff ON Aff.taxid = S.EINAssociatedWithEE AND Aff.filingYear = @PlanYear
        JOIN Employee E ON S.EmployeeSSN = E.ssn AND E.EmployerId = COALESCE(Aff.id, @ParentEmployerId)
        JOIN EmployeeCode EC ON EC.employeeId = E.id AND EC.filingYear = @PlanYear
        WHERE S.FileLogId = @FileLogId AND S.IsValid = 1 AND S.HireDate IS NOT NULL
        AND NOT EXISTS (SELECT 1 FROM EmployeeHireSpan HS WHERE HS.employeeCodeId = EC.id AND HS.startDate = TRY_CAST(S.HireDate AS DATE));
  
        /* 5b. EMPLOYEE STATUS PERIODS (one row per distinct status span) -- FIX 1 (NEW) */
               INSERT INTO EmployeeStatus (employeeCodeId, status, startDate, endDate)
        SELECT DISTINCT EC.id, TRY_CAST(S.Status AS INT), TRY_CAST(S.StatusStartDate AS DATE), TRY_CAST(S.StatusEndDate AS DATE)
        FROM Staging_Employees S
        LEFT JOIN Employer Aff ON Aff.taxid = S.EINAssociatedWithEE AND Aff.filingYear = @PlanYear
        JOIN Employee E ON E.ssn = S.EmployeeSSN AND E.EmployerId = COALESCE(Aff.id, @ParentEmployerId)
        JOIN EmployeeCode EC ON EC.employeeId = E.id AND EC.filingYear = @PlanYear
        WHERE S.FileLogId = @FileLogId AND S.IsValid = 1
          AND TRY_CAST(S.StatusStartDate AS DATE) IS NOT NULL
          AND NOT EXISTS (SELECT 1 FROM EmployeeStatus ST WHERE ST.employeeCodeId = EC.id
                AND ST.startDate = TRY_CAST(S.StatusStartDate AS DATE)
                AND ISNULL(ST.status,-1) = ISNULL(TRY_CAST(S.Status AS INT),-1));
  
  
        /* 6. PLANS & PREMIUMS */      
        MERGE EmployerPlan AS T      
        USING (  
            SELECT DISTINCT   
                SP.PlanName,   
                E.id AS EmpId,   
                CASE WHEN LTRIM(RTRIM(SP.PlanType)) = 'Medical' THEN 1 ELSE 0 END AS PlanTypeInt,  
                TRY_CAST(SP.OfferedToSpouse AS SMALLINT) AS OfferedToSpouseInt,   
                TRY_CAST(SP.OfferedToDependents AS SMALLINT) AS OfferedToDependentsInt,  
                SP.MinimumValue,   
                SP.FundingType,   
                SP.BandingType,   
                SP.PlanRenewalMonth,   
                SP.WaitingPeriodNumberOfDays,  
                SP.EligibleFirstOfTheMonth,  
                SP.PlanTerminatesOnDateOfTermination  
            FROM Staging_Plans SP   
            JOIN Employer E ON SP.PrimaryEIN = E.taxid AND E.filingYear = @PlanYear    
            WHERE SP.FileLogId = @FileLogId AND SP.IsValid = 1  
        ) AS S      
        ON T.employerId = S.EmpId AND T.name = S.PlanName      
        WHEN MATCHED THEN   
            UPDATE SET   
                medicalPlan = S.PlanTypeInt,  
                offeredSpouse = S.OfferedToSpouseInt,   
                offeredDependents = S.OfferedToDependentsInt,  
                minimumValue = S.MinimumValue,   
                fundingType = S.FundingType,   
                bandingType = S.BandingType,   
                planRenewal = S.PlanRenewalMonth,   
                waitingDays = S.WaitingPeriodNumberOfDays,  
                eligibile1stOfMonth = S.EligibleFirstOfTheMonth,  
                planTermTermination = S.PlanTerminatesOnDateOfTermination  
        WHEN NOT MATCHED THEN   
            INSERT (  
                FileLogId, employerId, name, medicalPlan,   
                offeredSpouse, offeredDependents,   
                minimumValue, fundingType, bandingType, planRenewal, waitingDays,   
                eligibile1stOfMonth, planTermTermination,   
                code1A, code2F, code2G, code2H   
            )   
            VALUES (  
                @FileLogId, S.EmpId, S.PlanName, S.PlanTypeInt,  
                S.OfferedToSpouseInt, S.OfferedToDependentsInt,  
                S.MinimumValue, S.FundingType, S.BandingType, S.PlanRenewalMonth, S.WaitingPeriodNumberOfDays,   
                S.EligibleFirstOfTheMonth, S.PlanTerminatesOnDateOfTermination,   
                0, 1, 1, 1  
            );  
  
        /* 7. PREMIUMS */  
        INSERT INTO Premium (EmployerPlanId, bandingValueStart, bandingValueEnd, bandingStartDate, bandingEndDate, amount, EmployerId)      
        SELECT   
            EP.id,   
            ISNULL(NULLIF(LTRIM(RTRIM(SP.[Start])), ''), SP.BandingType),   
            ISNULL(NULLIF(LTRIM(RTRIM(SP.[End])),   ''), SP.BandingType),   
            SP.StartDate,   
            SP.EndDate,   
            SP.EEMonthlyContribution,   
            EP.employerId   
        FROM Staging_Premiums SP   
        JOIN EmployerPlan EP ON   
            REPLACE(LTRIM(RTRIM(EP.name)), CHAR(160), '') = REPLACE(LTRIM(RTRIM(SP.PlanName)), CHAR(160), '')  
            AND EP.FileLogId = @FileLogId   
        WHERE SP.FileLogId = @FileLogId   
            AND SP.IsValid = 1   
            AND NOT EXISTS (  
                SELECT 1 FROM Premium P   
                WHERE P.EmployerPlanId = EP.id   
                AND P.bandingStartDate = SP.StartDate   
                AND P.amount = SP.EEMonthlyContribution  
            );  
  
        /* 8. ENROLLMENT & PAYROLL & DEPENDENTS */      
        INSERT INTO EmployeeEnrollment
            (employeeCodeId, planId, isEnrolled, coverageOfferDate, coverageStartDate, coverageEndDate,
             unionMember, contributionStartDate, contributionEndDate,
             COBRAEnrolled, COBRAStartDate, COBRAEndDate, RetireeEnrolled, RetireeStartDate, RetireeEndDate)   -- FIX 2
        SELECT DISTINCT
            EC.id, EP.id, ISNULL(TRY_CAST(S.CoverageElected AS SMALLINT),0),
            TRY_CAST(S.DateEligibleForCoverage AS DATE), TRY_CAST(S.CoverageStartDate AS DATE), TRY_CAST(S.CoverageEndDate AS DATE),
            ISNULL(TRY_CAST(S.UnionEmployee AS SMALLINT),0), TRY_CAST(S.DateEmployerFirstPaysToUnionBenefits AS DATE), TRY_CAST(S.DateEmployerLastPaysToUnionBenefits AS DATE),
            ISNULL(TRY_CAST(S.COBRABenefitsElected AS SMALLINT),0), TRY_CAST(S.COBRACoverageEffectiveDate AS DATE), TRY_CAST(S.COBRACoverageEndDate AS DATE),
            ISNULL(TRY_CAST(S.RetireeBenefitsElected AS SMALLINT),0), TRY_CAST(S.RetireePlanStartDate AS DATE), TRY_CAST(S.RetireePlanEndDate AS DATE)
                FROM Staging_Employees S
        LEFT JOIN Employer Aff ON Aff.taxid = S.EINAssociatedWithEE AND Aff.filingYear = @PlanYear
        JOIN Employee E ON E.ssn = S.EmployeeSSN AND E.EmployerId = COALESCE(Aff.id, @ParentEmployerId)
        JOIN EmployeeCode EC ON EC.employeeId = E.id AND EC.filingYear = @PlanYear
        LEFT JOIN EmployerPlan EP ON REPLACE(LTRIM(RTRIM(EP.name)),CHAR(160),'') = REPLACE(LTRIM(RTRIM(S.LowestCostPlanOffered)),CHAR(160),'') AND EP.employerId = EC.employerId
        WHERE S.FileLogId = @FileLogId AND S.IsValid = 1
            AND ( S.LowestCostPlanOffered IS NOT NULL
                 OR ISNULL(TRY_CAST(S.UnionEmployee AS SMALLINT),0)=1
                 OR ISNULL(TRY_CAST(S.COBRABenefitsElected AS SMALLINT),0)=1
                 OR ISNULL(TRY_CAST(S.RetireeBenefitsElected AS SMALLINT),0)=1 )
            AND NOT EXISTS (SELECT 1 FROM EmployeeEnrollment EE WHERE EE.employeeCodeId = EC.id
                    AND ISNULL(EE.planId,-1) = ISNULL(EP.id,-1)
                    AND ISNULL(EE.coverageStartDate,'1900-01-01') = ISNULL(TRY_CAST(S.CoverageStartDate AS DATE),'1900-01-01')
                    AND ISNULL(EE.coverageOfferDate,'1900-01-01')  = ISNULL(TRY_CAST(S.DateEligibleForCoverage AS DATE),'1900-01-01'));      
  
        INSERT INTO EmployeePayroll
            (employeeCodeId, payPeriodStartDate, payPeriodEndDate, payPeriodTotalHours, payPeriodSalaryAmount, payPeriodAdditional)   -- FIX 3
        SELECT
            EC.id, ISNULL(TRY_CAST(S.StatusStartDate AS DATE), E.HireDate), ISNULL(TRY_CAST(S.StatusEndDate AS DATE), '2099-12-31'),
            TRY_CAST(S.HourlyRate AS DECIMAL(18,2)),                 -- convention: hourly RATE stored here
            TRY_CAST(S.W2IncomeOrAnnualSalary AS DECIMAL(18,2)),     -- convention: W-2 / annual salary
            TRY_CAST(S.AdditionalIncome AS DECIMAL(18,2))            -- NEW: additional income (was dropped)
                FROM Staging_Employees S
        LEFT JOIN Employer Aff ON Aff.taxid = S.EINAssociatedWithEE AND Aff.filingYear = @PlanYear
        JOIN Employee E ON E.ssn = S.EmployeeSSN AND E.EmployerId = COALESCE(Aff.id, @ParentEmployerId)
        JOIN EmployeeCode EC ON EC.employeeId = E.id AND EC.filingYear = @PlanYear
        WHERE S.FileLogId = @FileLogId
            AND (S.W2IncomeOrAnnualSalary IS NOT NULL OR S.HourlyRate IS NOT NULL OR S.AdditionalIncome IS NOT NULL)
            AND NOT EXISTS (SELECT 1 FROM EmployeePayroll PR WHERE PR.employeeCodeId = EC.id AND PR.payPeriodStartDate = ISNULL(TRY_CAST(S.StatusStartDate AS DATE), E.HireDate));      
  
        /* F-07: carry the dependent's own coverage dates, relationship, middle
           name and suffix across. Staging_Dependents supplies all five and the
           import previously took none of them, so every dependent inherited the
           employee's coverage span via the fallback in ACALogicService.
           CalculateMonths -- correct only while the two spans happen to agree. */
        INSERT INTO CoveredIndividual (employeeCodeId, firstName, middleName, lastName, suffix,
                                       ssn, birthday, coverageStartDate, coverageEndDate,
                                       Relationship, EmployerId)
        SELECT
            EC.id,
            S.DependentLegalFirstName,
            NULLIF(LTRIM(RTRIM(S.DependentMiddleInitial)), ''),
            S.DependentLegalLastName,
            NULLIF(LTRIM(RTRIM(S.DependentSuffix)), ''),
            S.DependentSSN,
            TRY_CAST(S.DependentBirthdate AS DATE),
            TRY_CAST(S.DependentCoverageStartDate AS DATE),
            TRY_CAST(S.DependentCoverageEndDate   AS DATE),
            NULLIF(LTRIM(RTRIM(S.Relationship)), ''),
            EC.employerId
                FROM Staging_Dependents S
        JOIN Employee E ON S.EmployeeSSN = E.ssn
             AND (E.EmployerId = @ParentEmployerId
                  OR E.EmployerId IN (SELECT id FROM Employer WHERE companyId = @ParentEmployerId))
        JOIN EmployeeCode EC ON EC.employeeId = E.id AND EC.filingYear = @PlanYear
        WHERE S.FileLogId = @FileLogId AND S.IsValid = 1
            /* The guard used to be CI.ssn = S.DependentSSN alone. A dependent with
               no SSN -- routine for young children -- compares NULL to NULL, which
               is never true, so NOT EXISTS always passed and the row was inserted
               again on every re-import. Fall back to name plus date of birth when
               there is no SSN to match on. */
            AND NOT EXISTS (
                    SELECT 1
                    FROM   CoveredIndividual CI
                    WHERE  CI.employeeCodeId = EC.id
                      AND  (
                             (   NULLIF(LTRIM(RTRIM(S.DependentSSN)), '') IS NOT NULL
                             AND CI.ssn = S.DependentSSN )
                          OR (   NULLIF(LTRIM(RTRIM(S.DependentSSN)), '') IS NULL
                             AND ISNULL(LTRIM(RTRIM(CI.ssn)), '') = ''
                             AND ISNULL(LTRIM(RTRIM(CI.firstName)), '') = ISNULL(LTRIM(RTRIM(S.DependentLegalFirstName)), '')
                             AND ISNULL(LTRIM(RTRIM(CI.lastName)),  '') = ISNULL(LTRIM(RTRIM(S.DependentLegalLastName)),  '')
                             AND ISNULL(CI.birthday, '1900-01-01') = ISNULL(TRY_CAST(S.DependentBirthdate AS DATE), '1900-01-01') )
                           )
                );
  
        /* 9. FINALIZE & SUMMARY */      
        DELETE FROM ImportValidationSummary WHERE FileLogId = @FileLogId;  
        INSERT INTO ImportValidationSummary (FileLogId, TotalEmployees, EmployeesWithHireDate, EmployeesWithStatus, PayrollRows, EnrollmentRows, PlanCount, IsImportComplete, ValidationMessage, DependentRows, TotalPayrollImported)      
        SELECT @FileLogId, (SELECT COUNT(*) FROM Employee WHERE FileLogId = @FileLogId), (SELECT COUNT(*) FROM Employee WHERE FileLogId = @FileLogId AND HireDate IS NOT NULL), (SELECT COUNT(*) FROM Employee WHERE FileLogId = @FileLogId AND Status IS NOT 
        NULL), (SELECT COUNT(*) FROM EmployeePayroll EP JOIN EmployeeCode EC ON EP.employeeCodeId = EC.id JOIN Employee EMP ON EC.employeeId = EMP.id WHERE EMP.FileLogId = @FileLogId), (SELECT COUNT(*) FROM EmployeeEnrollment EE JOIN EmployeeCode EC ON EE.employeeCodeId = EC.id JOIN Employee EMP ON EC.employeeId = EMP.id WHERE EMP.FileLogId = @FileLogId), (SELECT COUNT(*) FROM EmployerPlan WHERE FileLogId = @FileLogId), 1, 'Import complete', (SELECT COUNT(*) FROM CoveredIndividual CI JOIN EmployeeCode EC ON CI.employeeCodeId = EC.id JOIN Employee E ON EC.employeeId = E.id WHERE E.FileLogId = @FileLogId), (SELECT ISNULL(SUM(payPeriodSalaryAmount), 0) FROM EmployeePayroll EP JOIN EmployeeCode EC ON EP.employeeCodeId = EC.id JOIN Employee EMP ON EC.employeeId = EMP.id WHERE EMP.FileLogId = @FileLogId) FROM UploadedFileLog WHERE FileLogId = @FileLogId;      
  
        UPDATE UploadedFileLog SET WorkflowStatusId = 40, ValidationStatusId = 5, ValidationStatus = 'Imported', ProcessingEndTime = SYSUTCDATETIME() WHERE FileLogId = @FileLogId;      
  
        IF @TranStarted = 1  
            COMMIT TRANSACTION;  
    END TRY  
    BEGIN CATCH  
        IF @@TRANCOUNT > 0   
            ROLLBACK TRANSACTION;  
  
        THROW;  
    END CATCH  
END  
GO

/* ---------------------------------------------------------------- PART 3 ----
   What changed, and which dependents now report a different Part III span.
   Nothing needs regenerating: Part III months are computed at read time from
   CoveredIndividual, so the next PDF or XML picks these up.
   ---------------------------------------------------------------------------- */
PRINT '--- Dependents with their own coverage dates, AFTER ---';
SELECT  Dependents        = COUNT(*),
        WithStartDate     = SUM(CASE WHEN CI.coverageStartDate IS NOT NULL THEN 1 ELSE 0 END),
        WithRelationship  = SUM(CASE WHEN NULLIF(LTRIM(RTRIM(CI.Relationship)), '') IS NOT NULL THEN 1 ELSE 0 END)
FROM    dbo.CoveredIndividual CI
WHERE   ISNULL(CI.IsDeleted, 0) = 0;

PRINT '--- Dependents whose span now differs from the employee they sit under ---';
PRINT '    These are the Part III rows that were being misreported.';
SELECT  EC.employerId,
        EC.filingYear,
        Employee     = E.firstName + ' ' + E.lastName,
        Dependent    = CI.firstName + ' ' + CI.lastName,
        CI.Relationship,
        EmployeeFrom = EN.coverageStartDate,
        EmployeeTo   = EN.coverageEndDate,
        DependentFrom= CI.coverageStartDate,
        DependentTo  = CI.coverageEndDate,
        Note         = CASE
                          WHEN CI.coverageStartDate > ISNULL(EN.coverageStartDate, '1900-01-01')
                               THEN 'joined later than the employee'
                          WHEN CI.coverageEndDate IS NOT NULL
                               AND (EN.coverageEndDate IS NULL OR CI.coverageEndDate < EN.coverageEndDate)
                               THEN 'left earlier than the employee'
                          ELSE 'same span'
                       END
FROM    dbo.CoveredIndividual CI
JOIN    dbo.EmployeeCode EC ON EC.id = CI.employeeCodeId
JOIN    dbo.Employee     E  ON E.id  = EC.employeeId
OUTER APPLY (
    SELECT TOP 1 X.coverageStartDate, X.coverageEndDate
    FROM   dbo.EmployeeEnrollment X
    WHERE  X.employeeCodeId = EC.id
    ORDER BY X.id DESC
) EN
WHERE   ISNULL(CI.IsDeleted, 0) = 0
  AND   CI.coverageStartDate IS NOT NULL
  AND   (
            CI.coverageStartDate <> ISNULL(EN.coverageStartDate, CI.coverageStartDate)
         OR ISNULL(CI.coverageEndDate, '2099-12-31') <> ISNULL(EN.coverageEndDate, '2099-12-31')
        )
ORDER BY EC.employerId, Employee, Dependent;
GO
