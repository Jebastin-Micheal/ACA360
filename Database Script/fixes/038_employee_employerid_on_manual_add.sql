/* =============================================================================
   038_employee_employerid_on_manual_add.sql

   Employee.EmployerId is what the web app checks before it lets anyone open or
   change an employee (BaseController.CheckEmployeeAccessAsync). The file import
   fills it in, but usp_employee_basic_details_action - the "Add employee" form -
   never did: it wrote the employer to EmployeeCode only. A manually added employee
   therefore had no employer, failed the check, and could not be opened by anyone,
   admins included. (Found locally: 1 of 927 employees, id 927, employer 1021.)

   1. usp_employee_basic_details_action
      - Insert: Employee.EmployerId is set from @EmployerId.
      - Update: Employee.EmployerId is kept in step with EmployeeCode.employerId,
        which this procedure already overwrites with @EmployerId. A NULL
        @EmployerId leaves Employee.EmployerId as it was.
      Nothing else in the procedure is changed.

   2. Backfill
      Employees with no EmployerId take the employer of their latest EmployeeCode
      row (highest filingYear, then highest id). Employees with no EmployeeCode row
      stay NULL and are listed. Rows whose EmployerId disagrees with their
      EmployeeCode rows are listed only, not changed.

   Prints what it changed. Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- 1. Procedure ---------------------------------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[usp_employee_basic_details_action]
    @EmployeeID bigint = null,
    @IsCorrected BIT = null,
    @FirstName NVARCHAR(100) = null,
    @MiddleName NVARCHAR(100) = NULL,
    @LastName NVARCHAR(100) = null,
    @SSN NVARCHAR(20) = null,
    @Suffix NVARCHAR(20) = NULL,
    @Birthday DATE = null,
    @Email NVARCHAR(255) = null,
    @Address NVARCHAR(255) = null,
    @Address2 NVARCHAR(255) = NULL,
    @City NVARCHAR(100) = null,
    @State NVARCHAR(50) = null,
    @Zip NVARCHAR(20)= null,
    @Country NVARCHAR(100) = NULL,
    @IsForeign BIT = null,
    @getForm BIT = null,
    @ConsentedToElectronic BIT = null,
    @IsExPatriot BIT= null,
    @EmployerId bigint = null,
    @FilingYear int = null,
    @StateId INT = null,
    @CountryId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        ---------------------------------------------------------------
        -- 0a. DOB cannot be a future date
        ---------------------------------------------------------------
        IF @Birthday IS NOT NULL AND @Birthday > CAST(GETDATE() AS DATE)
        BEGIN
            RAISERROR('INVALID_DOB: Date of Birth cannot be a future date.', 16, 1);
            ROLLBACK TRANSACTION;
            RETURN;
        END

        ---------------------------------------------------------------
        -- 0b. Duplicate SSN check (scoped to same Employer + FilingYear)
        ---------------------------------------------------------------
        IF @SSN IS NOT NULL AND LEN(LTRIM(RTRIM(@SSN))) > 0
        BEGIN
            IF EXISTS (
                SELECT 1
                FROM Employee e
                INNER JOIN EmployeeCode ec ON ec.employeeId = e.id
                WHERE e.SSN = @SSN
                  AND ec.employerId = @EmployerId
                  AND ec.filingYear = @FilingYear
                  AND e.id <> ISNULL(@EmployeeID, 0)   -- exclude self on update
            )
            BEGIN
                RAISERROR('DUPLICATE_SSN: An employee with this SSN already exists for this employer and filing year.', 16, 1);
                ROLLBACK TRANSACTION;
                RETURN;
            END
        END

        ---------------------------------------------------------------
        -- Declare locals AFTER validation passes
        ---------------------------------------------------------------
        DECLARE @NewEmployeeCodeID INT = NULL;

        IF EXISTS (SELECT 1 FROM Employee WHERE id = @EmployeeID)
        BEGIN
            -- 1. Update existing employee
            UPDATE Employee
            SET
               -- IsCorrected = @IsCorrected,
                FirstName   = @FirstName,
                MiddleName  = @MiddleName,
                LastName    = @LastName,
                SSN         = @SSN,
                Suffix      = @Suffix,
                Birthday    = @Birthday,
                email       = @Email,
                [Address]   = @Address,
                Address2    = @Address2,
                City        = @City,
                stateId     = @StateId,
                [state]     = @State,
                Zip         = @Zip,
                countryId   = @CountryId,
                IsForeign   = @IsForeign,
                HasConsentedToElectronic = @ConsentedToElectronic,
                IsExPatriot = @IsExPatriot,
                -- 038: kept in step with EmployeeCode.employerId below
                EmployerId  = COALESCE(@EmployerId, EmployerId)
            WHERE id = @EmployeeID;
            --update the isCorrected , getForm in employeedcode
            UPDATE EmployeeCode set isCorrected = @IsCorrected , getForm = @getForm ,employerId = @EmployerId  where employeeId = @EmployeeID;
        END
        ELSE
        BEGIN
            -- 2. Insert new employee
            INSERT INTO Employee ( --IsCorrected,
                fullName, FirstName, MiddleName, LastName, SSN, Suffix,
                Birthday, email, [Address], Address2, City, stateId,[state], Zip,
                countryId, IsForeign, HasConsentedToElectronic, IsExPatriot,
                EmployerId   -- 038
            )
            VALUES (
                --@IsCorrected,
                LTRIM(RTRIM(
                    ISNULL(LTRIM(RTRIM(@FirstName)) + ' ', '') +
                    ISNULL(LTRIM(RTRIM(@LastName)), '')
                )), @FirstName, @MiddleName, @LastName, @SSN, @Suffix,
                @Birthday, @Email, @Address, @Address2, @City, @StateId,@State,@Zip,
                @CountryId, @IsForeign,@ConsentedToElectronic ,@IsExPatriot,
                @EmployerId   -- 038
            );

            SET @EmployeeID = SCOPE_IDENTITY();

            -- 3. Insert into EmployeeCode
            INSERT INTO EmployeeCode (
                employeeId,
                employerId,
                filingYear,
                getForm,
                isCorrected,
                IsLocked
            )
            VALUES (
                @EmployeeID,
                @EmployerId,
                @FilingYear,
                @getForm,
                @IsCorrected,
                0
            );

           -- SET @NewEmployeeCodeID = SCOPE_IDENTITY();
        END

        COMMIT TRANSACTION;

    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;

        DECLARE @ErrorMessage  NVARCHAR(4000) = ERROR_MESSAGE();
        DECLARE @ErrorSeverity INT            = ERROR_SEVERITY();
        DECLARE @ErrorState    INT            = ERROR_STATE();

        RAISERROR(@ErrorMessage, @ErrorSeverity, @ErrorState);
    END CATCH
END;
GO

-- 2. Backfill ----------------------------------------------------------------
BEGIN TRANSACTION;

DECLARE @Filled TABLE (EmployeeId INT, EmployerId BIGINT, FromFilingYear INT);

UPDATE e
SET    e.EmployerId = latest.employerId
OUTPUT inserted.id, inserted.EmployerId, latest.filingYear
INTO   @Filled (EmployeeId, EmployerId, FromFilingYear)
FROM   dbo.Employee e
CROSS APPLY (
    SELECT TOP (1) ec.employerId, ec.filingYear
    FROM   dbo.EmployeeCode ec
    WHERE  ec.employeeId = e.id
    ORDER BY ec.filingYear DESC, ec.id DESC
) latest
WHERE  e.EmployerId IS NULL;

COMMIT TRANSACTION;

DECLARE @FilledCount INT = (SELECT COUNT(*) FROM @Filled);
PRINT CONCAT('Employees given an EmployerId: ', @FilledCount);
SELECT EmployeeId, EmployerId, FromFilingYear FROM @Filled ORDER BY EmployeeId;

-- Still NULL: no EmployeeCode row to take an employer from.
SELECT e.id AS EmployeeId, e.fullName, e.IsDeleted
FROM   dbo.Employee e
WHERE  e.EmployerId IS NULL;

-- Report only: EmployerId not matching any of the employee's EmployeeCode rows.
SELECT e.id AS EmployeeId, e.EmployerId AS EmployeeEmployerId,
       STRING_AGG(CONCAT(ec.filingYear, ':', ec.employerId), ', ') AS EmployeeCodeEmployers
FROM   dbo.Employee e
JOIN   dbo.EmployeeCode ec ON ec.employeeId = e.id
WHERE  e.EmployerId IS NOT NULL
  AND  NOT EXISTS (SELECT 1 FROM dbo.EmployeeCode x
                   WHERE x.employeeId = e.id AND x.employerId = e.EmployerId)
GROUP BY e.id, e.EmployerId;
GO
