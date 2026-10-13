/* =============================================================================
   028_block_deleted_user_login.sql

   Deleted, inactive and terminated users can no longer log in, and any session
   they already have ends on their next request.

   Before this, login only refused tbl_User.Flag = 0, which nothing in User
   Accounts ever sets:
     - Delete (sp_DeleteUserAccounts) sets tbl_User.Status = 4 and the account's
       *_Status = 4, so a deleted user could still log in.
     - Setting a user to Inactive (2) or Terminated (3) only changes the
       account row (Staff/Employer/Broker_Status), so they could too.

   dbo.fn_UserLoginAllowed holds the rule in one place:
     - Flag = 0                                   -> refused (as before)
     - account row status Inactive / Terminated / Deleted (2, 3, 4) -> refused
     - no account row and tbl_User.Status = '4'   -> refused
   The account row is the Staff/Employer/Broker row with the login's Ref_ID,
   the same account type, and the login's email. When there is one, its status
   decides, so an admin setting a deleted user back to Active lets them in again.
   Logins with no such row (Tracker employer logins, older rows) keep working
   unless Flag = 0 or Status = '4'.

   Used by:
     - sp_Login_access: refused users get the same "Invalid login attempt." as
       an unknown user.
     - sp_AuthSession_Validate (from 023): runs on every request, so an open
       session of a user who is deleted or deactivated is ended at once.

   sp_Login_access also gets its name lookup fixed: it joined the account tables
   by Ref_ID only, so an employer login sharing an ID with a staff member showed
   the staff member's name in the header.

   Run after 023. No web app change needed. Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER FUNCTION dbo.fn_UserLoginAllowed (@UserId BIGINT)
RETURNS TABLE
AS
RETURN
    SELECT CAST(CASE
               WHEN ISNULL(u.Flag, 1) = 0                            THEN 0
               WHEN a.HasAccount = 1 AND a.AccountStatus IN (2, 3, 4) THEN 0
               WHEN a.HasAccount = 0 AND LTRIM(RTRIM(ISNULL(u.[Status], ''))) = '4' THEN 0
               ELSE 1
           END AS BIT) AS IsAllowed
    FROM dbo.tbl_User u
    OUTER APPLY (
        SELECT COUNT(*) AS HasAccount, MAX(x.AccountStatus) AS AccountStatus
        FROM (
            SELECT b.Broker_Status AS AccountStatus
            FROM   dbo.BrokerAccounts b
            WHERE  u.Role_ID = 10 AND b.Broker_ID = u.Ref_ID AND b.Broker_Email = u.[User_Name]
            UNION ALL
            SELECT e.Employer_Status
            FROM   dbo.EmployerAccounts e
            WHERE  u.Role_ID = 13 AND e.Employer_ID = u.Ref_ID AND e.Employer_Email = u.[User_Name]
            UNION ALL
            SELECT s.Staff_Status
            FROM   dbo.StaffAccounts s
            WHERE  ISNULL(u.Role_ID, 0) NOT IN (10, 13) AND s.Staff_ID = u.Ref_ID AND s.Staff_Email = u.[User_Name]
        ) x
    ) a
    WHERE u.User_ID = @UserId;
GO

CREATE OR ALTER PROCEDURE dbo.sp_Login_access
(
    @uname NVARCHAR(100)
)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @UserId BIGINT;

    -- Only a login that is allowed to sign in (fn_UserLoginAllowed) is returned;
    -- a refused one looks exactly like an unknown user to the caller.
    SELECT @UserId = u.User_ID
    FROM   dbo.tbl_User u
    CROSS APPLY dbo.fn_UserLoginAllowed(u.User_ID) ok
    WHERE  u.[User_Name] = @uname
      AND  ok.IsAllowed = 1;

    -- =========================================================
    -- 1st ResultSet: User Credentials & Profile Data
    -- =========================================================
    SELECT
        u.User_ID,
        u.[User_Name] AS [User_Name],
        u.User_Password,
        u.Temp_Password,
        u.Role_ID,
        u.Flag,
        u.Ref_ID,
        u.Profile_Picture,
        u.isMFA,
        u.User_LandingPage,
        u.CreatedOn,
        u.ModifiedOn,
        u.PasswordHash,
        u.LockoutEnd,
        u.AccessFailedCount,
        u.LockoutEnabled,
        r.RoleName AS Role_Name,
        LTRIM(RTRIM(
            COALESCE(s.Staff_FName, e.Employer_FName, b.Broker_FName, '') + ' ' +
            COALESCE(s.Staff_LName, e.Employer_LName, b.Broker_LName, '')
        )) AS [Profile_Name]
    FROM dbo.tbl_User u
    LEFT JOIN dbo.tbl_Roles r ON r.ID = u.Role_ID
    -- Each login is joined only to the account table for its own type.
    LEFT JOIN dbo.BrokerAccounts   b ON u.Role_ID = 10 AND b.Broker_ID = u.Ref_ID
    LEFT JOIN dbo.EmployerAccounts e ON u.Role_ID = 13 AND e.Employer_ID = u.Ref_ID
    LEFT JOIN dbo.StaffAccounts    s ON ISNULL(u.Role_ID, 0) NOT IN (10, 13) AND s.Staff_ID = u.Ref_ID
    WHERE u.User_ID = @UserId;

    -- =========================================================
    -- 2nd ResultSet: Menus and Action Permissions
    -- =========================================================
    SELECT
        m.Id AS MenuId,
        m.MenuName,
        m.Controller,
        at.ActionName AS Action,
        'Enabled' AS PermissionType
    FROM dbo.tbl_User u
    JOIN dbo.tbl_AccessPermissions ap ON ap.RoleId = u.Role_ID AND ap.IsEnabled = 1
    JOIN dbo.tbl_Menus m ON m.Id = ap.MenuId AND m.IsActive = 1 AND m.Flag = 0
    JOIN dbo.tbl_ActionTypes at ON at.Id = ap.ActionID
    WHERE u.User_ID = @UserId;
END
GO

/* Same as 023, plus: a staff session ("user:<User_ID>") whose user is no longer
   allowed to sign in is revoked and reported invalid. */
CREATE OR ALTER PROCEDURE dbo.sp_AuthSession_Validate
    @SessionId             UNIQUEIDENTIFIER,
    @IdleMinutes           INT,
    @Touch                 BIT,
    @TouchIntervalSeconds  INT = 60
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Now     DATETIME2(3) = SYSUTCDATETIME();
    DECLARE @Last    DATETIME2(3);
    DECLARE @Subject NVARCHAR(100);

    SELECT @Last = LastActivityAt, @Subject = Subject
    FROM   dbo.tbl_AuthSessions
    WHERE  SessionId = @SessionId
      AND  RevokedAt IS NULL;

    IF @Last IS NULL
    BEGIN
        SELECT CAST(0 AS BIT) AS IsValid;
        RETURN;
    END

    IF @Last < DATEADD(MINUTE, -@IdleMinutes, @Now)
       OR (@Subject LIKE N'user:%'
           AND NOT EXISTS (SELECT 1
                           FROM   dbo.fn_UserLoginAllowed(TRY_CAST(SUBSTRING(@Subject, 6, 30) AS BIGINT))
                           WHERE  IsAllowed = 1))
    BEGIN
        -- Close it for good, so a later request cannot revive it.
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
