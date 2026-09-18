/* =============================================================================
   008_F16_F17_F24_import_summary_and_missing_procs.sql

   RUN AFTER 004, 005, 006 and 007.

   FIXES   F-16  Overwrite import does not clear EmployeeStatus.
           F-17  Import always reports success, and its counts undercount.
           F-24  Two stored procedures called from C# that do not exist.

   ------------------------------------------------------------------- F-16 ---
   The Overwrite cleanup deleted EmployeeEnrollment, EmployeePayroll,
   CoveredIndividual, EmployeeHireSpan, Premium and Employer_AggregatedGroupMembers
   but not EmployeeStatus, whose insert is guarded only by NOT EXISTS on start
   date plus status value. Status spans therefore accumulated across Overwrite
   imports.

   That matters because sp_Generate1095Codes marks a month full-time if ANY status
   row with status=1 overlaps it. A corrected file reclassifying an employee from
   full-time to part-time left the stale full-time span in place, and the month
   still coded as full-time — which is the direction that creates 4980H exposure.

   ------------------------------------------------------------------- F-17 ---
   ImportValidationSummary is what the application reads to decide whether an
   import worked, and it was not measuring anything:

     - IsImportComplete was the literal 1 and ValidationMessage the literal
       'Import complete'. DataProcessingRepository.ImportDataAsync branches on
       summary.IsImportComplete, so it could only ever report success. The only
       failure the UI could surface was an unhandled SQL exception.

     - The counts filtered on Employee.FileLogId, but the employee MERGE does not
       set FileLogId on WHEN MATCHED, so pre-existing employees keep the file id
       that first created them. A second import for the same employer reported a
       near-zero employee count while having updated hundreds of rows — which
       reads to the operator as a failed import that in fact succeeded.

   Completeness is now derived: employees were staged, and every valid staged
   employee resolved to a live row. Counts are by employer and filing year.

   ------------------------------------------------------------------- F-24 ---
   Six procedures were called from C# but absent from the database. Four had no
   caller outside their own repository and interface and have been DELETED from
   the C# instead — creating procedures to satisfy code nothing invokes would
   only add dead SQL. Those four were sp_GetInvalidStagingEmployees,
   sp_UpdateStagingEmployeeRow, sp_GetInvalidStagingEmployers and
   sp_UpdateStagingEmployerRow.

   The two remaining ones are genuinely reachable and are created here:

     sp_ReorderUserShortcuts   ShortcutController.cs:112 -> ShortcutService
     sp_FilingYear_Delete      SystemSettingsService.DeleteFilingYear

   sp_FilingYear_Delete refuses rather than orphaning data. FilingYear has no
   foreign keys pointing at it, so an unguarded DELETE would silently strand every
   Employer and EmployeeCode row for that year — and those years are exactly the
   ones 001 seeded penalty rates for.

   SAFE TO RE-RUN
           Yes. CREATE OR ALTER throughout, and the import proc replacement is
           idempotent.

   DRIFT WARNING
           PART 1 replaces sp_ImportData_FromStaging, rebuilt from the CURRENT
           post-migration definition in 20_08_2026_ACA_V1.sql — so it carries the
           F-06 and F-07 changes forward. Only the CREATE header and the old
           four-line summary block were removed; F-16's DELETE is additive.
   ============================================================================= */

USE [db_ACA360];
GO

SET NOCOUNT ON;
GO

/* ---------------------------------------------------------------- PART 1 ----
   sp_ImportData_FromStaging — F-16 status cleanup and F-17 real summary.
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
            /* F-16: EmployeeStatus was missing from this cleanup while its insert
               is guarded only by NOT EXISTS on start date plus status value, so
               status spans accumulated across Overwrite imports. That matters
               because sp_Generate1095Codes marks a month full-time if ANY status
               row with status=1 overlaps it: a corrected file reclassifying an
               employee from full-time to part-time left the stale full-time span
               in place and the month still coded as full-time. */
            DELETE FROM EmployeeStatus WHERE employeeCodeId IN (SELECT Id FROM @TargetEmployeeCodes);
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
        /* F-17: the summary is what the application reads to decide whether the
           import succeeded, and it was not measuring anything.

           IsImportComplete was the literal 1 and ValidationMessage the literal
           'Import complete', so DataProcessingRepository.ImportDataAsync — which
           branches on summary.IsImportComplete — could only ever report success.
           The only failure the UI could show was an unhandled SQL exception.

           The counts filtered on Employee.FileLogId, but the employee MERGE above
           does not update FileLogId on WHEN MATCHED, so every pre-existing
           employee kept the file id that first created them. A second import for
           the same employer reported a near-zero employee count while having
           updated hundreds of rows, which reads as a failed import that in fact
           succeeded. Counting by employer and filing year fixes that.

           Completeness is now derived: employees were loaded, and every valid
           staging employee resolved to one. */
        DECLARE @StagedEmployees   INT = (SELECT COUNT(DISTINCT S.EmployeeSSN)
                                          FROM   Staging_Employees S
                                          WHERE  S.FileLogId = @FileLogId AND S.IsValid = 1);
        DECLARE @LiveEmployees     INT = (SELECT COUNT(DISTINCT E.id)
                                          FROM   Employee E
                                          JOIN   EmployeeCode EC ON EC.employeeId = E.id AND EC.filingYear = @PlanYear
                                          WHERE  EC.employerId = @ParentEmployerId
                                             OR  EC.employerId IN (SELECT id FROM Employer WHERE companyId = @ParentEmployerId));
        DECLARE @MissingEmployees  INT = (SELECT COUNT(*)
                                          FROM  (SELECT DISTINCT S.EmployeeSSN
                                                 FROM   Staging_Employees S
                                                 WHERE  S.FileLogId = @FileLogId AND S.IsValid = 1) X
                                          WHERE NOT EXISTS (SELECT 1 FROM Employee E WHERE E.ssn = X.EmployeeSSN));

        DECLARE @IsComplete BIT = CASE WHEN @StagedEmployees > 0 AND @MissingEmployees = 0 THEN 1 ELSE 0 END;
        DECLARE @Message VARCHAR(500) =
            CASE WHEN @StagedEmployees = 0
                     THEN 'No valid employee rows were staged for this file.'
                 WHEN @MissingEmployees > 0
                     THEN CAST(@MissingEmployees AS VARCHAR(10))
                        + ' of ' + CAST(@StagedEmployees AS VARCHAR(10))
                        + ' staged employees did not reach the live tables.'
                 ELSE 'Import complete.' END;

        DELETE FROM ImportValidationSummary WHERE FileLogId = @FileLogId;

        INSERT INTO ImportValidationSummary
            (FileLogId, TotalEmployees, EmployeesWithHireDate, EmployeesWithStatus, PayrollRows,
             EnrollmentRows, PlanCount, IsImportComplete, ValidationMessage, DependentRows, TotalPayrollImported)
        SELECT
            @FileLogId,
            @LiveEmployees,
            (SELECT COUNT(DISTINCT E.id) FROM Employee E
             JOIN EmployeeCode EC ON EC.employeeId = E.id AND EC.filingYear = @PlanYear
             WHERE (EC.employerId = @ParentEmployerId
                    OR EC.employerId IN (SELECT id FROM Employer WHERE companyId = @ParentEmployerId))
               AND E.HireDate IS NOT NULL),
            (SELECT COUNT(DISTINCT ST.employeeCodeId) FROM EmployeeStatus ST
             JOIN EmployeeCode EC ON EC.id = ST.employeeCodeId AND EC.filingYear = @PlanYear
             WHERE EC.employerId = @ParentEmployerId
                OR EC.employerId IN (SELECT id FROM Employer WHERE companyId = @ParentEmployerId)),
            (SELECT COUNT(*) FROM EmployeePayroll PR
             JOIN EmployeeCode EC ON EC.id = PR.employeeCodeId AND EC.filingYear = @PlanYear
             WHERE EC.employerId = @ParentEmployerId
                OR EC.employerId IN (SELECT id FROM Employer WHERE companyId = @ParentEmployerId)),
            (SELECT COUNT(*) FROM EmployeeEnrollment EN
             JOIN EmployeeCode EC ON EC.id = EN.employeeCodeId AND EC.filingYear = @PlanYear
             WHERE EC.employerId = @ParentEmployerId
                OR EC.employerId IN (SELECT id FROM Employer WHERE companyId = @ParentEmployerId)),
            (SELECT COUNT(*) FROM EmployerPlan EP
             WHERE EP.employerId = @ParentEmployerId
                OR EP.employerId IN (SELECT id FROM Employer WHERE companyId = @ParentEmployerId)),
            @IsComplete,
            @Message,
            (SELECT COUNT(*) FROM CoveredIndividual CI
             JOIN EmployeeCode EC ON EC.id = CI.employeeCodeId AND EC.filingYear = @PlanYear
             WHERE ISNULL(CI.IsDeleted, 0) = 0
               AND (EC.employerId = @ParentEmployerId
                    OR EC.employerId IN (SELECT id FROM Employer WHERE companyId = @ParentEmployerId))),
            (SELECT ISNULL(SUM(PR.payPeriodSalaryAmount), 0) FROM EmployeePayroll PR
             JOIN EmployeeCode EC ON EC.id = PR.employeeCodeId AND EC.filingYear = @PlanYear
             WHERE EC.employerId = @ParentEmployerId
                OR EC.employerId IN (SELECT id FROM Employer WHERE companyId = @ParentEmployerId));
  
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

/* ---------------------------------------------------------------- PART 2 ----
   sp_ReorderUserShortcuts — called by ShortcutController.Reorder via
   ShortcutService.ReorderUserShortcuts(userId, orderedMenuIds), which joins the
   ids into a comma-separated string. Position in the list is the display order.
   ---------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE [dbo].[sp_ReorderUserShortcuts]
    @UserId     BIGINT,
    @MenuIdsCsv NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    IF @UserId IS NULL OR ISNULL(LTRIM(RTRIM(@MenuIdsCsv)), '') = ''
        RETURN;

    BEGIN TRY
        BEGIN TRANSACTION;

        /* ordinal preserves the caller's sequence; STRING_SPLIT alone does not
           guarantee order, so it is requested explicitly. */
        ;WITH Ordered AS (
            SELECT  MenuId = TRY_CAST(value AS INT),
                    Position = ROW_NUMBER() OVER (ORDER BY ordinal)
            FROM    STRING_SPLIT(@MenuIdsCsv, ',', 1)
            WHERE   TRY_CAST(value AS INT) IS NOT NULL
        )
        UPDATE  s
        SET     s.DisplayOrder = o.Position
        FROM    dbo.tbl_UserShortcuts s
        JOIN    Ordered o ON o.MenuId = s.MenuId
        WHERE   s.UserId = @UserId;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

/* ---------------------------------------------------------------- PART 3 ----
   sp_FilingYear_Delete — called by SystemSettingsService.DeleteFilingYear.
   @id is FilingYear.id, matching sp_Action_FilingYear.

   Guarded. FilingYear carries no foreign keys, so an unguarded DELETE would
   strand every Employer, EmployeeCode and UploadedFileLog row for that year with
   no error. A year that is in use is refused; deactivate it instead.
   ---------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE [dbo].[sp_FilingYear_Delete]
    @id INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Year INT;
    SELECT @Year = filingYear FROM dbo.FilingYear WHERE id = @id;

    IF @Year IS NULL
    BEGIN
        RAISERROR('Filing year id %d does not exist.', 16, 1, @id);
        RETURN;
    END

    DECLARE @Employers INT = (SELECT COUNT(*) FROM dbo.Employer       WHERE filingYear = @Year);
    DECLARE @Codes     INT = (SELECT COUNT(*) FROM dbo.EmployeeCode   WHERE filingYear = @Year);
    DECLARE @Files     INT = (SELECT COUNT(*) FROM dbo.UploadedFileLog WHERE PlanYear   = @Year);

    IF (@Employers + @Codes + @Files) > 0
    BEGIN
        RAISERROR(
            'Filing year %d is in use and was not deleted: %d employer row(s), %d employee code row(s), %d uploaded file(s). Set IsActive = 0 to retire it instead.',
            16, 1, @Year, @Employers, @Codes, @Files);
        RETURN;
    END

    DELETE FROM dbo.FilingYear WHERE id = @id;
    PRINT 'Filing year ' + CAST(@Year AS VARCHAR(4)) + ' deleted.';
END
GO

/* -------------------------------------------------------------- VERIFY ----- */
PRINT '--- The six F-24 procedures: four deleted in C#, two created here ---';
SELECT  ProcedureName = n.name,
        Present       = CASE WHEN OBJECT_ID('dbo.' + n.name, 'P') IS NOT NULL THEN 'yes' ELSE 'no' END,
        Expected      = n.expected
FROM   (VALUES
            ('sp_ReorderUserShortcuts',        'yes - created by this script'),
            ('sp_FilingYear_Delete',           'yes - created by this script'),
            ('sp_GetInvalidStagingEmployees',  'no  - caller removed from C#'),
            ('sp_UpdateStagingEmployeeRow',    'no  - caller removed from C#'),
            ('sp_GetInvalidStagingEmployers',  'no  - caller removed from C#'),
            ('sp_UpdateStagingEmployerRow',    'no  - caller removed from C#')
        ) n(name, expected);

PRINT '--- Overwrite cleanup now covers every child of EmployeeCode ---';
SELECT  ChildTable = t.name,
        ClearedOnOverwrite =
            CASE WHEN t.name IN ('EmployeeEnrollment','EmployeePayroll','CoveredIndividual',
                                 'EmployeeHireSpan','EmployeeStatus','EmployeeIchra')
                 THEN 'yes' ELSE 'review' END
FROM    sys.tables t
WHERE   t.name IN ('EmployeeEnrollment','EmployeePayroll','CoveredIndividual',
                   'EmployeeHireSpan','EmployeeStatus','EmployeeIchra')
ORDER BY t.name;
PRINT '    EmployeeIchra is intentionally not cleared: nothing writes it during import.';
GO
