/* =============================================================================
   032_dashboard_system_stats.sql

   System figures for the Admin / SuperAdmin dashboard.

   The old dashboard read sp_GetAdvancedSuperAdminStats, which reports 0 users
   (35 active locally), counts deleted admins, and adds up every year's copy of each
   employer. The old page also showed hard-coded values ("All Systems Operational",
   "Data Completeness 78%"). This procedure returns only figures it can stand behind:

       ActiveUsers       logins allowed to sign in (dbo.fn_UserLoginAllowed, 028)
       ActiveAdmins      of those, SuperAdmin / Admin
       LockedOutUsers    active logins locked after failed sign-ins (LockoutEnd ahead)
       PendingPassword   active logins that still only have a temporary password
       SignedInNow       distinct users with a session active in the last 15 minutes
                         (tbl_AuthSessions, 023)
       FilesThisMonth    uploads this calendar month (not deleted)
       FilesLastMonth    uploads last calendar month

   sp_GetAdvancedSuperAdminStats is left in place, unused by the dashboard.

   Run after 023 and 028. Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetDashboardSystemStats
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @MonthStart DATETIME = DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1);
    DECLARE @LastMonthStart DATETIME = DATEADD(MONTH, -1, @MonthStart);

    DECLARE @Active TABLE (UserId BIGINT PRIMARY KEY, RoleName NVARCHAR(100), LockoutEnd DATETIMEOFFSET, HasOwnPassword BIT, HasTemp BIT);
    INSERT INTO @Active
    SELECT u.User_ID, r.RoleName, u.LockoutEnd,
           CASE WHEN u.User_Password LIKE '$2%' THEN 1 ELSE 0 END,
           CASE WHEN u.Temp_Password IS NOT NULL THEN 1 ELSE 0 END
    FROM   dbo.tbl_User u
    LEFT   JOIN dbo.tbl_Roles r ON r.ID = u.Role_ID
    CROSS  APPLY dbo.fn_UserLoginAllowed(u.User_ID) ok
    WHERE  ok.IsAllowed = 1;

    SELECT
        ActiveUsers     = (SELECT COUNT(*) FROM @Active),
        ActiveAdmins    = (SELECT COUNT(*) FROM @Active WHERE RoleName IN ('SuperAdmin', 'Admin')),
        LockedOutUsers  = (SELECT COUNT(*) FROM @Active WHERE LockoutEnd > SYSDATETIMEOFFSET()),
        PendingPassword = (SELECT COUNT(*) FROM @Active WHERE HasOwnPassword = 0 AND HasTemp = 1),
        SignedInNow     = (SELECT COUNT(DISTINCT s.Subject)
                           FROM   dbo.tbl_AuthSessions s
                           WHERE  s.RevokedAt IS NULL
                             AND  s.Subject LIKE N'user:%'
                             AND  s.LastActivityAt > DATEADD(MINUTE, -15, SYSUTCDATETIME())),
        FilesThisMonth  = (SELECT COUNT(*) FROM dbo.UploadedFileLog
                           WHERE ISNULL(IsDeleted, 0) = 0 AND UploadedAt >= @MonthStart),
        FilesLastMonth  = (SELECT COUNT(*) FROM dbo.UploadedFileLog
                           WHERE ISNULL(IsDeleted, 0) = 0 AND UploadedAt >= @LastMonthStart AND UploadedAt < @MonthStart);
END
GO
