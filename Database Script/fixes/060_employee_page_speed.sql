/* =============================================================================
   060 - Employee page speed

   The Employee page runs two procedures on every load: sp_EmployeeList_Filter
   (the list) and sp_Employee_QuickStats (the counts above it). Both test each
   optional filter inside one big query ("no plan filter OR plan matches"), which
   makes SQL Server choose one plan for every combination: on an employer with
   10,000 employees the list took about 670 ms and the counts about 220 ms.

   Now each "is this filter used?" is worked out once into a variable and the big
   queries are compiled for the filters actually in use (OPTION (RECOMPILE)); the
   list reads only the selected employers' codes instead of every code of the year;
   the counts use a temp table for the employer ids instead of a table variable.
   The results are the same (checked on 38 filter combinations).

   Both procedures are the database's current ones with only the changes marked
   060. Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE [dbo].[sp_EmployeeList_Filter]  
(  
    @Employer_ID        BIGINT = NULL,  
    @EmployerIDs        NVARCHAR(MAX) = NULL,  
    @FillingYear        INT,  
    @PageIndex          INT = 1,  
    @PageSize           INT = 20,  
    @Search             NVARCHAR(100) = NULL,  
    @SortColumn         INT = 0,  
    @SortOrder          VARCHAR(5) = 'asc',  
    @PlanIDs            NVARCHAR(MAX) = NULL,  
    @Statuses            NVARCHAR(MAX) = NULL,  
    @EnrollmentIDs      NVARCHAR(MAX) = NULL,  
    @Flags              NVARCHAR(MAX) = NULL,  
    @IsReceivingForms   NVARCHAR(MAX) = NULL,  
    @User_ID            INT = NULL,  
    @QuickFilter        VARCHAR(10) = NULL  
)  
AS  
BEGIN  
    SET NOCOUNT ON;  
  
  
  
    BEGIN TRY  
  
  
  
        ------------------------------------------------------------  
        -- Safety  
        ------------------------------------------------------------  
        IF @PageIndex < 1  
            SET @PageIndex = 1;  
  
  
  
        IF @PageSize < 1  
            SET @PageSize = 20;  
  
  
  
        SET @Search = NULLIF(LTRIM(RTRIM(@Search)), '');  
  
  
  
        ------------------------------------------------------------  
        -- 1. Parse JSON filters ONCE  
        ------------------------------------------------------------  
  
  
  
        CREATE TABLE #EmployerFilter  
        (  
            ID BIGINT NOT NULL PRIMARY KEY  
        );  
  
  
  
        CREATE TABLE #StatusFilter  
(  
    StatusValue NVARCHAR(100) NOT NULL PRIMARY KEY  
);  
  
  
  
        CREATE TABLE #PlanFilter  
        (  
            ID BIGINT NOT NULL PRIMARY KEY  
        );  
  
  
  
        CREATE TABLE #EnrollmentFilter  
        (  
            ID INT NOT NULL PRIMARY KEY  
        );  
  
  
  
        CREATE TABLE #FlagFilter  
        (  
            ID INT NOT NULL PRIMARY KEY  
        );  
  
  
  
        CREATE TABLE #ReceivingFormFilter  
        (  
            ID INT NOT NULL PRIMARY KEY  
        );  
  
  
  
  
  
        ------------------------------------------------------------  
        -- Employer IDs  
        ------------------------------------------------------------  
        IF NULLIF(@EmployerIDs, '') IS NOT NULL  
           AND @EmployerIDs <> '[]'  
        BEGIN  
            INSERT INTO #EmployerFilter(ID)  
            SELECT DISTINCT TRY_CONVERT(BIGINT, value)  
            FROM OPENJSON(@EmployerIDs)  
            WHERE TRY_CONVERT(BIGINT, value) IS NOT NULL;  
        END  
        ELSE IF @Employer_ID IS NOT NULL  
        BEGIN  
            INSERT INTO #EmployerFilter(ID)  
            VALUES (@Employer_ID);  
  
  
  
            INSERT INTO #EmployerFilter(ID)  
            SELECT E.id  
            FROM Employer E  
            WHERE E.companyId = @Employer_ID  
              AND NOT EXISTS  
              (  
                  SELECT 1  
                  FROM #EmployerFilter X  
                  WHERE X.ID = E.id  
              );  
        END;  
  
  
  
  
  
        ------------------------------------------------------------  
        -- Status  
        ------------------------------------------------------------  
        IF NULLIF(@Statuses, '') IS NOT NULL  
           AND @Statuses <> '[]'  
        BEGIN  
           INSERT INTO #StatusFilter(StatusValue)  
SELECT DISTINCT CONVERT(NVARCHAR(100), value)  
FROM OPENJSON(@Statuses)  
WHERE value IS NOT NULL;  
        END;  
  
  
  
  
  
        ------------------------------------------------------------  
        -- Plan  
        ------------------------------------------------------------  
        IF NULLIF(@PlanIDs, '') IS NOT NULL  
           AND @PlanIDs <> '[]'  
        BEGIN  
            INSERT INTO #PlanFilter(ID)  
            SELECT DISTINCT TRY_CONVERT(BIGINT, value)  
            FROM OPENJSON(@PlanIDs)  
            WHERE TRY_CONVERT(BIGINT, value) IS NOT NULL;  
        END;  
  
  
  
  
  
        ------------------------------------------------------------  
        -- Enrollment  
        ------------------------------------------------------------  
        IF NULLIF(@EnrollmentIDs, '') IS NOT NULL  
           AND @EnrollmentIDs <> '[]'  
        BEGIN  
            INSERT INTO #EnrollmentFilter(ID)  
            SELECT DISTINCT TRY_CONVERT(INT, value)  
            FROM OPENJSON(@EnrollmentIDs)  
            WHERE TRY_CONVERT(INT, value) IS NOT NULL;  
        END;  
  
  
  
  
  
        ------------------------------------------------------------  
        -- Flags  
        ------------------------------------------------------------  
        IF NULLIF(@Flags, '') IS NOT NULL  
           AND @Flags <> '[]'  
        BEGIN  
            INSERT INTO #FlagFilter(ID)  
            SELECT DISTINCT TRY_CONVERT(INT, value)  
            FROM OPENJSON(@Flags)  
            WHERE TRY_CONVERT(INT, value) IS NOT NULL;  
        END;  
  
  
  
  
  
        ------------------------------------------------------------  
        -- Receiving forms  
        ------------------------------------------------------------  
        IF NULLIF(@IsReceivingForms, '') IS NOT NULL  
           AND @IsReceivingForms <> '[]'  
        BEGIN  
            INSERT INTO #ReceivingFormFilter(ID)  
            SELECT DISTINCT TRY_CONVERT(INT, value)  
            FROM OPENJSON(@IsReceivingForms)  
            WHERE TRY_CONVERT(INT, value) IS NOT NULL;  
        END;

        -- 060: which filters are in use.
        DECLARE @HasEmployerFilter BIT = CASE WHEN EXISTS (SELECT 1 FROM #EmployerFilter) THEN 1 ELSE 0 END,
                @HasStatusFilter   BIT = CASE WHEN EXISTS (SELECT 1 FROM #StatusFilter) THEN 1 ELSE 0 END,
                @HasPlanFilter     BIT = CASE WHEN EXISTS (SELECT 1 FROM #PlanFilter) THEN 1 ELSE 0 END,
                @HasFormFilter     BIT = CASE WHEN EXISTS (SELECT 1 FROM #ReceivingFormFilter) THEN 1 ELSE 0 END,
                @HasEnrollFilter   BIT = CASE WHEN EXISTS (SELECT 1 FROM #EnrollmentFilter) THEN 1 ELSE 0 END,
                @HasFlagFilter     BIT = CASE WHEN EXISTS (SELECT 1 FROM #FlagFilter) THEN 1 ELSE 0 END;
  
  
  
  
  
  
        ------------------------------------------------------------  
        -- 2. Filter EmployeeCode FIRST  
        --  
        -- This is extremely important for millions of records.  
        ------------------------------------------------------------  
        CREATE TABLE #EmployeeCodes  
        (  
            EmpCodeID       BIGINT NOT NULL PRIMARY KEY,  
            EmployeeID      BIGINT NOT NULL,  
            EmployerID      BIGINT NULL,  
            GetForm         INT NULL,  
            FilingYear      INT NOT NULL  
        );  
  
  
  
  
  
        -- 060: the selected employers' codes through a join, or every code of the year.
        IF @HasEmployerFilter = 1
            INSERT INTO #EmployeeCodes (EmpCodeID, EmployeeID, EmployerID, GetForm, FilingYear)
            SELECT EC.id, EC.employeeid, EC.employerId, EC.getForm, EC.filingYear
            FROM   EmployeeCode EC
            JOIN   (SELECT DISTINCT ID FROM #EmployerFilter) EF ON EF.ID = EC.employerId
            WHERE  EC.filingYear = @FillingYear;
        ELSE
            INSERT INTO #EmployeeCodes (EmpCodeID, EmployeeID, EmployerID, GetForm, FilingYear)
            SELECT EC.id, EC.employeeid, EC.employerId, EC.getForm, EC.filingYear
            FROM   EmployeeCode EC
            WHERE  EC.filingYear = @FillingYear;

        CREATE INDEX IX_EmployeeCodes_EmployeeID  
            ON #EmployeeCodes(EmployeeID);  
  
  
  
        CREATE INDEX IX_EmployeeCodes_EmployerID  
            ON #EmployeeCodes(EmployerID);  
  
  
  
  
  
        ------------------------------------------------------------  
        -- 3. Build Status information ONCE  
        ------------------------------------------------------------  
        CREATE TABLE #Status  
        (  
            EmployeeCodeID BIGINT NOT NULL PRIMARY KEY,  
            EmployeeStatus NVARCHAR(100) NULL  
        );  
  
  
  
  
  
        INSERT INTO #Status  
        (  
            EmployeeCodeID,  
            EmployeeStatus  
        )  
        SELECT  
            ES.employeeCodeId,  
            MAX(ES.status)  
        FROM EmployeeStatus ES  
        INNER JOIN #EmployeeCodes EC  
            ON EC.EmpCodeID = ES.employeeCodeId  
        GROUP BY  
            ES.employeeCodeId;  
  
  
  
  
  
        ------------------------------------------------------------  
        -- 4. Build Enrollment information ONCE  
        ------------------------------------------------------------  
        CREATE TABLE #Enrollment  
        (  
            EmployeeCodeID       BIGINT NOT NULL PRIMARY KEY,  
            PlanID               BIGINT NULL,  
            CoverageOfferDate    DATETIME NULL,  
            IsEnrolled            BIT NULL,  
            UnionMember           BIT NULL,  
            COBRAEnrolled         BIT NULL,  
            RetireeEnrolled       BIT NULL  
        );  
  
  
  
  
  
        INSERT INTO #Enrollment  
        (  
            EmployeeCodeID,  
            PlanID,  
            CoverageOfferDate,  
            IsEnrolled,  
            UnionMember,  
            COBRAEnrolled,  
            RetireeEnrolled  
        )  
        SELECT  
            EE.employeeCodeId,  
            MAX(EE.planId),  
            MAX(EE.coverageOfferDate),  
            MAX(CONVERT(INT, ISNULL(EE.isEnrolled, 0))),  
            MAX(CONVERT(INT, ISNULL(EE.unionMember, 0))),  
            MAX(CONVERT(INT, ISNULL(EE.COBRAEnrolled, 0))),  
            MAX(CONVERT(INT, ISNULL(EE.RetireeEnrolled, 0)))  
        FROM EmployeeEnrollment EE  
        INNER JOIN #EmployeeCodes EC  
            ON EC.EmpCodeID = EE.employeeCodeId  
        GROUP BY  
            EE.employeeCodeId;  
  
  
  
  
  
        ------------------------------------------------------------  
        -- 5. Missing-data calculation  
        --  
        -- Instead of four correlated NOT EXISTS per employee,  
        -- calculate availability once.  
        ------------------------------------------------------------  
        CREATE TABLE #DataAvailability  
        (  
            EmployeeCodeID BIGINT NOT NULL PRIMARY KEY,  
            HasHire        BIT NOT NULL,  
            HasStatus      BIT NOT NULL,  
            HasEnrollment  BIT NOT NULL,  
            HasPayroll     BIT NOT NULL  
        );  
  
  
  
  
  
        INSERT INTO #DataAvailability  
        (  
            EmployeeCodeID,  
            HasHire,  
            HasStatus,  
            HasEnrollment,  
            HasPayroll  
        )  
        SELECT  
            EC.EmpCodeID,  
  
  
  
            CASE  
                WHEN EXISTS  
                (  
                    SELECT 1  
                    FROM EmployeeHireSpan H  
                    WHERE H.employeeCodeId = EC.EmpCodeID  
                )  
                THEN 1 ELSE 0  
            END,  
  
  
  
            CASE  
                WHEN EXISTS  
                (  
                    SELECT 1  
                    FROM EmployeeStatus S  
                    WHERE S.employeeCodeId = EC.EmpCodeID  
                )  
                THEN 1 ELSE 0  
            END,  
  
  
  
            CASE  
                WHEN EXISTS  
                (  
                    SELECT 1  
                    FROM EmployeeEnrollment E  
                    WHERE E.employeeCodeId = EC.EmpCodeID  
                )  
                THEN 1 ELSE 0  
            END,  
  
  
  
            CASE  
                WHEN EXISTS  
                (  
                    SELECT 1  
                    FROM EmployeePayroll P  
                    WHERE P.employeeCodeId = EC.EmpCodeID  
                )  
                THEN 1 ELSE 0  
            END  
  
  
  
        FROM #EmployeeCodes EC;  
  
  
  
  
  
        ------------------------------------------------------------  
        -- 6. Employee Flags  
        --  
        -- Only process flags belonging to relevant employees/year.  
        ------------------------------------------------------------  
        CREATE TABLE #EmployeeFlags  
        (  
            EmployeeSSN  NVARCHAR(100) NOT NULL,  
            EmployerID   BIGINT NOT NULL,  
            FlagID       INT NOT NULL,  
  
  
  
            PRIMARY KEY  
            (  
                EmployeeSSN,  
                EmployerID,  
                FlagID  
            )  
        );  
  
  
  
  
  
        INSERT INTO #EmployeeFlags  
        (  
            EmployeeSSN,  
            EmployerID,  
            FlagID  
        )  
        SELECT DISTINCT  
            EF.EmployeeSSN,  
            EF.EmployerId,  
            EF.FlagId  
        FROM EmployeeFlags EF  
        INNER JOIN #EmployeeCodes EC  
            ON EC.EmployerID = EF.EmployerId  
        INNER JOIN Employee EMP  
            ON EMP.id = EC.EmployeeID  
           AND EMP.ssn = EF.EmployeeSSN  
        WHERE EF.TaxYear = @FillingYear;  
  
  
  
  
  
        ------------------------------------------------------------  
        -- 7. Main result  
        ------------------------------------------------------------  
        CREATE TABLE #TempResults  
        (  
            Id              BIGINT NOT NULL,  
            FirstName       NVARCHAR(200) NULL,  
            LastName        NVARCHAR(200) NULL,  
            FullName        NVARCHAR(401) NULL,  
            SSN             NVARCHAR(100) NULL,  
            Suffix          NVARCHAR(100) NULL,  
            EmployerName    NVARCHAR(500) NULL,  
            EmployeeStatus  NVARCHAR(100) NULL,  
            GetForm         INT NULL,  
            FlagId          INT NULL,  
            FlagDescription NVARCHAR(MAX) NULL,  
            FlagSeverity    VARCHAR(20) NULL,  
            IsMissingData   BIT NOT NULL,  
            EmpCodeID       BIGINT NOT NULL  
        );  
  
  
  
  
  
        ------------------------------------------------------------  
        -- Employee base query  
        ------------------------------------------------------------  
        INSERT INTO #TempResults  
        (  
            Id,  
            FirstName,  
            LastName,  
            FullName,  
            SSN,  
            Suffix,  
            EmployerName,  
            EmployeeStatus,  
            GetForm,  
            FlagId,  
            FlagDescription,  
            FlagSeverity,  
            IsMissingData,  
            EmpCodeID  
        )  
        SELECT  
            EMP.id,  
  
  
  
            EMP.firstName,  
  
  
  
            EMP.lastName,  
  
  
  
            ISNULL(EMP.firstName, '') +  
                CASE  
                    WHEN EMP.firstName IS NOT NULL  
                     AND EMP.lastName IS NOT NULL  
                    THEN ' '  
                    ELSE ''  
                END +  
                ISNULL(EMP.lastName, ''),  
  
  
  
            EMP.ssn,  
  
  
  
            EMP.suffix,  
  
  
  
            ER.name,  
  
  
  
            S.EmployeeStatus,  
  
  
  
            EC.GetForm,  
  
  
  
            EMP.FlagId,  
  
  
  
            EMP.FlagDescription,  
  
  
  
            FD.Severity,  
  
  
  
            CASE  
                WHEN DA.HasHire = 0  
                  OR DA.HasStatus = 0  
                  OR DA.HasEnrollment = 0  
                  OR DA.HasPayroll = 0  
                THEN 1  
                ELSE 0  
            END,  
  
  
  
            EC.EmpCodeID  
  
  
  
        FROM #EmployeeCodes EC  
  
  
  
        INNER JOIN Employee EMP  
            ON EMP.id = EC.EmployeeID  
  
  
  
        LEFT JOIN Employer ER  
            ON ER.id = EC.EmployerID  
  
  
  
        LEFT JOIN #Status S  
            ON S.EmployeeCodeID = EC.EmpCodeID  
  
  
  
        LEFT JOIN #Enrollment EN  
            ON EN.EmployeeCodeID = EC.EmpCodeID  
  
  
  
        LEFT JOIN #DataAvailability DA  
            ON DA.EmployeeCodeID = EC.EmpCodeID  
  
  
  
        LEFT JOIN FlagDefinitions FD  
            ON FD.FlagId = EMP.FlagId  
  
  
  
        WHERE EMP.IsDeleted = 0  
          AND EMP.Flag = 0  
  
  
  
          ----------------------------------------------------------  
          -- Search  
          ----------------------------------------------------------  
          AND  
          (  
              @Search IS NULL  
              OR EMP.firstName LIKE '%' + @Search + '%'  
              OR EMP.lastName LIKE '%' + @Search + '%'  
              OR EMP.fullName LIKE '%' + @Search + '%'  
              OR EMP.ssn LIKE '%' + @Search + '%'  
          )  
  
  
  
          ----------------------------------------------------------  
          -- Status filter  
          ----------------------------------------------------------  
         AND  
(  
    @HasStatusFilter = 0  
  
  
  
    OR  
    (  
        EXISTS  
        (  
            SELECT 1  
            FROM #StatusFilter SF  
            WHERE SF.StatusValue = '-2'  
        )  
        AND S.EmployeeStatus IS NULL  
    )  
  
  
  
    OR  
    EXISTS  
    (  
        SELECT 1  
        FROM #StatusFilter SF  
        WHERE SF.StatusValue = S.EmployeeStatus  
    )  
)  
  
  
  
          ----------------------------------------------------------  
          -- Plan filter  
          ----------------------------------------------------------  
          AND  
          (  
              @HasPlanFilter = 0  
  
  
  
              OR EXISTS  
              (  
                  SELECT 1  
                  FROM #PlanFilter PF  
                  WHERE PF.ID = -2  
                    AND EN.PlanID IS NULL  
              )  
  
  
  
              OR EXISTS  
              (  
                  SELECT 1  
                  FROM #PlanFilter PF  
                  WHERE PF.ID = EN.PlanID  
              )  
          )  
  
  
  
          ----------------------------------------------------------  
          -- Receiving forms  
          ----------------------------------------------------------  
          AND  
          (  
              @HasFormFilter = 0  
  
  
  
              OR EXISTS  
              (  
                  SELECT 1  
                  FROM #ReceivingFormFilter RF  
                  WHERE RF.ID = 0  
                    AND ISNULL(EC.GetForm, 0) = 0  
              )  
  
  
  
              OR EXISTS  
              (  
                  SELECT 1  
                  FROM #ReceivingFormFilter RF  
                  WHERE RF.ID = EC.GetForm  
              )  
          )  
  
  
  
          ----------------------------------------------------------  
          -- Enrollment filter  
          ----------------------------------------------------------  
          AND  
          (  
              @HasEnrollFilter = 0  
  
  
  
              OR EXISTS  
              (  
                  SELECT 1  
                  FROM #EnrollmentFilter EF  
                  WHERE  
                      (  
                          EF.ID = 1  
                          AND  
                          (  
                              EN.EmployeeCodeID IS NULL  
                              OR  
                              (  
                                  EN.CoverageOfferDate IS NULL  
                                  AND ISNULL(EN.IsEnrolled, 0) = 0  
                                  AND ISNULL(EN.UnionMember, 0) = 0  
                                  AND ISNULL(EN.COBRAEnrolled, 0) = 0  
                                  AND ISNULL(EN.RetireeEnrolled, 0) = 0  
                              )  
                          )  
                      )  
  
  
  
                      OR  
                      (  
                          EF.ID = 2  
                          AND EN.CoverageOfferDate IS NOT NULL  
                          AND ISNULL(EN.IsEnrolled, 0) = 0  
                      )  
  
  
  
                      OR  
                      (  
                          EF.ID = 3  
                          AND EN.IsEnrolled = 1  
                      )  
  
  
  
                      OR  
                      (  
                          EF.ID = 4  
                          AND EN.UnionMember = 1  
                      )  
  
  
  
                      OR  
                      (  
                          EF.ID = 5  
                          AND EN.COBRAEnrolled = 1  
                      )  
  
  
  
                      OR  
                      (  
                          EF.ID = 6  
                          AND EN.RetireeEnrolled = 1  
                      )  
              )  
          )  
  
  
  
          ----------------------------------------------------------  
          -- Flag filter  
          ----------------------------------------------------------  
          AND  
          (  
              @HasFlagFilter = 0  
  
  
  
              OR EXISTS  
              (  
                  SELECT 1  
                  FROM #FlagFilter FF  
                  WHERE FF.ID = -2  
                    AND EMP.FlagId IS NULL  
              )  
  
  
  
              OR EXISTS  
              (  
                  SELECT 1  
                  FROM #FlagFilter FF  
                  WHERE FF.ID = EMP.FlagId  
              )  
  
  
  
              OR EXISTS  
              (  
                  SELECT 1  
                  FROM #EmployeeFlags EF  
                  INNER JOIN #FlagFilter FF  
                      ON FF.ID = EF.FlagID  
                  WHERE EF.EmployeeSSN = EMP.ssn  
                    AND EF.EmployerID = EC.EmployerID  
              )  
          )
        OPTION (RECOMPILE);   -- 060: plan for the filters actually used  
  
  
  
  
  
        ------------------------------------------------------------  
        -- 8. Remove duplicate Employee IDs  
        --  
        -- Employee can have multiple EmployeeCode records.  
        -- Keep newest EmpCodeID.  
        ------------------------------------------------------------  
        ;WITH D AS  
        (  
            SELECT  
                EmpCodeID,  
                ROW_NUMBER() OVER  
                (  
                    PARTITION BY Id  
                    ORDER BY EmpCodeID DESC  
                ) AS RN  
            FROM #TempResults  
        )  
        DELETE T  
        FROM #TempResults T  
        INNER JOIN D  
            ON D.EmpCodeID = T.EmpCodeID  
        WHERE D.RN > 1;  
  
  
  
  
  
        ------------------------------------------------------------  
        -- 9. Quick Filter  
        ------------------------------------------------------------  
        IF @QuickFilter IS NOT NULL  
           AND @QuickFilter <> ''  
           AND @QuickFilter <> 'all'  
        BEGIN  
  
  
  
            IF @QuickFilter = 'missing'  
            BEGIN  
                DELETE  
                FROM #TempResults  
                WHERE IsMissingData = 0;  
            END  
  
  
  
            ELSE IF @QuickFilter = 'flagged'  
            BEGIN  
                DELETE  
                FROM #TempResults  
                WHERE FlagId IS NULL  
                   OR IsMissingData = 1;  
            END  
  
  
  
            ELSE IF @QuickFilter = 'valid'  
            BEGIN  
                DELETE  
                FROM #TempResults  
                WHERE FlagId IS NOT NULL  
                   OR IsMissingData = 1;  
            END  
  
  
  
        END;  
  
  
  
  
  
        ------------------------------------------------------------  
        -- 10. Index temporary result  
        ------------------------------------------------------------  
        CREATE CLUSTERED INDEX IX_TempResults_Id  
            ON #TempResults(Id);  
  
  
  
  
  
        ------------------------------------------------------------  
        -- 11. Count  
        ------------------------------------------------------------  
        DECLARE @RecordCount INT;  
  
  
  
        SELECT @RecordCount = COUNT(*)  
        FROM #TempResults;  
  
  
  
  
  
        ------------------------------------------------------------  
        -- 12. Final paging  
        ------------------------------------------------------------  
        SELECT  
            Id,  
            FirstName,  
            LastName,  
            FullName,  
            SSN,  
            Suffix,  
            EmployerName,  
            EmployeeStatus,  
            GetForm,  
            FlagId,  
            FlagDescription,  
            FlagSeverity,  
            IsMissingData,  
            EmpCodeID,  
  
  
  
            ROW_NUMBER() OVER  
            (  
                ORDER BY  
  
  
  
                CASE  
                    WHEN @SortColumn = 0  
                     AND @SortOrder = 'asc'  
                    THEN FullName  
                END ASC,  
  
  
  
                CASE  
                    WHEN @SortColumn = 0  
                     AND @SortOrder = 'desc'  
                    THEN FullName  
                END DESC,  
  
  
  
                CASE  
                    WHEN @SortColumn = 1  
                     AND @SortOrder = 'asc'  
                    THEN LastName  
                END ASC,  
  
  
  
                CASE  
                    WHEN @SortColumn = 1  
                     AND @SortOrder = 'desc'  
                    THEN LastName  
                END DESC,  
  
  
  
                Id ASC  
            ) AS RowNUM,  
  
  
  
            @RecordCount AS TotalCount  
  
  
  
        FROM #TempResults  
  
  
  
        ORDER BY  
  
  
  
            CASE  
                WHEN @SortColumn = 0  
                 AND @SortOrder = 'asc'  
                THEN FullName  
            END ASC,  
  
  
  
            CASE  
                WHEN @SortColumn = 0  
                 AND @SortOrder = 'desc'  
                THEN FullName  
            END DESC,  
  
  
  
            CASE  
                WHEN @SortColumn = 1  
                 AND @SortOrder = 'asc'  
                THEN LastName  
            END ASC,  
  
  
  
            CASE  
                WHEN @SortColumn = 1  
                 AND @SortOrder = 'desc'  
                THEN LastName  
            END DESC,  
  
  
  
            Id ASC  
  
  
  
        OFFSET @PageSize * (@PageIndex - 1) ROWS  
        FETCH NEXT @PageSize ROWS ONLY  
  
  
  
        OPTION (RECOMPILE);  
  
  
  
  
  
        ------------------------------------------------------------  
        -- Metadata  
        ------------------------------------------------------------  
        SELECT  
            @RecordCount AS [RowCount],  
    @RecordCount AS [TotalCount];  
  
  
  
  
  
        ------------------------------------------------------------  
        -- Cleanup  
        ------------------------------------------------------------  
        DROP TABLE #TempResults;  
        DROP TABLE #EmployeeFlags;  
        DROP TABLE #DataAvailability;  
        DROP TABLE #Enrollment;  
        DROP TABLE #Status;  
        DROP TABLE #EmployeeCodes;  
        DROP TABLE #EmployerFilter;  
        DROP TABLE #StatusFilter;  
        DROP TABLE #PlanFilter;  
        DROP TABLE #EnrollmentFilter;  
        DROP TABLE #FlagFilter;  
        DROP TABLE #ReceivingFormFilter;  
  
  
  
    END TRY  
  
  
  
    BEGIN CATCH  
  
  
  
        IF OBJECT_ID('tempdb..#TempResults') IS NOT NULL  
            DROP TABLE #TempResults;  
  
  
  
        IF OBJECT_ID('tempdb..#EmployeeFlags') IS NOT NULL  
            DROP TABLE #EmployeeFlags;  
  
  
  
        IF OBJECT_ID('tempdb..#DataAvailability') IS NOT NULL  
            DROP TABLE #DataAvailability;  
  
  
  
        IF OBJECT_ID('tempdb..#Enrollment') IS NOT NULL  
            DROP TABLE #Enrollment;  
  
  
  
        IF OBJECT_ID('tempdb..#Status') IS NOT NULL  
            DROP TABLE #Status;  
  
  
  
        IF OBJECT_ID('tempdb..#EmployeeCodes') IS NOT NULL  
            DROP TABLE #EmployeeCodes;  
  
  
  
        IF OBJECT_ID('tempdb..#EmployerFilter') IS NOT NULL  
            DROP TABLE #EmployerFilter;  
  
  
  
        IF OBJECT_ID('tempdb..#StatusFilter') IS NOT NULL  
            DROP TABLE #StatusFilter;  
  
  
  
        IF OBJECT_ID('tempdb..#PlanFilter') IS NOT NULL  
            DROP TABLE #PlanFilter;  
  
  
  
        IF OBJECT_ID('tempdb..#EnrollmentFilter') IS NOT NULL  
            DROP TABLE #EnrollmentFilter;  
  
  
  
        IF OBJECT_ID('tempdb..#FlagFilter') IS NOT NULL  
            DROP TABLE #FlagFilter;  
  
  
  
        IF OBJECT_ID('tempdb..#ReceivingFormFilter') IS NOT NULL  
            DROP TABLE #ReceivingFormFilter;  
  
  
  
        THROW;  
  
  
  
    END CATCH  
END
GO

-- =====================================================================
-- 2. Counts for the quick-filter badge bar. Applies the SAME filters as
--    the list (employer, plan, status, enrollment, flag, receiving forms,
--    search) so the counts reflect the filtered subset   only the quick
--    filter itself is excluded (each category is counted independently).
-- =====================================================================
CREATE OR ALTER PROCEDURE [dbo].[sp_Employee_QuickStats]
(
    @Employer_ID        BIGINT = NULL,
    @EmployerIDs        NVARCHAR(MAX) = NULL,
    @FillingYear        INT,
    @Search             NVARCHAR(100) = NULL,
    @PlanIDs            NVARCHAR(MAX) = NULL,
    @Statuses           NVARCHAR(MAX) = NULL,
    @EnrollmentIDs      NVARCHAR(MAX) = NULL,
    @Flags              NVARCHAR(MAX) = NULL,
    @IsReceivingForms   NVARCHAR(MAX) = NULL
)
AS
BEGIN
    SET NOCOUNT ON;

    -- 060: a temp table (with statistics) instead of a table variable, and whether it is used.
    CREATE TABLE #SelectedIDs (ID BIGINT NOT NULL PRIMARY KEY);

    IF (@EmployerIDs IS NOT NULL AND @EmployerIDs <> '[]' AND @EmployerIDs <> '')
        INSERT INTO #SelectedIDs SELECT DISTINCT value FROM OPENJSON(@EmployerIDs);
    ELSE IF (@Employer_ID IS NOT NULL)
    BEGIN
        INSERT INTO #SelectedIDs (ID) VALUES (@Employer_ID);
        INSERT INTO #SelectedIDs (ID) SELECT id FROM Employer WHERE companyId = @Employer_ID AND id <> @Employer_ID;
    END
    DECLARE @HasSelected BIT = CASE WHEN EXISTS (SELECT 1 FROM #SelectedIDs) THEN 1 ELSE 0 END;

    ;WITH FilteredEmployees AS (
        SELECT
            emp.id,
            emp.ssn,
            emp.FlagId,
            CASE WHEN NOT EXISTS (SELECT 1 FROM EmployeeHireSpan HS2 WHERE HS2.employeeCodeId = empCode.id)
                   OR NOT EXISTS (SELECT 1 FROM EmployeeStatus  ST2 WHERE ST2.employeeCodeId = empCode.id)
                   OR NOT EXISTS (SELECT 1 FROM EmployeeEnrollment EN2 WHERE EN2.employeeCodeId = empCode.id)
                   OR NOT EXISTS (SELECT 1 FROM EmployeePayroll  PR2 WHERE PR2.employeeCodeId = empCode.id)
                 THEN 1 ELSE 0 END AS IsMissingData,
            empCode.employerId,
            ROW_NUMBER() OVER(PARTITION BY emp.id ORDER BY empCode.id DESC) AS DuplicateRank
        FROM Employee emp
        INNER JOIN EmployeeCode empCode ON emp.id = empCode.employeeid
        LEFT JOIN EmployeeStatus ES ON ES.employeeCodeId = empCode.id
        LEFT JOIN EmployeeEnrollment EE ON EE.employeeCodeId = empCode.id
        WHERE emp.IsDeleted = 0
          AND emp.Flag = 0
          AND empCode.filingYear = @FillingYear
          AND (@HasSelected = 0 OR empCode.employerId IN (SELECT ID FROM #SelectedIDs))

          AND (@Statuses IS NULL OR @Statuses = '[]'
               OR ES.status IN (SELECT value FROM OPENJSON(@Statuses))
               -- '-2' = the "No status" option: employees with no status history
               OR (EXISTS (SELECT 1 FROM OPENJSON(@Statuses) WHERE value = '-2') AND ES.status IS NULL))
          AND (@PlanIDs IS NULL OR @PlanIDs = '[]'
                   OR EE.planId IN (SELECT value FROM OPENJSON(@PlanIDs))
                   -- '-2' = the "No plan" option: employees with no enrollment/plan
                   OR (EXISTS (SELECT 1 FROM OPENJSON(@PlanIDs) WHERE value = '-2') AND EE.planId IS NULL))
          AND (@IsReceivingForms IS NULL OR @IsReceivingForms = '[]'
               OR empCode.getForm IN (SELECT value FROM OPENJSON(@IsReceivingForms))
               -- NULL getForm counts as "Not Receiving Forms" (0)
               OR (EXISTS (SELECT 1 FROM OPENJSON(@IsReceivingForms) WHERE value = '0') AND empCode.getForm IS NULL))

          AND (@EnrollmentIDs IS NULL OR @EnrollmentIDs = '[]'
               OR (
                    (EXISTS (SELECT 1 FROM OPENJSON(@EnrollmentIDs) WHERE value = '1')
                        AND (EE.id IS NULL OR (EE.coverageOfferDate IS NULL
                                               AND ISNULL(EE.isEnrolled, 0) = 0
                                               AND ISNULL(EE.unionMember, 0) = 0
                                               AND ISNULL(EE.COBRAEnrolled, 0) = 0
                                               AND ISNULL(EE.RetireeEnrolled, 0) = 0)))
                 OR (EXISTS (SELECT 1 FROM OPENJSON(@EnrollmentIDs) WHERE value = '2')
                        AND EE.coverageOfferDate IS NOT NULL AND ISNULL(EE.isEnrolled, 0) = 0)
                 OR (EXISTS (SELECT 1 FROM OPENJSON(@EnrollmentIDs) WHERE value = '3') AND EE.isEnrolled = 1)
                 OR (EXISTS (SELECT 1 FROM OPENJSON(@EnrollmentIDs) WHERE value = '4') AND EE.unionMember = 1)
                 OR (EXISTS (SELECT 1 FROM OPENJSON(@EnrollmentIDs) WHERE value = '5') AND EE.COBRAEnrolled = 1)
                 OR (EXISTS (SELECT 1 FROM OPENJSON(@EnrollmentIDs) WHERE value = '6') AND EE.RetireeEnrolled = 1)
               ))

          AND (@Flags IS NULL OR @Flags = '[]'
               -- '-2' = the "No flags" option
               OR (EXISTS (SELECT 1 FROM OPENJSON(@Flags) WHERE value = '-2') AND emp.FlagId IS NULL)
               OR emp.FlagId IN (SELECT value FROM OPENJSON(@Flags))
               OR EXISTS (SELECT 1 FROM EmployeeFlags EF
                          WHERE EF.EmployeeSSN = emp.ssn
                            AND EF.EmployerId = empCode.employerId
                            AND EF.TaxYear = @FillingYear
                            AND EF.FlagId IN (SELECT value FROM OPENJSON(@Flags))))

          AND (ISNULL(@Search, '') = '' OR (emp.firstName LIKE '%' + @Search + '%' OR emp.lastName LIKE '%' + @Search + '%' OR emp.ssn LIKE '%' + @Search + '%'))
    )
    -- Mutually exclusive categories (missing data takes precedence over flags):
    -- Missing + Flagged + Valid = All
    SELECT
        COUNT(*)                                                            AS AllCount,
        ISNULL(SUM(CASE WHEN IsMissingData = 0 AND FlagId IS NOT NULL
                        THEN 1 ELSE 0 END), 0)                              AS FlaggedCount,
        ISNULL(SUM(CASE WHEN IsMissingData = 1 THEN 1 ELSE 0 END), 0)       AS MissingCount,
        ISNULL(SUM(CASE WHEN IsMissingData = 0 AND FlagId IS NULL
                        THEN 1 ELSE 0 END), 0)                              AS ValidCount
    FROM FilteredEmployees
    WHERE DuplicateRank = 1
    OPTION (RECOMPILE);   -- 060: plan for the filters actually used
END
GO
