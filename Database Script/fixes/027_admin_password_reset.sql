/* =============================================================================
   027_admin_password_reset.sql

   Makes an admin password reset (User Accounts > Edit > Change Password) a real
   reset, and stops storing staff temporary passwords as plain text.

   sp_UpdateUserAccounts
     1. A new password now replaces the old one. Before, the SP only wrote
        Temp_Password and left User_Password / PasswordHash in place, so the
        user's old password kept working after the reset.
        Now it clears both, unlocks the account, and ends the user's open
        sessions (tbl_AuthSessions, from 023), so a reset also logs out anyone
        using the old password.
     2. @UserAccounts_Password NULL (or '') now means "leave the password
        alone". Before, the app re-sent the stored temp password on every
        edit to keep it.
     3. The password parameter is NVARCHAR(100). It was NVARCHAR(25), which
        would silently cut a BCrypt hash (60 chars) short.
     4. The login row is found by Ref_ID *and* account type. Before it was
        Ref_ID only, but broker, employer and staff IDs come from separate
        tables and overlap, so editing staff #5 also renamed / re-roled /
        reset the login of broker #5 and employer #5. Employer logins created
        from the Tracker (Role 13, Ref_ID = tracker employer) share the
        employer role, so when more than one row matches, the one whose
        User_Name is this account's current email wins; if that is still
        ambiguous the SP raises an error instead of guessing.

   sp_CreateUserAccounts
     - Password parameter widened to NVARCHAR(100) for the same reason.
     - On error it now rolls back. Before, it could COMMIT the half-done
       work (an account row with no login).

   The web app now BCrypt-hashes the password before calling either SP, and
   login accepts a hashed temp password. Temp passwords already stored in
   plain text keep working (login compares them as before) until they are
   used or replaced.

   Run after 023. Deploy together with the matching web app build: an older
   build sends plain-text passwords, which this script would store as-is.

   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE dbo.sp_CreateUserAccounts
    @UserAccounts_FName    NVARCHAR(100),
    @UserAccounts_LName    NVARCHAR(100),
    @UserAccounts_Addr1    NVARCHAR(100) = NULL,
    @UserAccounts_Addr2    NVARCHAR(50)  = NULL,
    @UserAccounts_City     NVARCHAR(50)  = NULL,
    @UserAccounts_State    BIGINT        = NULL,
    @UserAccounts_Zip      NVARCHAR(10)  = NULL,
    @UserAccounts_Phone    NVARCHAR(25)  = NULL,
    @UserAccounts_Email    NVARCHAR(100),
    @UserAccounts_Status   BIGINT        = NULL,
    @UserAccounts_Role     BIGINT        = NULL,
    @UserAccounts_Password NVARCHAR(100) = NULL,   -- BCrypt hash from the app
    @ProfilePicture        NVARCHAR(MAX) = NULL,
    @IsMFA                 INT           = NULL,
    @createdby             BIGINT        = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @UserAccounts_ID BIGINT;

    BEGIN TRY
        BEGIN TRANSACTION;

        IF (@UserAccounts_Role = 10)
        BEGIN
            INSERT INTO dbo.BrokerAccounts (
                Broker_FName, Broker_LName, Broker_Addr1, Broker_Addr2,
                Broker_City, Broker_State, Broker_Zip, Broker_Phone,
                Broker_Email, Broker_Status, createdon, createdby)
            VALUES (
                @UserAccounts_FName, @UserAccounts_LName, @UserAccounts_Addr1, @UserAccounts_Addr2,
                @UserAccounts_City, @UserAccounts_State, @UserAccounts_Zip, @UserAccounts_Phone,
                @UserAccounts_Email, @UserAccounts_Status, GETDATE(), @createdby);
        END
        ELSE IF (@UserAccounts_Role = 13)
        BEGIN
            INSERT INTO dbo.EmployerAccounts (
                Employer_FName, Employer_LName, Employer_Addr1, Employer_Addr2,
                Employer_City, Employer_State, Employer_Zip, Employer_Phone,
                Employer_Email, Employer_Status, createdon, createdby)
            VALUES (
                @UserAccounts_FName, @UserAccounts_LName, @UserAccounts_Addr1, @UserAccounts_Addr2,
                @UserAccounts_City, @UserAccounts_State, @UserAccounts_Zip, @UserAccounts_Phone,
                @UserAccounts_Email, @UserAccounts_Status, GETDATE(), @createdby);
        END
        ELSE
        BEGIN
            INSERT INTO dbo.StaffAccounts (
                Staff_FName, Staff_LName, Staff_Addr1, Staff_Addr2,
                Staff_City, Staff_State, Staff_Zip, Staff_Phone,
                Staff_Email, Staff_Status, createdon, createdby)
            VALUES (
                @UserAccounts_FName, @UserAccounts_LName, @UserAccounts_Addr1, @UserAccounts_Addr2,
                @UserAccounts_City, @UserAccounts_State, @UserAccounts_Zip, @UserAccounts_Phone,
                @UserAccounts_Email, @UserAccounts_Status, GETDATE(), @createdby);
        END

        SET @UserAccounts_ID = SCOPE_IDENTITY();

        INSERT INTO dbo.tbl_User ([User_Name], Temp_Password, Ref_ID, Role_ID, Profile_Picture, isMFA)
        VALUES (@UserAccounts_Email, @UserAccounts_Password, @UserAccounts_ID, @UserAccounts_Role, @ProfilePicture, @IsMFA);

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;

        INSERT INTO dbo.DB_Errors
        VALUES (SUSER_SNAME(), ERROR_NUMBER(), ERROR_STATE(), ERROR_SEVERITY(),
                ERROR_LINE(), ERROR_PROCEDURE(), ERROR_MESSAGE(), GETDATE());

        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_UpdateUserAccounts
    @UserAccounts_ID       BIGINT,
    @UserAccounts_FName    NVARCHAR(100),
    @UserAccounts_LName    NVARCHAR(100),
    @UserAccounts_Addr1    NVARCHAR(100) = NULL,
    @UserAccounts_Addr2    NVARCHAR(50)  = NULL,
    @UserAccounts_City     NVARCHAR(50)  = NULL,
    @UserAccounts_State    BIGINT        = NULL,
    @UserAccounts_Zip      VARCHAR(10)   = NULL,
    @UserAccounts_Phone    VARCHAR(25)   = NULL,
    @UserAccounts_Email    VARCHAR(100),
    @UserAccounts_Status   BIGINT        = NULL,
    @UserAccounts_Role     BIGINT        = NULL,
    @UserAccounts_Password NVARCHAR(100) = NULL,   -- BCrypt hash from the app; NULL = keep the current password
    @ProfilePicture        NVARCHAR(MAX) = NULL,
    @IsMFA                 INT           = NULL,
    @modifiedby            BIGINT        = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @OldEmail NVARCHAR(100),
            @UserId   BIGINT,
            @Matches  INT,
            @IsBroker   BIT = CASE WHEN @UserAccounts_Role = 10 THEN 1 ELSE 0 END,
            @IsEmployer BIT = CASE WHEN @UserAccounts_Role = 13 THEN 1 ELSE 0 END;

    BEGIN TRY
        BEGIN TRANSACTION;

        -----------------------------------------------------------------
        -- 1. Update the account row (email captured first to find the login)
        -----------------------------------------------------------------
        IF (@IsBroker = 1)
        BEGIN
            SELECT @OldEmail = Broker_Email FROM dbo.BrokerAccounts WHERE Broker_ID = @UserAccounts_ID;

            UPDATE dbo.BrokerAccounts
            SET    Broker_FName  = @UserAccounts_FName,
                   Broker_LName  = @UserAccounts_LName,
                   Broker_Addr1  = @UserAccounts_Addr1,
                   Broker_Addr2  = @UserAccounts_Addr2,
                   Broker_City   = @UserAccounts_City,
                   Broker_State  = @UserAccounts_State,
                   Broker_Zip    = @UserAccounts_Zip,
                   Broker_Phone  = @UserAccounts_Phone,
                   Broker_Email  = @UserAccounts_Email,
                   Broker_Status = @UserAccounts_Status,
                   modifiedon    = GETDATE(),
                   modifiedby    = @modifiedby
            WHERE  Broker_ID = @UserAccounts_ID;
        END
        ELSE IF (@IsEmployer = 1)
        BEGIN
            SELECT @OldEmail = Employer_Email FROM dbo.EmployerAccounts WHERE Employer_ID = @UserAccounts_ID;

            UPDATE dbo.EmployerAccounts
            SET    Employer_FName  = @UserAccounts_FName,
                   Employer_LName  = @UserAccounts_LName,
                   Employer_Addr1  = @UserAccounts_Addr1,
                   Employer_Addr2  = @UserAccounts_Addr2,
                   Employer_City   = @UserAccounts_City,
                   Employer_State  = @UserAccounts_State,
                   Employer_Zip    = @UserAccounts_Zip,
                   Employer_Phone  = @UserAccounts_Phone,
                   Employer_Email  = @UserAccounts_Email,
                   Employer_Status = @UserAccounts_Status,
                   modifiedon      = GETDATE(),
                   modifiedby      = @modifiedby
            WHERE  Employer_ID = @UserAccounts_ID;
        END
        ELSE
        BEGIN
            SELECT @OldEmail = Staff_Email FROM dbo.StaffAccounts WHERE Staff_ID = @UserAccounts_ID;

            UPDATE dbo.StaffAccounts
            SET    Staff_FName  = @UserAccounts_FName,
                   Staff_LName  = @UserAccounts_LName,
                   Staff_Addr1  = @UserAccounts_Addr1,
                   Staff_Addr2  = @UserAccounts_Addr2,
                   Staff_City   = @UserAccounts_City,
                   Staff_State  = @UserAccounts_State,
                   Staff_Zip    = @UserAccounts_Zip,
                   Staff_Phone  = @UserAccounts_Phone,
                   Staff_Email  = @UserAccounts_Email,
                   Staff_Status = @UserAccounts_Status,
                   modifiedon   = GETDATE(),
                   modifiedby   = @modifiedby
            WHERE  Staff_ID = @UserAccounts_ID;
        END

        -----------------------------------------------------------------
        -- 2. Find this account's login row (same Ref_ID + same account type)
        -----------------------------------------------------------------
        SELECT @Matches = COUNT(*), @UserId = MIN(u.User_ID)
        FROM   dbo.tbl_User u
        WHERE  u.Ref_ID = @UserAccounts_ID
          AND  (   (@IsBroker = 1   AND u.Role_ID = 10)
                OR (@IsEmployer = 1 AND u.Role_ID = 13)
                OR (@IsBroker = 0 AND @IsEmployer = 0 AND ISNULL(u.Role_ID, 0) NOT IN (10, 13)));

        IF (@Matches > 1)
        BEGIN
            SELECT @Matches = COUNT(*), @UserId = MIN(u.User_ID)
            FROM   dbo.tbl_User u
            WHERE  u.Ref_ID = @UserAccounts_ID
              AND  u.[User_Name] = @OldEmail
              AND  (   (@IsBroker = 1   AND u.Role_ID = 10)
                    OR (@IsEmployer = 1 AND u.Role_ID = 13)
                    OR (@IsBroker = 0 AND @IsEmployer = 0 AND ISNULL(u.Role_ID, 0) NOT IN (10, 13)));

            IF (@Matches <> 1)
                THROW 50027, 'sp_UpdateUserAccounts: more than one login matches this account and none is uniquely tied to its email. Login not updated.', 1;
        END

        -----------------------------------------------------------------
        -- 3. Update the login (no login row = nothing to update, as before)
        -----------------------------------------------------------------
        IF (@UserId IS NOT NULL)
        BEGIN
            UPDATE dbo.tbl_User
            SET    [User_Name]     = @UserAccounts_Email,
                   Role_ID         = @UserAccounts_Role,
                   isMFA           = @IsMFA,
                   Profile_Picture = COALESCE(@ProfilePicture, Profile_Picture),
                   ModifiedOn      = GETDATE()
            WHERE  User_ID = @UserId;

            -- Admin reset: the new temp password replaces the old password,
            -- the account is unlocked, and open sessions are ended.
            IF (NULLIF(@UserAccounts_Password, N'') IS NOT NULL)
            BEGIN
                UPDATE dbo.tbl_User
                SET    Temp_Password     = @UserAccounts_Password,
                       User_Password     = NULL,
                       PasswordHash      = NULL,
                       AccessFailedCount = 0,
                       LockoutEnd        = NULL
                WHERE  User_ID = @UserId;

                IF OBJECT_ID(N'dbo.tbl_AuthSessions', N'U') IS NOT NULL
                    UPDATE dbo.tbl_AuthSessions
                    SET    RevokedAt = SYSUTCDATETIME()
                    WHERE  Subject = CONCAT(N'user:', @UserId)
                      AND  RevokedAt IS NULL;
            END
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;

        INSERT INTO dbo.DB_Errors
        VALUES (SUSER_SNAME(), ERROR_NUMBER(), ERROR_STATE(), ERROR_SEVERITY(),
                ERROR_LINE(), ERROR_PROCEDURE(), ERROR_MESSAGE(), GETDATE());

        THROW;
    END CATCH
END
GO
