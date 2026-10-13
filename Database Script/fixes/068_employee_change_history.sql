/* =============================================================================
   068 - Employee change history (idea #19)

   Hand edits were already audited (DataAuditlog) and code changes too
   (EmployeeCodeAuditTrail); what an import changed on an employee was not
   kept anywhere.

   1. EmployeeChangeHistory: one row per employee the import added or
      restored, and per field it changed (old -> new), with the file.
   2. sp_ImportData_FromStaging: the employee MERGE now writes those rows.
      Everything else is the database's current procedure (script 048),
      unchanged.
   3. sp_EmployeeChangeHistory: the employee's history from all three
      sources, newest first. SSNs are shown by their last four digits.

   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

IF OBJECT_ID('dbo.EmployeeChangeHistory') IS NULL
BEGIN
    CREATE TABLE dbo.EmployeeChangeHistory (
        ChangeId   BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmployeeChangeHistory PRIMARY KEY,
        EmployeeId BIGINT        NOT NULL,
        ChangedAt  DATETIME2(0)  NOT NULL CONSTRAINT DF_EmployeeChangeHistory_ChangedAt DEFAULT SYSDATETIME(),
        Source     VARCHAR(20)   NOT NULL,   -- Import
        FileLogId  INT           NULL,
        ChangeKind VARCHAR(20)   NOT NULL,   -- Added, Restored, Changed
        FieldName  NVARCHAR(64)  NULL,
        OldValue   NVARCHAR(400) NULL,
        NewValue   NVARCHAR(400) NULL
    );
    CREATE INDEX IX_EmployeeChangeHistory_Employee ON dbo.EmployeeChangeHistory (EmployeeId, ChangedAt DESC);
    CREATE INDEX IX_EmployeeChangeHistory_File ON dbo.EmployeeChangeHistory (FileLogId);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.DataAuditlog') AND name = 'IX_DataAuditlog_Record')
    CREATE INDEX IX_DataAuditlog_Record ON dbo.DataAuditlog (Record_Id, Table_Name) INCLUDE (Created_at);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.EmployeeCodeAuditTrail') AND name = 'IX_EmployeeCodeAuditTrail_Employee')
    CREATE INDEX IX_EmployeeCodeAuditTrail_Employee ON dbo.EmployeeCodeAuditTrail (EmployeeId, ChangedOn DESC);
GO

-- 1. Import: rows without an Affiliated EIN ------------------------------------
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
                  AND NULLIF(LTRIM(RTRIM(AffiliatedEIN)), '') IS NOT NULL   -- 048: a row without an affiliate EIN is not an affiliate
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
          AND NULLIF(LTRIM(RTRIM(S.AffiliatedEIN)), '') IS NOT NULL   -- 048: MemberEIN is NOT NULL; a blank one stopped the import
          AND NOT EXISTS (SELECT 1 FROM Employer_AggregatedGroupMembers AG WHERE AG.MemberEIN = S.AffiliatedEIN AND AG.EmployerId = @ParentEmployerId);      
  
        /* 068: what this import changes on each employee, kept as the employee's history */
        IF OBJECT_ID('tempdb..#EmpImportChanges') IS NOT NULL DROP TABLE #EmpImportChanges;
        CREATE TABLE #EmpImportChanges (
            MergeAction NVARCHAR(10) NOT NULL, EmployeeId BIGINT NOT NULL, WasDeleted BIT NULL,
            Old0 NVARCHAR(400) NULL, New0 NVARCHAR(400) NULL,
            Old1 NVARCHAR(400) NULL, New1 NVARCHAR(400) NULL,
            Old2 NVARCHAR(400) NULL, New2 NVARCHAR(400) NULL,
            Old3 NVARCHAR(400) NULL, New3 NVARCHAR(400) NULL,
            Old4 NVARCHAR(400) NULL, New4 NVARCHAR(400) NULL,
            Old5 NVARCHAR(400) NULL, New5 NVARCHAR(400) NULL,
            Old6 NVARCHAR(400) NULL, New6 NVARCHAR(400) NULL,
            Old7 NVARCHAR(400) NULL, New7 NVARCHAR(400) NULL,
            Old8 NVARCHAR(400) NULL, New8 NVARCHAR(400) NULL,
            Old9 NVARCHAR(400) NULL, New9 NVARCHAR(400) NULL,
            Old10 NVARCHAR(400) NULL, New10 NVARCHAR(400) NULL,
            Old11 NVARCHAR(400) NULL, New11 NVARCHAR(400) NULL,
            Old12 NVARCHAR(400) NULL, New12 NVARCHAR(400) NULL,
            Old13 NVARCHAR(400) NULL, New13 NVARCHAR(400) NULL,
            Old14 NVARCHAR(400) NULL, New14 NVARCHAR(400) NULL,
            Old15 NVARCHAR(400) NULL, New15 NVARCHAR(400) NULL,
            Old16 NVARCHAR(400) NULL, New16 NVARCHAR(400) NULL,
            Old17 NVARCHAR(400) NULL, New17 NVARCHAR(400) NULL,
            Old18 NVARCHAR(400) NULL, New18 NVARCHAR(400) NULL
        );

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
            )
        OUTPUT $action, inserted.id, deleted.IsDeleted,
               CAST(deleted.firstName AS NVARCHAR(400)), CAST(inserted.firstName AS NVARCHAR(400)),
               CAST(deleted.middleName AS NVARCHAR(400)), CAST(inserted.middleName AS NVARCHAR(400)),
               CAST(deleted.lastName AS NVARCHAR(400)), CAST(inserted.lastName AS NVARCHAR(400)),
               CAST(deleted.suffix AS NVARCHAR(400)), CAST(inserted.suffix AS NVARCHAR(400)),
               CAST(deleted.address AS NVARCHAR(400)), CAST(inserted.address AS NVARCHAR(400)),
               CAST(deleted.address2 AS NVARCHAR(400)), CAST(inserted.address2 AS NVARCHAR(400)),
               CAST(deleted.city AS NVARCHAR(400)), CAST(inserted.city AS NVARCHAR(400)),
               CAST(deleted.state AS NVARCHAR(400)), CAST(inserted.state AS NVARCHAR(400)),
               CAST(deleted.zip AS NVARCHAR(400)), CAST(inserted.zip AS NVARCHAR(400)),
               CONVERT(NVARCHAR(10), deleted.birthday, 23), CONVERT(NVARCHAR(10), inserted.birthday, 23),
               CONVERT(NVARCHAR(10), deleted.HireDate, 23), CONVERT(NVARCHAR(10), inserted.HireDate, 23),
               CONVERT(NVARCHAR(10), deleted.TerminationDate, 23), CONVERT(NVARCHAR(10), inserted.TerminationDate, 23),
               CAST(deleted.Status AS NVARCHAR(20)), CAST(inserted.Status AS NVARCHAR(20)),
               CAST(deleted.CoverageElected AS NVARCHAR(20)), CAST(inserted.CoverageElected AS NVARCHAR(20)),
               CONVERT(NVARCHAR(10), deleted.CoverageStartDate, 23), CONVERT(NVARCHAR(10), inserted.CoverageStartDate, 23),
               CONVERT(NVARCHAR(10), deleted.CoverageEndDate, 23), CONVERT(NVARCHAR(10), inserted.CoverageEndDate, 23),
               CAST(deleted.LowestCostPlanOffered AS NVARCHAR(400)), CAST(inserted.LowestCostPlanOffered AS NVARCHAR(400)),
               CONVERT(NVARCHAR(10), deleted.DateEligibleForCoverage, 23), CONVERT(NVARCHAR(10), inserted.DateEligibleForCoverage, 23),
               CAST(deleted.email AS NVARCHAR(400)), CAST(inserted.email AS NVARCHAR(400))
        INTO #EmpImportChanges (MergeAction, EmployeeId, WasDeleted, Old0, New0, Old1, New1, Old2, New2, Old3, New3, Old4, New4, Old5, New5, Old6, New6, Old7, New7, Old8, New8, Old9, New9, Old10, New10, Old11, New11, Old12, New12, Old13, New13, Old14, New14, Old15, New15, Old16, New16, Old17, New17, Old18, New18);

        -- 068: the employee's history: added, restored, and each field the file changed
        INSERT INTO dbo.EmployeeChangeHistory (EmployeeId, Source, FileLogId, ChangeKind, FieldName, OldValue, NewValue)
        SELECT C.EmployeeId, 'Import', @FileLogId, 'Added', NULL, NULL, NULL
        FROM   #EmpImportChanges C WHERE C.MergeAction = 'INSERT'
        UNION ALL
        SELECT C.EmployeeId, 'Import', @FileLogId, 'Restored', NULL, NULL, NULL
        FROM   #EmpImportChanges C WHERE C.MergeAction = 'UPDATE' AND C.WasDeleted = 1
        UNION ALL
        SELECT C.EmployeeId, 'Import', @FileLogId, 'Changed', V.FieldName, V.OldValue, V.NewValue
        FROM   #EmpImportChanges C
        CROSS  APPLY (VALUES
                   (N'First name', C.Old0, C.New0),
                   (N'Middle initial', C.Old1, C.New1),
                   (N'Last name', C.Old2, C.New2),
                   (N'Suffix', C.Old3, C.New3),
                   (N'Address', C.Old4, C.New4),
                   (N'Address 2', C.Old5, C.New5),
                   (N'City', C.Old6, C.New6),
                   (N'State', C.Old7, C.New7),
                   (N'ZIP', C.Old8, C.New8),
                   (N'Birth date', C.Old9, C.New9),
                   (N'Hire date', C.Old10, C.New10),
                   (N'Termination date', C.Old11, C.New11),
                   (N'Status', C.Old12, C.New12),
                   (N'Coverage elected', C.Old13, C.New13),
                   (N'Coverage start', C.Old14, C.New14),
                   (N'Coverage end', C.Old15, C.New15),
                   (N'Lowest cost plan', C.Old16, C.New16),
                   (N'Eligible for coverage', C.Old17, C.New17),
                   (N'E-mail', C.Old18, C.New18)
               ) V (FieldName, OldValue, NewValue)
        WHERE  C.MergeAction = 'UPDATE'
          AND  ISNULL(LTRIM(RTRIM(V.OldValue)), N'') <> ISNULL(LTRIM(RTRIM(V.NewValue)), N'');  

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
            UPDATE SET FileLogId = @FileLogId,    
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

CREATE OR ALTER PROCEDURE dbo.sp_EmployeeChangeHistory
    @EmployeeId BIGINT,
    @Top        INT = 500
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Rec VARCHAR(50) = CAST(@EmployeeId AS VARCHAR(50));

    ;WITH Imports AS (
        SELECT H.ChangedAt, 'Import' AS Source,
               CASE H.ChangeKind WHEN 'Added' THEN N'Added by an import' WHEN 'Restored' THEN N'Restored by an import'
                                 ELSE N'Changed by an import' END AS Summary,
               COALESCE(NULLIF(U1.User_Name, ''), NULLIF(U2.User_Name, '')) AS ChangedBy,
               H.FileLogId, F.OriginalFileName AS FileName,
               H.FieldName, H.OldValue, H.NewValue,
               'I' + CAST(H.FileLogId AS VARCHAR(20)) + '-' + CONVERT(VARCHAR(19), H.ChangedAt, 126) AS GroupKey
        FROM   dbo.EmployeeChangeHistory H
        LEFT   JOIN dbo.UploadedFileLog F ON F.FileLogId = H.FileLogId
        LEFT   JOIN dbo.tbl_User U1 ON CAST(U1.User_ID AS VARCHAR(20)) = F.AssignedToUserId
        LEFT   JOIN dbo.tbl_User U2 ON CAST(U2.User_ID AS VARCHAR(20)) = F.UploadedByUserId
        WHERE  H.EmployeeId = @EmployeeId
    ), Edits AS (
        -- Hand edits: field deltas for single saves, one line for a grid save.
        SELECT A.Created_at AS ChangedAt, 'Edit' AS Source,
               CASE WHEN A.Action_Type = 'INSERT' THEN N'Added by hand'
                    WHEN A.Action_Type = 'BULK_UPSERT' THEN N'Saved ' + REPLACE(REPLACE(A.Table_Name, 'Employee', ''), 'Details', '') + N' details'
                    ELSE N'Edited by hand' END AS Summary,
               A.User_Name AS ChangedBy, CAST(NULL AS INT) AS FileLogId, CAST(NULL AS NVARCHAR(260)) AS FileName,
               CAST(N.[key] AS NVARCHAR(64)) COLLATE DATABASE_DEFAULT AS FieldName,
               CASE WHEN N.[key] LIKE '%SSN%' AND LEN(O.value) >= 4 THEN N'XXX-XX-' + RIGHT(O.value, 4) ELSE CAST(O.value AS NVARCHAR(400)) END COLLATE DATABASE_DEFAULT AS OldValue,
               CASE WHEN N.[key] LIKE '%SSN%' AND LEN(N.value) >= 4 THEN N'XXX-XX-' + RIGHT(N.value, 4) ELSE CAST(N.value AS NVARCHAR(400)) END COLLATE DATABASE_DEFAULT AS NewValue,
               'E' + CAST(A.Id AS VARCHAR(40)) AS GroupKey
        FROM   dbo.DataAuditlog A
        OUTER  APPLY (SELECT [key], value FROM OPENJSON(CASE WHEN A.Action_Type = 'UPDATE' AND ISJSON(A.New_Values) = 1
                                                             AND LEFT(LTRIM(A.New_Values), 1) = '{' THEN A.New_Values END)) N
        OUTER  APPLY (SELECT TOP 1 value FROM OPENJSON(CASE WHEN ISJSON(A.Old_Values) = 1 AND LEFT(LTRIM(A.Old_Values), 1) = '{'
                                                          THEN A.Old_Values END) X WHERE X.[key] = N.[key]) O
        WHERE  A.Record_Id = @Rec AND A.Table_Name LIKE 'Employee%'
    ), Codes AS (
        SELECT T.ChangedOn AS ChangedAt, 'Codes' AS Source,
               CASE WHEN T.FieldName = 'IsLocked' THEN CASE WHEN T.NewValue = '1' THEN N'Codes locked' ELSE N'Codes unlocked' END
                    WHEN T.IsSystemGenerated = 1 THEN N'Codes recalculated' ELSE N'Code changed by hand' END AS Summary,
               COALESCE(T.ChangedByUserName, CASE WHEN T.IsSystemGenerated = 1 THEN 'System' END) AS ChangedBy,
               CAST(NULL AS INT) AS FileLogId, CAST(NULL AS NVARCHAR(260)) AS FileName,
               CASE WHEN T.LineNumber > 0 THEN CAST(CONCAT('Line ', T.LineNumber, ' ', CASE WHEN T.AppliesToAllMonths = 1 THEN 'All months' ELSE T.MonthCode END) AS NVARCHAR(64)) END AS FieldName,
               CASE WHEN T.LineNumber > 0 THEN CAST(T.OldValue AS NVARCHAR(400)) END AS OldValue,
               CASE WHEN T.LineNumber > 0 THEN CAST(T.NewValue AS NVARCHAR(400)) END AS NewValue,
               'C' + CONVERT(VARCHAR(16), T.ChangedOn, 126) + CAST(T.IsSystemGenerated AS CHAR(1)) AS GroupKey
        FROM   dbo.EmployeeCodeAuditTrail T
        WHERE  T.EmployeeId = @EmployeeId
          AND  ISNULL(T.OldValue, '') <> ISNULL(T.NewValue, '')
          AND  NOT (TRY_CAST(T.OldValue AS DECIMAL(18,4)) IS NOT NULL AND TRY_CAST(T.OldValue AS DECIMAL(18,4)) = TRY_CAST(T.NewValue AS DECIMAL(18,4)))
    ), AllRows AS (
        SELECT * FROM Imports UNION ALL SELECT * FROM Edits UNION ALL SELECT * FROM Codes
    )
    SELECT TOP (@Top) ChangedAt, Source, Summary, ChangedBy, FileLogId, FileName, FieldName, OldValue, NewValue, GroupKey
    FROM   AllRows
    WHERE  NOT (Source = 'Edit' AND FieldName IN ('EmployeeID', 'EmployerId', 'FilingYear', 'RowId', 'EmployeeCodeId'))
    ORDER  BY ChangedAt DESC, GroupKey, FieldName;
END
GO

PRINT '068: employee change history (imports recorded; sp_EmployeeChangeHistory).';
GO
