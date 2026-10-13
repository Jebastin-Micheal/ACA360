/* =============================================================================
   049_irs_records_ack_and_age.sql

   IRS AIR filing (IrsXmlService), following IRS Publication 5165:

   1. dbo.IrsSubmissionRecord: which employee each 1095 in a submission is. The
      IRS requires Record IDs to run 1, 2, 3... within a submission (Pub 5165,
      3.2), so they can no longer be employee ids; the acknowledgement's errors
      (ReceiptId|SubmissionId|RecordId) are traced back to employees through this
      table, and corrections name the record they correct from it.
      dbo.IrsSubmissionRecordList is the table type the web app passes the rows in.

   2. dbo.IrsSubmissionErrors gets UniqueRecordId (ReceiptId|SubmissionId|RecordId
      or ReceiptId|SubmissionId for errors on the 1094, as the IRS reports them).
      Nothing ever wrote to this table; the acknowledgement upload now does.

   3. Procedures:
      sp_SaveSubmissionRecords   replace a submission's record list
      sp_GetSubmissionRecords    read it
      sp_ClearSubmissionErrors   remove a submission's errors (before re-reading an acknowledgement)
      sp_AddSubmissionError      add one error (employee name looked up)
      sp_GetSubmissionErrors     unchanged columns plus UniqueRecordId (SELECT *)
      sp_UpdateSubmissionFiles   regenerated files are a new transmission: the old
                                 Receipt ID, acknowledgement and errors are cleared
      sp_GetSubmissionByEmployerYear  the original filing only (never a correction row)

   4. sp_GetEmployeeCodesList also returns the employee's date of birth
      (BirthDate): the 1095-C needs the age on January 1 with ICHRA codes 1L-1S.
      Otherwise unchanged.

   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- 1. Record list -----------------------------------------------------------------
IF OBJECT_ID(N'dbo.IrsSubmissionRecord', N'U') IS NULL
    CREATE TABLE dbo.IrsSubmissionRecord (
        SubmissionId INT      NOT NULL,
        RecordId     INT      NOT NULL,
        EmployeeId   INT      NOT NULL,
        CreatedDate  DATETIME NOT NULL CONSTRAINT DF_IrsSubmissionRecord_Created DEFAULT GETDATE(),
        CONSTRAINT PK_IrsSubmissionRecord PRIMARY KEY (SubmissionId, RecordId),
        CONSTRAINT FK_IrsSubmissionRecord_Log FOREIGN KEY (SubmissionId)
            REFERENCES dbo.IrsSubmissionLog (SubmissionId) ON DELETE CASCADE
    );
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_IrsSubmissionRecord_Employee' AND object_id = OBJECT_ID(N'dbo.IrsSubmissionRecord'))
    CREATE INDEX IX_IrsSubmissionRecord_Employee ON dbo.IrsSubmissionRecord (SubmissionId, EmployeeId);
GO
IF TYPE_ID(N'dbo.IrsSubmissionRecordList') IS NULL
    CREATE TYPE dbo.IrsSubmissionRecordList AS TABLE (RecordId INT NOT NULL PRIMARY KEY, EmployeeId INT NOT NULL);
GO

-- 2. Error rows carry the IRS identifier -----------------------------------------
IF COL_LENGTH('dbo.IrsSubmissionErrors', 'UniqueRecordId') IS NULL
    ALTER TABLE dbo.IrsSubmissionErrors ADD UniqueRecordId VARCHAR(120) NULL;
GO

-- 3. Procedures -----------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_SaveSubmissionRecords
    @SubmissionId INT,
    @Records      dbo.IrsSubmissionRecordList READONLY
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.IrsSubmissionRecord WHERE SubmissionId = @SubmissionId;
    INSERT INTO dbo.IrsSubmissionRecord (SubmissionId, RecordId, EmployeeId)
    SELECT @SubmissionId, RecordId, EmployeeId FROM @Records;
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetSubmissionRecords
    @SubmissionId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT RecordId, EmployeeId FROM dbo.IrsSubmissionRecord WHERE SubmissionId = @SubmissionId ORDER BY RecordId;
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_ClearSubmissionErrors
    @SubmissionId INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.IrsSubmissionErrors WHERE SubmissionId = @SubmissionId;
END;
GO

-- RecordId keeps its meaning for the correction screen: the employee id (NULL for an
-- error on the 1094 itself).
CREATE OR ALTER PROCEDURE dbo.sp_AddSubmissionError
    @SubmissionId   INT,
    @EmployeeId     INT           = NULL,
    @UniqueRecordId VARCHAR(120)  = NULL,
    @ErrorCode      VARCHAR(50)   = NULL,
    @ErrorMessage   NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.IrsSubmissionErrors (SubmissionId, RecordId, EmployeeName, ErrorCode, ErrorMessage, IsFixed, CreatedDate, UniqueRecordId)
    SELECT @SubmissionId,
           CAST(@EmployeeId AS VARCHAR(50)),
           CASE WHEN @EmployeeId IS NULL THEN 'Form 1094 (whole submission)'
                ELSE LEFT(LTRIM(RTRIM(CONCAT(e.firstName, ' ', e.lastName))), 100) END,
           LEFT(@ErrorCode, 50), @ErrorMessage, 0, GETDATE(), @UniqueRecordId
    FROM (SELECT 1 AS one) x
    LEFT JOIN dbo.Employee e ON e.id = @EmployeeId;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[sp_UpdateSubmissionFiles]
    @Id               INT,
    @FormType         NVARCHAR(50),
    @TransmissionId   UNIQUEIDENTIFIER,
    @ManifestFilePath NVARCHAR(1024),
    @FormFilePath     NVARCHAR(1024),
    @RecordCount      INT,
    @GeneratedBy      NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;
    -- New files are a new transmission: the previous one's receipt, acknowledgement and
    -- errors no longer describe this row (049).
    DELETE FROM dbo.IrsSubmissionErrors WHERE SubmissionId = @Id;
    UPDATE dbo.IrsSubmissionLog
    SET    FormType         = @FormType,
           TransmissionId   = @TransmissionId,
           ManifestFilePath = @ManifestFilePath,
           FormFilePath     = @FormFilePath,
           RecordCount      = @RecordCount,
           StatusId         = 0,
           StatusMessage    = 'Regenerated',
           ReceiptId        = NULL,
           AckFilePath      = NULL,
           TransmittedDate  = NULL,
           GeneratedBy      = @GeneratedBy,
           GeneratedDate    = GETDATE()
    WHERE  SubmissionId = @Id;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[sp_GetSubmissionByEmployerYear]
    @EmployerId INT,
    @TaxYear    INT
AS
BEGIN
    SET NOCOUNT ON;
    -- The original filing; correction rows (SubmissionType C) are separate transmissions (049).
    SELECT TOP 1 *
    FROM   dbo.IrsSubmissionLog
    WHERE  EmployerId = @EmployerId AND TaxYear = @TaxYear
      AND  ISNULL(SubmissionType, 'O') <> 'C'
    ORDER BY GeneratedDate DESC;
END;
GO

-- 4. Employee date of birth for the 1095-C --------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[sp_GetEmployeeCodesList]
    @EmployerId INT,
    @Year INT
AS
BEGIN
    BEGIN TRY
        SELECT
            ec.*,
            e.FirstName, e.LastName, e.SSN,
            e.[address]  AS Address1,
            e.[address2] AS Address2,
            e.[city]     AS City,
            e.[state]    AS [State],
            e.[zip]      AS Zip,
            e.[birthday] AS BirthDate   -- 049: age on January 1 for ICHRA codes 1L-1S
        FROM EmployeeCode ec
        INNER JOIN Employee e ON ec.employeeId = e.id
        WHERE ec.employerId = @EmployerId AND ec.filingYear = @Year
        ORDER BY e.LastName;
    END TRY
    BEGIN CATCH
        SELECT
            ERROR_NUMBER() AS ErrorNumber,
            ERROR_LINE() AS ErrorLine,
            ERROR_MESSAGE() AS ErrorMessage;
    END CATCH
END
GO

PRINT '049: IRS record list, acknowledgement errors, regenerated files reset, employee date of birth.';
GO
