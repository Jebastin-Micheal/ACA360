/* =============================================================================
   023_server_side_auth_sessions.sql

   Server-side login sessions, so that logging out actually ends a session and
   an idle session expires even if the browser still holds its cookie.

   Before this, the login cookie was the only record of a session. Logout just
   asked the browser to delete it; a copy of the cookie kept working for up to
   30 minutes after logout, and "Remember me" kept it valid for 30 days.

   The web app now:
     - inserts a row here on every sign-in and puts its SessionId in the cookie
     - checks the row on every request (missing, revoked, or idle > 15 min
       => the user is sent to the login page)
     - revokes the row on every sign-out

   RUN THIS BEFORE DEPLOYING THE MATCHING C# CHANGE. The C# rejects any cookie
   without a valid session row, so every user currently logged in will be sent
   to the login page once, when the new build goes live. That is expected.

   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'dbo.tbl_AuthSessions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tbl_AuthSessions
    (
        SessionId       UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_tbl_AuthSessions PRIMARY KEY,
        -- "user:<User_ID>" for staff, "portal:<EmployeeId>" for the employee portal.
        Subject         NVARCHAR(100)    NOT NULL,
        CreatedAt       DATETIME2(3)     NOT NULL
            CONSTRAINT DF_tbl_AuthSessions_CreatedAt DEFAULT SYSUTCDATETIME(),
        LastActivityAt  DATETIME2(3)     NOT NULL
            CONSTRAINT DF_tbl_AuthSessions_LastActivityAt DEFAULT SYSUTCDATETIME(),
        RevokedAt       DATETIME2(3)     NULL,
        IpAddress       NVARCHAR(64)     NULL,
        UserAgent       NVARCHAR(512)    NULL
    );

    CREATE INDEX IX_tbl_AuthSessions_Subject   ON dbo.tbl_AuthSessions (Subject);
    CREATE INDEX IX_tbl_AuthSessions_CreatedAt ON dbo.tbl_AuthSessions (CreatedAt);
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_AuthSession_Create
    @SessionId  UNIQUEIDENTIFIER,
    @Subject    NVARCHAR(100),
    @IpAddress  NVARCHAR(64)  = NULL,
    @UserAgent  NVARCHAR(512) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.tbl_AuthSessions (SessionId, Subject, IpAddress, UserAgent)
    VALUES (@SessionId, @Subject, @IpAddress, LEFT(@UserAgent, 512));
END
GO

/* Returns one row, IsValid = 1 or 0.
   @Touch = 0 is for background polling (notifications, file status): it checks
   the session without counting as user activity, so an open tab that is only
   polling still times out. LastActivityAt is written at most once per
   @TouchIntervalSeconds, so a busy page is not a write per request. */
CREATE OR ALTER PROCEDURE dbo.sp_AuthSession_Validate
    @SessionId             UNIQUEIDENTIFIER,
    @IdleMinutes           INT,
    @Touch                 BIT,
    @TouchIntervalSeconds  INT = 60
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Now  DATETIME2(3) = SYSUTCDATETIME();
    DECLARE @Last DATETIME2(3);

    SELECT @Last = LastActivityAt
    FROM   dbo.tbl_AuthSessions
    WHERE  SessionId = @SessionId
      AND  RevokedAt IS NULL;

    IF @Last IS NULL
    BEGIN
        SELECT CAST(0 AS BIT) AS IsValid;
        RETURN;
    END

    IF @Last < DATEADD(MINUTE, -@IdleMinutes, @Now)
    BEGIN
        -- Close it for good, so a later request cannot revive an expired session.
        UPDATE dbo.tbl_AuthSessions
        SET    RevokedAt = @Now
        WHERE  SessionId = @SessionId
          AND  RevokedAt IS NULL;

        SELECT CAST(0 AS BIT) AS IsValid;
        RETURN;
    END

    IF @Touch = 1 AND @Last < DATEADD(SECOND, -@TouchIntervalSeconds, @Now)
    BEGIN
        UPDATE dbo.tbl_AuthSessions
        SET    LastActivityAt = @Now
        WHERE  SessionId = @SessionId
          AND  RevokedAt IS NULL;
    END

    SELECT CAST(1 AS BIT) AS IsValid;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_AuthSession_Revoke
    @SessionId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.tbl_AuthSessions
    SET    RevokedAt = SYSUTCDATETIME()
    WHERE  SessionId = @SessionId
      AND  RevokedAt IS NULL;
END
GO

-- Called daily by a Hangfire recurring job. Keeps 30 days for audit.
CREATE OR ALTER PROCEDURE dbo.sp_AuthSession_Purge
    @OlderThanDays INT = 30
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.tbl_AuthSessions
    WHERE  CreatedAt < DATEADD(DAY, -@OlderThanDays, SYSUTCDATETIME());
END
GO
