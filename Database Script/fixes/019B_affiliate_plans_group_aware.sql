/* =============================================================================
   019B_affiliate_plans_group_aware.sql        ***  OPTION B -- CANDIDATE  ***

   DO NOT RUN BOTH. This and 019A fix the same bug two different ways. Pick one.

   ---------------------------------------------------------------------------
   THE BUG   (identical to 019A -- same diagnosis, different remedy)

   CARMEN DELACROIX came out of the code generator as  1H - 2C.

   That pair cannot both be true. 2C means "employee enrolled in the coverage
   offered". 1H means "no offer of coverage was made". She is full time,
   enrolled in MEDICAL PPO CORE with a spouse and a child, and should read
   1E / $165.00 / 2C exactly as SARAH MITCHELL does.

   The Plan Information tab is keyed on Primary EIN and has no affiliate column,
   so every plan is created under the PARENT employer. Employees are split
   across employer records by their own EIN. ATLANTIC COASTAL SERVICES LLC
   (1014) therefore holds two employees and zero plans, while all five plans sit
   under ATLANTIC HEALTH PARTNERS INC (1013).

   Three lookups then fail for an affiliate employee, because each is scoped to
   a single employerId:

     1. sp_ImportData_FromStaging, enrolment insert
            LEFT JOIN EmployerPlan EP ON ... AND EP.employerId = EC.employerId
        LEFT JOIN, so planId lands NULL. EnrolledMo still fires, giving 2C,
        while PlanId stays NULL, giving 1H. This is the contradiction.

     2. sp_Generate1095Codes, premium lookup
            WHERE pr.EmployerId = @EmployerId
        Empty for an affiliate, so Line 15 is blank and no affordability safe
        harbour can be assigned -- 2F, 2G and 2H are all decided by comparing
        against that premium.

     3. sp_Generate1095Codes, snapshot plan fallback
            AND P.employerId = @EmployerId
        The path taken by employees with no enrolment row.

   This is not specific to the test file. ANY client with affiliate EINs has
   every affiliate employee reported as having received no offer of coverage --
   the 4980H(a) trigger, the larger of the two penalties, asserted against an
   employer who did in fact offer coverage.

   ---------------------------------------------------------------------------
   WHAT OPTION B DOES

   Leaves the data alone -- one set of plans, owned by the parent -- and widens
   the three lookups to search the whole aggregated group.

   Employer.companyId is the parent pointer. The importer already sets it on
   affiliates and leaves it NULL on the parent, so the group is derivable
   without any schema change:

       the employer itself
       + its parent, if it has one
       + everything sharing that parent
       + everything whose parent is the employer itself

   For a standalone employer that set is just itself, so behaviour is unchanged.

   THREE procedures change:
       sp_ImportData_FromStaging   (one join condition)
       sp_Generate1095Codes        (a table variable + two filters)
       sp_AffordabilityExport      (its premium subquery, same fault)

   WHAT IT COSTS
     - The 265-line code generator has to be redeployed. It was patched by
       string replacement against the live definition rather than retyped; the
       diff is four hunks and nothing else moves.
     - The rule now lives in procedure logic rather than in the data, so every
       FUTURE procedure that joins EmployerPlan or Premium has to remember it.
       That is the real long-term cost, and it is the argument for 019A.
     - sp_Generate1095Codes_V5 carries the same two faults. The application does
       not call it, so it is left alone -- worth deleting if it is genuinely dead.

   WHAT IT BUYS
     - No duplicated plan or premium rows; one plan stays one row.
     - Nothing can double-count a plan across a group.
     - The aggregated-group relationship stays explicit in the code that needs
       it, rather than being flattened away at import time.

   ---------------------------------------------------------------------------
   BUILT ON SCRIPT 018. Both procedures below already contain 018's fixes. Run
   018 first, or run this instead -- either way these versions are the later
   ones.

   AFTER RUNNING: regenerate the codes for both employers, then re-export.
   ============================================================================= */

USE [db_ACA360];
GO

SET NOCOUNT ON;
GO

/* =============================================================================
   PART 1 -- sp_ImportData_FromStaging: resolve an employee's plan anywhere in
             the group, not only under their own employer record.
   ============================================================================= */
CREATE OR ALTER PROCEDURE [dbo].[sp_ImportData_FromStaging]  
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
        INSERT INTO Premium (EmployerPlanId, bandingValueStart, bandingValueEnd, bandingStartDate, bandingEndDate, amount, EmployerId, FileLogId)      
        SELECT   
            EP.id,   
            ISNULL(NULLIF(LTRIM(RTRIM(SP.[Start])), ''), SP.BandingType),   
            ISNULL(NULLIF(LTRIM(RTRIM(SP.[End])),   ''), SP.BandingType),   
            SP.StartDate,   
            SP.EndDate,   
            SP.EEMonthlyContribution,   
            EP.employerId,   
            @FileLogId   
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
        LEFT JOIN EmployerPlan EP ON REPLACE(LTRIM(RTRIM(EP.name)),CHAR(160),'') = REPLACE(LTRIM(RTRIM(S.LowestCostPlanOffered)),CHAR(160),'') AND EP.employerId IN (SELECT g.id FROM Employer g WHERE g.id = EC.employerId OR g.id = (SELECT p2.companyId FROM Employer p2 WHERE p2.id = EC.employerId))
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

    -- LINE 14 / 15 (group-plan path — unchanged)
    UPDATE #C SET Line14 = CASE
        WHEN PlanId IS NULL THEN '1H' WHEN mv=0 THEN '1F'
        WHEN cond=1 AND dep=1 THEN '1K' WHEN cond=1 AND dep=0 THEN '1J'
        WHEN sp=1 AND dep=1 AND code1A=1 AND Prem IS NOT NULL AND Prem<=@MonthlyFPL THEN '1A'
        WHEN sp=1 AND dep=1 THEN '1E' WHEN sp=1 AND dep=0 THEN '1D'
        WHEN sp=0 AND dep=1 THEN '1C' ELSE '1B' END,
      Line15 = CASE WHEN PlanId IS NULL OR mv=0 THEN NULL
                    WHEN sp=1 AND dep=1 AND code1A=1 AND Prem<=@MonthlyFPL THEN NULL ELSE Prem END;
    UPDATE #C SET Line14='1G', Line15=NULL WHERE IsFT=0 AND EnrolledMo=1 AND funding='2';

    -- LINE 16 (precedence — group-plan path unchanged)
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

    DROP TABLE #M; DROP TABLE #E; DROP TABLE #PP; DROP TABLE #Pay; DROP TABLE #C; DROP TABLE #PSM;
    IF @TranStarted=1 COMMIT TRANSACTION;
    END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK TRANSACTION; THROW; END CATCH
END


GO

GO

/* =============================================================================
   PART 3 -- sp_AffordabilityExport: same fault, same widening.

   This is 018's corrected version (right payroll columns, year-scoped premium)
   with the premium subquery extended across the group. If you run 019B you get
   018's Part 3 as well, so there is no need to run both.
   ============================================================================= */

CREATE OR ALTER PROCEDURE [dbo].[sp_AffordabilityExport]
    @EmployerId INT,
    @Year       INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @YearStart DATE = DATEFROMPARTS(@Year, 1, 1);
    DECLARE @YearEnd   DATE = DATEFROMPARTS(@Year, 12, 31);

    /* The aggregated ALE group. Plans live under the parent only, so an
       affiliate's employees have to look upward to find theirs. */
    DECLARE @Group TABLE (id INT PRIMARY KEY);
    INSERT @Group (id)
    SELECT g.id
    FROM   dbo.Employer g
    WHERE  g.id = @EmployerId
       OR  g.id = (SELECT p.companyId FROM dbo.Employer p WHERE p.id = @EmployerId)
       OR  g.companyId = (SELECT p.companyId FROM dbo.Employer p WHERE p.id = @EmployerId)
       OR  g.companyId = @EmployerId;

    BEGIN TRY
        SELECT
            e.id            AS EmployeeId,
            e.ssn           AS SSN,
            e.firstName     AS FirstName,
            e.lastName      AS LastName,
            emp.taxid       AS TaxId,
            emp.name        AS Employer,
            e.LowestCostPlanOffered AS PlanName,
            (
                /* Restricted to bands whose window overlaps the filing year, and
                   searched across the group. Previously this was MIN(amount) over
                   every Premium row for the plan with no year or band filter,
                   which on a salary-banded plan returns the cheapest band to
                   every employee. */
                SELECT MIN(prem.amount)
                FROM   dbo.EmployerPlan p
                JOIN   dbo.Premium prem ON prem.EmployerPlanId = p.id
                WHERE  p.name = e.LowestCostPlanOffered
                  AND  p.employerId IN (SELECT id FROM @Group)
                  AND  prem.bandingStartDate <= @YearEnd
                  AND  (prem.bandingEndDate IS NULL OR prem.bandingEndDate >= @YearStart)
            )               AS LowestCostPremium,
            pay.W2Wages,
            pay.HourlyRate,
            RateOfPayMonthlyMax = CASE WHEN pay.HourlyRate > 0
                                       THEN CAST(pay.HourlyRate * fy.ropHours * fy.fpgPercent AS DECIMAL(18,2))
                                  END,
            W2MonthlyMax        = CASE WHEN pay.W2Wages > 0
                                       THEN CAST((pay.W2Wages / 12.0) * fy.fpgPercent AS DECIMAL(18,2))
                                  END,
            FplMonthlyMax       = fy.fpgPremium,
            AffordabilityPct    = fy.fpgPercent
        FROM dbo.Employee e
        JOIN dbo.Employer emp
            ON e.EmployerId = emp.id
        LEFT JOIN dbo.FilingYear fy
            ON fy.filingYear = @Year
        OUTER APPLY (
            SELECT
                /* The importer writes one payroll row per employee holding the
                   annual figures, and puts the hourly RATE in
                   payPeriodTotalHours -- payPeriodHourlyAmount is left NULL.
                   sp_Generate1095Codes reads it this way; this procedure now
                   agrees with it, including counting payPeriodAdditional. */
                W2Wages    = ISNULL(MAX(pr.payPeriodSalaryAmount), 0)
                           + ISNULL(MAX(pr.payPeriodAdditional), 0),
                HourlyRate = ISNULL(MAX(pr.payPeriodTotalHours), 0)
            FROM   dbo.EmployeeCode ec
            JOIN   dbo.EmployeePayroll pr ON pr.employeeCodeId = ec.id
            WHERE  ec.employeeId = e.id
              AND  ec.filingYear = @Year
        ) pay
        WHERE e.EmployerId = @EmployerId
          AND emp.filingYear = @Year
          AND ISNULL(e.IsDeleted, 0) = 0
        ORDER BY e.lastName, e.firstName;
    END TRY
    BEGIN CATCH
        THROW;
    END CATCH
END
GO

/* =============================================================================
   PART 4 -- Repair what is already in the database.

   The procedure changes only affect the next import and the next code run. This
   fixes the enrolment rows already loaded, without re-importing them. No plan or
   premium rows are created -- that is 019A's approach, not this one.
   ============================================================================= */

PRINT '--- Point the orphaned enrolments at their plan, searching the group ---';
PRINT '    These are the rows that produced 1H with a 2C beside it.';

UPDATE  en
SET     en.planId = ep.id
FROM    dbo.EmployeeEnrollment en
JOIN    dbo.EmployeeCode ec ON ec.id = en.employeeCodeId
JOIN    dbo.Employee e      ON e.id = ec.employeeId
JOIN    dbo.Employer emp    ON emp.id = e.EmployerId
JOIN    dbo.EmployerPlan ep ON LTRIM(RTRIM(ep.name)) = LTRIM(RTRIM(e.LowestCostPlanOffered))
                           AND ep.employerId IN (emp.id, ISNULL(emp.companyId, emp.id))
WHERE   en.planId IS NULL
  AND   ISNULL(LTRIM(RTRIM(e.LowestCostPlanOffered)), '') <> '';

PRINT '    ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' enrolment row(s) re-linked.';
GO

/* -------------------------------------------------------------- VERIFY ----- */

PRINT '';
PRINT '=== 1. All three procedures are group-aware ===';
SELECT  Procedure_ = 'sp_ImportData_FromStaging',
        Result = CASE WHEN OBJECT_DEFINITION(OBJECT_ID('dbo.sp_ImportData_FromStaging')) LIKE '%p2.companyId%'
                      THEN 'PASS' ELSE 'FAIL' END
UNION ALL
SELECT  'sp_Generate1095Codes',
        CASE WHEN OBJECT_DEFINITION(OBJECT_ID('dbo.sp_Generate1095Codes')) LIKE '%SELECT id FROM @Group%'
             THEN 'PASS' ELSE 'FAIL' END
UNION ALL
SELECT  'sp_AffordabilityExport',
        CASE WHEN OBJECT_DEFINITION(OBJECT_ID('dbo.sp_AffordabilityExport')) LIKE '%SELECT id FROM @Group%'
             THEN 'PASS' ELSE 'FAIL' END;

PRINT '=== 2. The group as the procedures will resolve it ===';
SELECT  EmployerId = er.id,
        er.taxid,
        EmployerName = er.name,
        Role = CASE WHEN er.companyId IS NULL THEN 'parent' ELSE 'affiliate of ' + CAST(er.companyId AS VARCHAR(10)) END,
        OwnPlans = (SELECT COUNT(*) FROM dbo.EmployerPlan p WHERE p.employerId = er.id),
        PlansVisibleToItsEmployees =
            (SELECT COUNT(*) FROM dbo.EmployerPlan p
             WHERE p.employerId IN (er.id, ISNULL(er.companyId, er.id))),
        Employees = (SELECT COUNT(*) FROM dbo.Employee em WHERE em.EmployerId = er.id AND ISNULL(em.IsDeleted,0) = 0)
FROM    dbo.Employer er
WHERE   ISNULL(er.IsDeleted, 0) = 0
  AND  (er.companyId IS NOT NULL
        OR EXISTS (SELECT 1 FROM dbo.Employer c WHERE c.companyId = er.id))
ORDER BY ISNULL(er.companyId, er.id), er.companyId;

PRINT '    OwnPlans should still be 0 for affiliates -- that is the point of this';
PRINT '    option. PlansVisibleToItsEmployees is what now matters, and it should';
PRINT '    match the parent''s count.';

PRINT '=== 3. Enrolments still missing a plan ===';
PRINT '    Any row here will still produce 1H. Empty is what you want.';
SELECT  Employee = e.firstName + ' ' + e.lastName,
        EmployerTaxId = er.taxid,
        e.LowestCostPlanOffered,
        en.isEnrolled, en.coverageStartDate
FROM    dbo.EmployeeEnrollment en
JOIN    dbo.EmployeeCode ec ON ec.id = en.employeeCodeId
JOIN    dbo.Employee e      ON e.id = ec.employeeId
JOIN    dbo.Employer er     ON er.id = e.EmployerId
WHERE   en.planId IS NULL
  AND   ISNULL(LTRIM(RTRIM(e.LowestCostPlanOffered)), '') <> ''
ORDER BY e.lastName;

PRINT '';
PRINT '=== NEXT STEP ===';
PRINT '    Regenerate the codes for BOTH employers, then re-export.';
PRINT '    CARMEN DELACROIX should change from  1H - 2C  to  1E $165.00 2C.';
PRINT '    VICTOR NAKASHIMA should stay  1H - 2E  (union, no plan offered).';
GO
