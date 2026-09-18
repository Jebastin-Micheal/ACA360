/* =============================================================================
   003_F-30_scope_download_history.sql

   FIXES   F-30 — the downloads hub lists every employer's generated form
           archives to every user, and any listed file can then be fetched.

   WHY     sp_GetDownloadHistory takes no parameters at all. It returns the whole
           of GenerateMultiEmployerBatchHistory ordered by date.
           DownloadService.GetDownloadsDashboardAsync is handed userId and role
           and passes neither. DownloadsController.GetFile blocks directory
           traversal correctly but performs no ownership check, so any name the
           listing reveals can be downloaded.

           Result: an Employer or Broker login opens /Downloads and sees a
           directory of every form batch in the system — employer names, form
           types, dates — and can download any of them.

   THE MISSING PIECE

           GenerateMultiEmployerBatchHistoryEmployers exists, carries an index
           (IX_BatchHistoryEmployers_EmployerYear) and a foreign key, and holds
           ZERO rows. Nothing in the C# or the SQL ever inserted into it.

           That is why the hub could not scope itself: EmployerNames on the
           parent table is a comma-joined display string, not something you can
           join on, and the table that was meant to carry the real link was never
           populated. Two other features read it and silently never worked:

               ACAController.DownloadForms  -> always "No generated batch found"
               FilingService                -> HasBatch always false

           PdfService.GenerateMultiEmployerBatchAsync now captures SCOPE_IDENTITY
           and writes one link row per employer, so batches generated from here on
           are scopeable and those two features start working. This script only
           replaces the read procedure.

   WHAT    Replaces sp_GetDownloadHistory with a scoped version taking:

               @EmployerIDs   JSON array of the caller's permitted employer ids
               @Unrestricted  1 for roles that legitimately see every employer
               @FileName      optional; narrows to one file for the access check

           A batch is visible when the caller is entitled to EVERY employer in
           it — not merely one. A multi-employer archive contains forms for all
           of them, so partial entitlement must not grant the download.

   LEGACY ROWS
           The 14 existing history rows pre-date the link fix and have no link
           rows, so they cannot be scoped. They are shown to unrestricted roles
           only and hidden from everyone else — fail closed. They stay
           downloadable by admins, and become listable again for scoped users
           only if you backfill. There is a commented backfill at the end that
           matches on EmployerNames; it is NOT run by default because that column
           is a display string and the match is not reliable.

   SAFE TO RE-RUN
           Yes. CREATE OR ALTER, and the backfill is commented out.

   DRIFT WARNING
           Replaces the whole body of sp_GetDownloadHistory. The original took no
           parameters, so any other caller must be updated — at the time of
           writing DownloadService was the only one.
   ============================================================================= */

USE [db_ACA360];
GO

SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE [dbo].[sp_GetDownloadHistory]
    @EmployerIDs  NVARCHAR(MAX) = NULL,   -- JSON array, e.g. '[3,7,12]'
    @Unrestricted BIT           = 0,
    @FileName     VARCHAR(255)  = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Permitted TABLE (EmployerId INT PRIMARY KEY);

    IF @Unrestricted = 0
       AND @EmployerIDs IS NOT NULL
       AND LTRIM(RTRIM(@EmployerIDs)) NOT IN ('', '[]')
    BEGIN
        INSERT INTO @Permitted (EmployerId)
        SELECT DISTINCT TRY_CAST(value AS INT)
        FROM   OPENJSON(@EmployerIDs)
        WHERE  TRY_CAST(value AS INT) IS NOT NULL;
    END

    SELECT
        h.FileName,
        h.EmployerNames,
        h.FormType,
        h.TaxYear,
        h.FileSizeBytes,
        h.GeneratedDate
    FROM   dbo.GenerateMultiEmployerBatchHistory h
    WHERE  (@FileName IS NULL OR h.FileName = @FileName)
      AND  (
                /* Roles that see every employer are not filtered at all. This
                   deliberately matches SelectEmployerController.IsUnrestrictedRole
                   so the hub and the employer picker agree on who is unscoped. */
                @Unrestricted = 1

                /* Otherwise: the batch must be linked, and every employer in it
                   must be permitted. NOT EXISTS over the un-permitted rows is the
                   "all of them" test — a batch containing one employer the caller
                   cannot reach is withheld entirely. */
             OR (
                    EXISTS (
                        SELECT 1
                        FROM   dbo.GenerateMultiEmployerBatchHistoryEmployers e
                        WHERE  e.BatchHistoryId = h.Id
                    )
                AND NOT EXISTS (
                        SELECT 1
                        FROM   dbo.GenerateMultiEmployerBatchHistoryEmployers e
                        WHERE  e.BatchHistoryId = h.Id
                          AND  NOT EXISTS (SELECT 1 FROM @Permitted p
                                           WHERE p.EmployerId = e.EmployerId)
                    )
                )
           )
    ORDER BY h.GeneratedDate DESC;
END
GO

/* -------------------------------------------------------------- VERIFY ----- */
PRINT '--- Batch history: linked vs unlinked ---';
SELECT  Linked   = SUM(CASE WHEN x.LinkRows > 0 THEN 1 ELSE 0 END),
        Unlinked = SUM(CASE WHEN x.LinkRows = 0 THEN 1 ELSE 0 END),
        Total    = COUNT(*)
FROM    dbo.GenerateMultiEmployerBatchHistory h
CROSS APPLY (
    SELECT LinkRows = COUNT(*)
    FROM   dbo.GenerateMultiEmployerBatchHistoryEmployers e
    WHERE  e.BatchHistoryId = h.Id
) x;

PRINT '--- An unrestricted caller sees every batch ---';
EXEC dbo.sp_GetDownloadHistory @EmployerIDs = NULL, @Unrestricted = 1;

PRINT '--- A caller entitled to nothing sees none ---';
EXEC dbo.sp_GetDownloadHistory @EmployerIDs = N'[]', @Unrestricted = 0;
GO

/* =============================================================================
   OPTIONAL BACKFILL — deliberately left commented.

   Reattaches the pre-existing history rows to employers by matching the
   comma-joined EmployerNames display string against Employer.name. Run it only
   if you have checked the match on your own data: names are not unique across
   filing years, may have been edited since the batch was generated, and any
   mismatch here silently grants or withholds access to a form archive.

   Verify first:

       SELECT h.Id, h.FileName, h.TaxYear, h.EmployerNames,
              Matched = (SELECT COUNT(DISTINCT emp.id)
                         FROM STRING_SPLIT(h.EmployerNames, ',') s
                         JOIN dbo.Employer emp
                           ON LTRIM(RTRIM(emp.name)) = LTRIM(RTRIM(s.value))
                          AND emp.filingYear = h.TaxYear),
              Expected = (SELECT COUNT(*) FROM STRING_SPLIT(h.EmployerNames, ',') s
                          WHERE LTRIM(RTRIM(s.value)) <> '')
       FROM dbo.GenerateMultiEmployerBatchHistory h
       WHERE NOT EXISTS (SELECT 1 FROM dbo.GenerateMultiEmployerBatchHistoryEmployers e
                         WHERE e.BatchHistoryId = h.Id);

   Only rows where Matched = Expected are safe to backfill.

   INSERT INTO dbo.GenerateMultiEmployerBatchHistoryEmployers (BatchHistoryId, EmployerId, TaxYear)
   SELECT DISTINCT h.Id, emp.id, h.TaxYear
   FROM   dbo.GenerateMultiEmployerBatchHistory h
   CROSS APPLY STRING_SPLIT(h.EmployerNames, ',') s
   JOIN   dbo.Employer emp
          ON  LTRIM(RTRIM(emp.name)) = LTRIM(RTRIM(s.value))
          AND emp.filingYear = h.TaxYear
   WHERE  NOT EXISTS (SELECT 1 FROM dbo.GenerateMultiEmployerBatchHistoryEmployers e
                      WHERE e.BatchHistoryId = h.Id);
   ============================================================================= */
