/* =============================================================================
   014_F-37_email_delivery_status.sql

   FIXES   F-37 — the application recorded email deliveries it never performed.

   WHY     DistributionController.SendSecureEmail generated the secure link, wrote a
           CommunicationLog row and told the user "Secure link sent successfully".
           The send itself was a commented-out line. The batch path carried a
           "TODO: Actual email logic goes here".

           Nothing in the solution sent mail at all: the only SmtpClient was the
           "test connection" button on System Settings.

           sp_LogEmail compounded it by never populating CommunicationLog.Status,
           so the row's mere existence was the only evidence of a delivery — and it
           meant nothing.

   WHAT    Adds @Status to sp_LogEmail and writes it. The application now supplies
           the real outcome from the provider:

               Sent      the provider accepted the message
               Failed    it was attempted and rejected, or the provider was unreachable
               Skipped   no usable address on file, nothing attempted
               Disabled  no provider configured, or sending switched off

           Also stops the procedure swallowing its own errors. It previously caught
           everything and returned the error as a RESULT SET, which Dapper's
           ExecuteAsync discards — so a failed write looked like a successful one.

   PROVIDER
           SendGrid, over its v3 REST API. Configuration belongs with the other
           secrets — User Secrets in development, environment or key vault in
           production — NOT in the settings table beside SMTP_Pass, which is stored
           in clear:

               "SendGrid": {
                 "ApiKey":    "SG.xxxxx",
                 "FromEmail": "no-reply@yourdomain.com",
                 "FromName":  "ACA360",
                 "ReplyTo":   "support@yourdomain.com",
                 "Enabled":   true
               }

           FromEmail must be a verified sender or a verified domain in SendGrid, or
           every send returns 403.

   THE SMTP SETTINGS SCREEN IS NOW MISLEADING
           System Settings still shows Host/Port/User/Pass and a working "test
           connection" button, and none of it affects delivery any more. Either
           point that screen at SendGrid or label it clearly; leaving it as-is
           invites someone to "fix" mail by editing fields nothing reads.

   SAFE TO RE-RUN
           Yes. CREATE OR ALTER, and the new parameter is optional.
   ============================================================================= */

USE [db_ACA360];
GO

SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE [dbo].[sp_LogEmail]
    @EntityId   INT,
    @EntityType VARCHAR(255),
    @Type       VARCHAR(255),
    @SentTo     VARCHAR(255),
    @SentBy     INT,
    @Subject    VARCHAR(500),
    @Body       NVARCHAR(MAX),
    @Status     VARCHAR(50) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    /* No TRY/CATCH. The previous version caught every error and returned it as a
       result set, which the Dapper call discards — so a row that failed to write
       reported success. A genuine failure should reach the caller, which now
       records it against the send rather than losing it. */
    INSERT INTO dbo.CommunicationLog
        (RelatedEntityId, EntityType, Type, SentTo, SentBy, SentDate, Subject, MessageBody, Status)
    VALUES
        (@EntityId, @EntityType, @Type, @SentTo, @SentBy, GETUTCDATE(), @Subject, @Body,
         NULLIF(LTRIM(RTRIM(ISNULL(@Status, ''))), ''));
END
GO

/* -------------------------------------------------------------- VERIFY ----- */
PRINT '--- sp_LogEmail parameters ---';
SELECT ParameterName = name, Position = parameter_id, IsOptional = has_default_value
FROM   sys.parameters
WHERE  object_id = OBJECT_ID('dbo.sp_LogEmail')
ORDER BY parameter_id;

PRINT '--- Delivery outcomes recorded so far ---';
PRINT '    Rows with a NULL status pre-date this change and were never actually sent.';
SELECT  Status    = ISNULL(Status, '(null - never sent)'),
        Messages  = COUNT(*),
        Earliest  = MIN(SentDate),
        Latest    = MAX(SentDate)
FROM    dbo.CommunicationLog
GROUP BY ISNULL(Status, '(null - never sent)')
ORDER BY COUNT(*) DESC;

PRINT '--- Employees still waiting on a working notification ---';
SELECT  TaxYear      = l.TaxYear,
        LinksIssued  = COUNT(*),
        Collected    = SUM(CASE WHEN l.IsUsed = 1 THEN 1 ELSE 0 END),
        NotCollected = SUM(CASE WHEN l.IsUsed = 0 THEN 1 ELSE 0 END)
FROM    dbo.SecureDownloadLinks l
GROUP BY l.TaxYear
ORDER BY l.TaxYear;
PRINT '    No rows means no link has ever been issued, which is expected until the';
PRINT '    first real send now that delivery works.';
GO
