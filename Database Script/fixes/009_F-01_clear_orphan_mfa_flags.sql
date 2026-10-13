/* =============================================================================
   009_F-01_clear_orphan_mfa_flags.sql

   FIXES   F-01 — accounts flagged for MFA cannot log in.

   WHY     AccountController.Login redirected to Account/MFA when isMFA = 1. That
           action does not exist and never has, and no view backs it. The password
           check succeeded, no sign-in cookie was issued, and the 404 left the user
           unable to reach Settings to switch the flag back off.

           No second-factor provider is implemented anywhere in the solution: there
           is no authenticator-key storage on tbl_User, and outbound email does not
           work either — the only SmtpClient is the "test connection" button on the
           System Settings screen (see F-37). So the flag has never gated anything.
           Its entire observable effect has been to break login.

   WHO IS AFFECTED RIGHT NOW

           Two rows in this database carry isMFA = 1:

               User_ID 35   jamesmicheal007@gmail.com
               User_ID 36   jamesmicheal007@gmail.com

           Both are locked out today. PART 1 lists them before changing anything.

   WHAT    Clears isMFA on every account that carries it. This removes no
           protection, because none was ever in force.

   COMPANION C# CHANGES — already applied in the working tree

           - Login no longer redirects to the missing action. It records the
             condition and continues, so an account cannot be stranded again.
           - SettingsController.ToggleMFA refuses to enable and explains why.
             Disabling still works, so anyone flagged can clear it themselves.
           - The admin MFA dropdown in UserAccountsController offers "Disable"
             only. An administrator setting "Enable" across the estate would
             previously have locked out every user at once.

   THIS IS NOT AN MFA IMPLEMENTATION

           It closes the lockout. Building a real second factor is a separate piece
           of work and needs a decision on which factor to use — see the note at the
           end of this script.

   SAFE TO RE-RUN
           Yes. Scoped to rows that still carry the flag.
   ============================================================================= */

USE [db_ACA360];
GO

SET NOCOUNT ON;
GO

/* ---------------------------------------------------------------- PART 1 ----
   Who is affected.
   ---------------------------------------------------------------------------- */
PRINT '--- Accounts currently unable to log in because of the MFA flag ---';
SELECT  u.User_ID,
        u.User_Name,
        RoleName = r.RoleName,
        u.isMFA,
        u.ModifiedOn
FROM    dbo.tbl_User u
LEFT JOIN dbo.tbl_Roles r ON r.ID = u.Role_ID
WHERE   ISNULL(u.isMFA, 0) = 1
ORDER BY u.User_ID;

/* ---------------------------------------------------------------- PART 2 ----
   Clear it.
   ---------------------------------------------------------------------------- */
BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @Cleared INT;

    UPDATE dbo.tbl_User
    SET    isMFA      = 0,
           ModifiedOn = GETDATE()
    WHERE  ISNULL(isMFA, 0) = 1;

    SET @Cleared = @@ROWCOUNT;
    PRINT '--- Accounts unlocked: ' + CAST(@Cleared AS VARCHAR(10)) + ' ---';

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT '--- Rolled back. No changes were made. ---';
    THROW;
END CATCH
GO

/* -------------------------------------------------------------- VERIFY ----- */
PRINT '--- Accounts still flagged (expect zero) ---';
SELECT Remaining = COUNT(*) FROM dbo.tbl_User WHERE ISNULL(isMFA, 0) = 1;

PRINT '--- Sanity: every account can now reach the application ---';
SELECT  Blocked = SUM(CASE WHEN ISNULL(u.Flag, 1) = 0 THEN 1 ELSE 0 END),
        MfaLocked = SUM(CASE WHEN ISNULL(u.isMFA, 0) = 1 THEN 1 ELSE 0 END),
        Total = COUNT(*)
FROM    dbo.tbl_User u;
PRINT '    Blocked counts deliberate deactivations from script 002 and is expected to be non-zero.';
GO

/* =============================================================================
   WHEN A REAL SECOND FACTOR IS BUILT

   The choice is constrained by what exists today:

     TOTP / authenticator app
         Self-contained. Needs a key column on tbl_User, an enrolment screen that
         shows the secret and its otpauth:// URI, a verification step at login, and
         recovery codes. Recovery codes are not optional — without them a lost
         phone reproduces exactly the lockout this script is clearing.

     Emailed one-time code
         Blocked. Nothing in the solution sends email: the only SmtpClient is the
         SMTP test button, and DistributionController.SendSecureEmail logs a
         delivery it never performs (F-37). Outbound email has to be built first,
         at which point it also fixes employee form distribution.

     SMS
         Nothing in the solution sends SMS, and tbl_User.PhoneNumberConfirmed is
         never set, so numbers on file are unverified.

   Whichever is chosen, the challenge belongs in AccountController.Login where the
   flag is now merely logged, and the "Enable" options removed above should be
   restored at the same time.
   ============================================================================= */
