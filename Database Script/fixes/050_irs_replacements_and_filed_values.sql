/* =============================================================================
   050_irs_replacements_and_filed_values.sql

   IRS AIR corrections and replacements, as IRS Publication 5165 section 7 sets them out:

   1. dbo.IrsSubmissionLog
      - ReplacesReceiptId: a rejected original is re-sent as a replacement
        (TransmissionTypeCd R) naming the rejected transmission's Receipt ID
        (manifest OriginalReceiptId, 7.3.1). Kept on the row so the replacement
        stays a replacement if it is regenerated again before it is sent.
      - FiledPayerName, FiledPayerEin: the employer name and EIN as written on the
        1094-C. A correction of the 1094-C must name them as filed (7.1.2).

   2. dbo.IrsSubmissionRecord: FiledFirstName, FiledLastName, FiledTin - the
      recipient's name and TIN as written on each 1095. A correction must give
      them "from the original record" (7.1.3). (The same values are in the form
      file on disk; the TIN is the SSN as filed.) dbo.IrsSubmissionRecordList gains
      the same columns (re-created, with the procedure that uses it).

   3. Procedures: sp_SaveSubmissionLog and sp_UpdateSubmissionFiles take the new
      values; sp_SaveSubmissionRecords / sp_GetSubmissionRecords carry the filed
      name and TIN; sp_GetChildSubmissions lists the corrections of a submission
      (a record already corrected is corrected again through its latest accepted
      correction, 7.1.1).

   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- 1. Submission row ----------------------------------------------------------------
IF COL_LENGTH('dbo.IrsSubmissionLog', 'ReplacesReceiptId') IS NULL
    ALTER TABLE dbo.IrsSubmissionLog ADD ReplacesReceiptId VARCHAR(80) NULL;
IF COL_LENGTH('dbo.IrsSubmissionLog', 'FiledPayerName') IS NULL
    ALTER TABLE dbo.IrsSubmissionLog ADD FiledPayerName NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.IrsSubmissionLog', 'FiledPayerEin') IS NULL
    ALTER TABLE dbo.IrsSubmissionLog ADD FiledPayerEin CHAR(9) NULL;
GO

-- 2. Record list ------------------------------------------------------------------
IF COL_LENGTH('dbo.IrsSubmissionRecord', 'FiledFirstName') IS NULL
    ALTER TABLE dbo.IrsSubmissionRecord ADD FiledFirstName NVARCHAR(40) NULL;
IF COL_LENGTH('dbo.IrsSubmissionRecord', 'FiledLastName') IS NULL
    ALTER TABLE dbo.IrsSubmissionRecord ADD FiledLastName NVARCHAR(40) NULL;
IF COL_LENGTH('dbo.IrsSubmissionRecord', 'FiledTin') IS NULL
    ALTER TABLE dbo.IrsSubmissionRecord ADD FiledTin CHAR(9) NULL;
GO

-- The table type gains columns: drop the procedure that uses it, re-create both.
IF OBJECT_ID(N'dbo.sp_SaveSubmissionRecords', N'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_SaveSubmissionRecords;
IF TYPE_ID(N'dbo.IrsSubmissionRecordList') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.table_types tt JOIN sys.columns c ON c.object_id = tt.type_table_object_id
                   WHERE tt.name = N'IrsSubmissionRecordList' AND c.name = N'FiledTin')
    DROP TYPE dbo.IrsSubmissionRecordList;
IF TYPE_ID(N'dbo.IrsSubmissionRecordList') IS NULL
    CREATE TYPE dbo.IrsSubmissionRecordList AS TABLE (
        RecordId       INT          NOT NULL PRIMARY KEY,
        EmployeeId     INT          NOT NULL,
        FiledFirstName NVARCHAR(40) NULL,
        FiledLastName  NVARCHAR(40) NULL,
        FiledTin       CHAR(9)      NULL
    );
GO

-- 3. Procedures -----------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_SaveSubmissionRecords
    @SubmissionId INT,
    @Records      dbo.IrsSubmissionRecordList READONLY
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.IrsSubmissionRecord WHERE SubmissionId = @SubmissionId;
    INSERT INTO dbo.IrsSubmissionRecord (SubmissionId, RecordId, EmployeeId, FiledFirstName, FiledLastName, FiledTin)
    SELECT @SubmissionId, RecordId, EmployeeId, FiledFirstName, FiledLastName, FiledTin FROM @Records;
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetSubmissionRecords
    @SubmissionId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT RecordId, EmployeeId, FiledFirstName, FiledLastName, FiledTin
    FROM dbo.IrsSubmissionRecord WHERE SubmissionId = @SubmissionId ORDER BY RecordId;
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetChildSubmissions
    @ParentSubmissionId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.IrsSubmissionLog WHERE ParentSubmissionId = @ParentSubmissionId ORDER BY GeneratedDate, SubmissionId;
END;
GO

CREATE OR ALTER PROCEDURE [dbo].[sp_SaveSubmissionLog]
    @EmployerId         INT,
    @TaxYear            INT,
    @FormType           NVARCHAR(50),
    @TransmissionId     UNIQUEIDENTIFIER,
    @ManifestFilePath   NVARCHAR(1024),
    @FormFilePath       NVARCHAR(1024),
    @RecordCount        INT,
    @StatusId           INT,
    @GeneratedBy        NVARCHAR(256),
    @SubmissionType     VARCHAR(1)    = 'O',
    @ParentSubmissionId INT           = NULL,
    @StatusMessage      NVARCHAR(512) = NULL,
    @FiledPayerName     NVARCHAR(100) = NULL,
    @FiledPayerEin      CHAR(9)       = NULL
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.IrsSubmissionLog
           (EmployerId, TaxYear, FormType, TransmissionId,
            ManifestFilePath, FormFilePath, RecordCount,
            StatusId, GeneratedBy, SubmissionType, ParentSubmissionId,
            StatusMessage, FiledPayerName, FiledPayerEin)
    VALUES (@EmployerId, @TaxYear, @FormType, @TransmissionId,
            @ManifestFilePath, @FormFilePath, @RecordCount,
            @StatusId, @GeneratedBy, @SubmissionType, @ParentSubmissionId,
            @StatusMessage, @FiledPayerName, @FiledPayerEin);

    SELECT CAST(SCOPE_IDENTITY() AS INT) AS SubmissionId;
END
GO

CREATE OR ALTER PROCEDURE [dbo].[sp_UpdateSubmissionFiles]
    @Id                INT,
    @FormType          NVARCHAR(50),
    @TransmissionId    UNIQUEIDENTIFIER,
    @ManifestFilePath  NVARCHAR(1024),
    @FormFilePath      NVARCHAR(1024),
    @RecordCount       INT,
    @GeneratedBy       NVARCHAR(256),
    @ReplacesReceiptId VARCHAR(80)   = NULL,
    @FiledPayerName    NVARCHAR(100) = NULL,
    @FiledPayerEin     CHAR(9)       = NULL
AS
BEGIN
    SET NOCOUNT ON;
    -- New files are a new transmission: the previous one's receipt, acknowledgement and
    -- errors no longer describe this row (049). A replacement keeps naming the rejected
    -- transmission it replaces (050).
    DELETE FROM dbo.IrsSubmissionErrors WHERE SubmissionId = @Id;
    UPDATE dbo.IrsSubmissionLog
    SET    FormType          = @FormType,
           TransmissionId    = @TransmissionId,
           ManifestFilePath  = @ManifestFilePath,
           FormFilePath      = @FormFilePath,
           RecordCount       = @RecordCount,
           StatusId          = 0,
           StatusMessage     = CASE WHEN @ReplacesReceiptId IS NULL THEN 'Regenerated' ELSE 'Replacement for rejected receipt ' + @ReplacesReceiptId END,
           ReceiptId         = NULL,
           AckFilePath       = NULL,
           TransmittedDate   = NULL,
           ReplacesReceiptId = @ReplacesReceiptId,
           FiledPayerName    = @FiledPayerName,
           FiledPayerEin     = @FiledPayerEin,
           GeneratedBy       = @GeneratedBy,
           GeneratedDate     = GETDATE()
    WHERE  SubmissionId = @Id;
END;
GO

PRINT '050: IRS replacements (ReplacesReceiptId), filed name/TIN for corrections, child submissions.';
GO
