/* =============================================================================
   075 - "My sign-in sessions" on Account Settings > Security

   Lets a user see where they are signed in (the server-side sessions of
   script 023) and sign out everywhere except the browser they are using.

   1. sp_AuthSession_ListForSubject: the subject's sessions that are still
      live (not revoked, used within the idle timeout).
   2. sp_AuthSession_RevokeOthers: revokes every live session of the
      subject except the one given.

   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE dbo.sp_AuthSession_ListForSubject
    @Subject     NVARCHAR(100),
    @IdleMinutes INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT SessionId, CreatedAt, LastActivityAt, IpAddress, UserAgent
    FROM   dbo.tbl_AuthSessions
    WHERE  Subject = @Subject
      AND  RevokedAt IS NULL
      AND  LastActivityAt >= DATEADD(MINUTE, -@IdleMinutes, SYSUTCDATETIME())
    ORDER  BY LastActivityAt DESC;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_AuthSession_RevokeOthers
    @Subject   NVARCHAR(100),
    @KeepSessionId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.tbl_AuthSessions
    SET    RevokedAt = SYSUTCDATETIME()
    WHERE  Subject = @Subject AND RevokedAt IS NULL AND SessionId <> @KeepSessionId;
    SELECT @@ROWCOUNT AS Revoked;
END
GO

PRINT '075: my sign-in sessions (list, sign out everywhere else).';
GO
