/* =============================================================================
   025_file_progress_updates.sql

   Lets the structure check and validation report progress, not just the import.

   UploadedFileLog already has ValidationCurrentStep / ValidationProgressPercent /
   ValidationRowsProcessed / ValidationTotalRows, but only sp_ProcessEmployerData
   (the import) wrote to them, so a file being validated showed an endless
   spinner. The web app now calls this procedure while a file is checked and
   validated, and the File Upload page shows the step and a real percentage.

   Passing NULLs clears the columns. The app does that when a phase ends, so a
   later phase never shows a leftover step from an earlier one.

   Row counts are left to the import: during validation the detail goes into
   the step text ("Reading Employee: 1,200 of 4,800 rows").

   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE dbo.sp_UpdateFileProgress
    @FileLogId INT,
    @Step      VARCHAR(100)  = NULL,
    @Percent   DECIMAL(5, 2) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.UploadedFileLog
    SET    ValidationCurrentStep     = LEFT(@Step, 100),
           ValidationProgressPercent = @Percent,
           ValidationRowsProcessed   = NULL,
           ValidationTotalRows       = NULL
    WHERE  FileLogId = @FileLogId;
END
GO
