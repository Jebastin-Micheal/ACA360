/* =============================================================================
   044_employer_logins_from_tracker.sql

   Employer logins were linked two ways. Every employer-scope check, the File Upload
   list and Tracker's Portal login treat an Employer login's tbl_User.Ref_ID as the
   Employer it belongs to (Employer.id). User Accounts > Add, through
   sp_CreateUserAccounts, set Ref_ID to the id of a new EmployerAccounts profile row
   instead, so such a login reached whichever employer happened to share that number.

   Decided 2026-10-01: Employer logins are made only from Tracker > Employer > Portal
   login (which links them correctly). This script:
     1. sp_CreateUserAccounts refuses the Employer role (the web app refuses it too).
     2. sp_UserAccountsList lists every Employer login from the employer it points at
        (UserType 'EmployerPortal', named after the employer, read-only), instead of
        joining EmployerAccounts on Ref_ID, which matched profiles by coincidence and
        left Tracker-made logins out of the list.
   Otherwise both procedures are unchanged. Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE [dbo].[sp_UserAccountsList]
    @PageIndex INT = 1,
    @PageSize INT = 20,
    @Search VARCHAR(100) = NULL,
    @SortColumn INT = 0,
    @SortOrder VARCHAR(5) = 'asc',
    @RoleId int =0,
    @StatusId int =0
AS
BEGIN
    DECLARE @RecordCount INT;
    DECLARE @StartPage INT;
    DECLARE @EndPage INT;

    BEGIN TRY
        BEGIN TRANSACTION;
        SET NOCOUNT ON;

        -- Create temporary table to hold combined data
        CREATE TABLE #CombinedUsers
        (
            UserAccounts_ID INT,
            UserAccounts_FName NVARCHAR(100),
            UserAccounts_LName NVARCHAR(100),
            UserAccounts_Addr1 NVARCHAR(255),
            UserAccounts_Addr2 NVARCHAR(255),
            UserAccounts_City NVARCHAR(100),
            UserAccounts_State NVARCHAR(50),
            UserAccounts_Zip NVARCHAR(20),
            UserAccounts_Phone NVARCHAR(20),
            UserAccounts_Email NVARCHAR(100),
            UserAccounts_Status INT,
            CreatedOn DATETIME,
            CreatedBy INT,
            ModifiedOn DATETIME,
            ModifiedBy INT,
            UserType NVARCHAR(20),
            UserAccounts_Role INT,
            Temp_Password NVARCHAR(255),
            ProfilePicture NVARCHAR(MAX),
            UserAccounts_Role_Name NVARCHAR(100),
            UserAccounts_Full_Name NVARCHAR(201),
            AssginCount INT,
            IsMFA INT,
            LoginEnabled BIT
        );

        -------------------------------------------------------
        -- INSERT STAFF DATA
        -------------------------------------------------------
        INSERT INTO #CombinedUsers
        SELECT
            staf.Staff_ID AS UserAccounts_ID,
            staf.Staff_FName AS UserAccounts_FName,
            staf.Staff_LName AS UserAccounts_LName,
            staf.Staff_Addr1 AS UserAccounts_Addr1,
            staf.Staff_Addr2 AS UserAccounts_Addr2,
            staf.Staff_City AS UserAccounts_City,
            staf.Staff_State AS UserAccounts_State,
            staf.Staff_Zip AS UserAccounts_Zip,
            staf.Staff_Phone AS UserAccounts_Phone,
            staf.Staff_Email AS UserAccounts_Email,
            CASE WHEN ISNULL(us.Flag, 1) = 0 THEN 2 ELSE staf.Staff_Status END AS UserAccounts_Status,
            staf.CreatedOn,
            staf.CreatedBy,
            staf.ModifiedOn,
            staf.ModifiedBy,
            'Staff' AS UserType,
            us.Role_ID,
            us.Temp_Password,
            us.Profile_Picture,
            roles.RoleName,
            --us.[User_ID],
            COALESCE(staf.Staff_FName, '') + ' ' + COALESCE(staf.Staff_LName, '') AS UserAccounts_Full_Name,
            CASE
        WHEN us.Role_ID = 16 OR us.Role_ID = 6 -- Account Manager
        THEN (SELECT COUNT(*) FROM tbl_Portfolio_Assignment WHERE AM_UserID = us.User_ID And IsActive = 1)

        WHEN us.Role_ID = 11 OR us.Role_ID = 5 -- Data Analyst
        THEN (SELECT COUNT(*) FROM tbl_Operational_Assignment WHERE DA_UserID = us.User_ID And IsActive = 1)

    ELSE 0
        END AS ExtraCount,
        us.isMFA,
        CAST(CASE WHEN ISNULL(us.Flag, 1) = 0 THEN 0 ELSE 1 END AS BIT)
        FROM StaffAccounts staf
        INNER JOIN tbl_user us ON us.Ref_ID = staf.Staff_ID
        INNER JOIN tbl_roles roles ON roles.ID = us.Role_ID
        WHERE staf.Staff_Status != 4
          AND ISNULL(us.Role_ID, 0) NOT IN (10, 13);   -- each login only with its own account type, as sp_Login_access

        -------------------------------------------------------
        -- INSERT BROKER DATA
        -------------------------------------------------------
        INSERT INTO #CombinedUsers
        SELECT
            br.Broker_ID AS UserAccounts_ID,
           br.Broker_FName,
            br.Broker_LName,
            br.Broker_Addr1,
            br.Broker_Addr2,
            br.Broker_City,
            br.Broker_State,
            br.Broker_Zip,
            br.Broker_Phone,
            br.Broker_Email,
            CASE WHEN ISNULL(us.Flag, 1) = 0 THEN 2 ELSE br.Broker_Status END,
            br.CreatedOn,
            br.CreatedBy,
            br.ModifiedOn,
            br.ModifiedBy,
           'Broker' AS UserType,
            us.Role_ID,
            us.Temp_Password,
            us.Profile_Picture,
            roles.RoleName,
            COALESCE(br.Broker_FName, '') + ' ' + COALESCE(br.Broker_LName, '') AS UserAccounts_Full_Name,
            (SELECT COUNT(*) FROM tbl_Employer_Broker_Assignment WHERE Broker_UserID = us.User_ID And IsActive = 1) AS ExtraCount,
            us.isMFA,
        CAST(CASE WHEN ISNULL(us.Flag, 1) = 0 THEN 0 ELSE 1 END AS BIT)
        FROM BrokerAccounts br
        INNER JOIN tbl_user us ON us.Ref_ID = br.Broker_ID
        INNER JOIN tbl_roles roles ON roles.ID = us.Role_ID
        WHERE roles.ID = 10 and br.Broker_Status != 4;

        -------------------------------------------------------
        -- INSERT EMPLOYER LOGINS (044)
        -- An Employer login points at the employer it belongs to (Ref_ID = Employer.id) and
        -- is made and managed from Tracker > Employer > Portal login. Listed read-only, named
        -- after the employer; UserAccounts_ID is the employer id (the page links to Tracker).
        -------------------------------------------------------
        INSERT INTO #CombinedUsers
        SELECT
            e.id AS UserAccounts_ID,
            e.Name,
            '',
            NULL, NULL, NULL, NULL, NULL, NULL,
            us.[User_Name],
            CASE WHEN ISNULL(us.Flag, 1) = 0 THEN 2 WHEN ISNULL(e.IsDeleted, 0) = 1 THEN 3 ELSE 1 END,
            us.CreatedOn,
            NULL,
            us.ModifiedOn,
            NULL,
            'EmployerPortal' AS UserType,
            us.Role_ID,
            us.Temp_Password,
            us.Profile_Picture,
            roles.RoleName,
            e.Name AS UserAccounts_Full_Name,
            NULL AS ExtraCount,
            us.isMFA,
            CAST(CASE WHEN ISNULL(us.Flag, 1) = 0 THEN 0 ELSE 1 END AS BIT)
        FROM tbl_user us
        INNER JOIN dbo.Employer e ON e.id = us.Ref_ID
        INNER JOIN tbl_roles roles ON roles.ID = us.Role_ID
        WHERE us.Role_ID = 13;

        -------------------------------------------------------
        -- Get total record count with search filter
        -------------------------------------------------------
        SELECT @RecordCount = COUNT(*)
        FROM #CombinedUsers
        WHERE (@Search IS NULL
            OR UserAccounts_Full_Name LIKE '%' + @Search + '%'
               OR UserAccounts_Email LIKE '%' + @Search + '%')
               and(@RoleId =0 OR UserAccounts_Role = @RoleId)
               and(@StatusId =0 OR UserAccounts_Status = @StatusId);


        -- Calculate pagination values
        SET @StartPage = (@PageIndex - 1) * @PageSize + 1;
        SET @EndPage = @PageIndex * @PageSize;

        -------------------------------------------------------
        -- Main query with pagination and sorting
        -------------------------------------------------------
        SELECT
            UserAccounts_ID,
            UserAccounts_FName,
            UserAccounts_LName,
            UserAccounts_Addr1,
            UserAccounts_Addr2,
        UserAccounts_City,
            UserAccounts_State,
            UserAccounts_Zip,
            UserAccounts_Phone,
            UserAccounts_Email,
            UserAccounts_Status,
            CreatedOn,
            CreatedBy,
            ModifiedOn,
            ModifiedBy,
            UserType,
            UserAccounts_Role,
            Temp_Password,
            CASE
                WHEN ProfilePicture IS NOT NULL AND ProfilePicture != '' THEN ProfilePicture
                ELSE UPPER(
                    ISNULL(LEFT(NULLIF(UserAccounts_FName, ''), 1), '') +
                    ISNULL(LEFT(NULLIF(UserAccounts_LName, ''), 1), '')
                )
            END AS ProfilePicture,
            UserAccounts_Role_Name,
            UserAccounts_Full_Name,
            AssginCount,
            IsMFA,
            LoginEnabled
        FROM #CombinedUsers
        WHERE (@Search IS NULL
               OR UserAccounts_Full_Name LIKE '%' + @Search + '%'
               OR UserAccounts_Email LIKE '%' + @Search + '%')
               and(@RoleId =0 OR UserAccounts_Role = @RoleId)
               and(@StatusId =0 OR UserAccounts_Status = @StatusId)
        ORDER BY
            CASE WHEN (@SortColumn = 0 AND @SortOrder = 'asc') THEN UserAccounts_Full_Name END ASC,
            CASE WHEN (@SortColumn = 0 AND @SortOrder = 'desc') THEN UserAccounts_Full_Name END DESC,
            CASE WHEN (@SortColumn = 1 AND @SortOrder = 'asc') THEN UserAccounts_Email END ASC,
            CASE WHEN (@SortColumn = 1 AND @SortOrder = 'desc') THEN UserAccounts_Email END DESC,
            CASE WHEN (@SortColumn = 2 AND @SortOrder = 'asc') THEN CreatedOn END ASC,
            CASE WHEN (@SortColumn = 2 AND @SortOrder = 'desc') THEN CreatedOn END DESC
        OFFSET @PageSize * (@PageIndex - 1) ROWS
        FETCH NEXT @PageSize ROWS ONLY;

        -------------------------------------------------------
        -- Return pagination metadata
        -------------------------------------------------------
        SELECT
            @RecordCount as [RowCount],
            @StartPage as StartPage,
            CASE WHEN @EndPage > @RecordCount THEN @RecordCount ELSE @EndPage END as EndPage,
            @PageIndex as PageNumber,
            @RecordCount as TotalCount;




       -------------------------------------------------------
-- Summary: Count users by each role with sample details
-------------------------------------------------------
SELECT
    CU.UserAccounts_Role,
    R.RoleName,
    COUNT(*) AS TotalUsers,

    -- ALL users for this role
    STRING_AGG(CU.UserAccounts_Full_Name, ', ') AS AllUsers,

    -- ALL profile pictures for this role (using same initials logic)
    STRING_AGG(
        CASE
            WHEN CU.ProfilePicture IS NOT NULL AND CU.ProfilePicture != ''
                THEN CU.ProfilePicture
            ELSE UPPER(
                    ISNULL(LEFT(NULLIF(CU.UserAccounts_FName, ''), 1), '') +
                    ISNULL(LEFT(NULLIF(CU.UserAccounts_LName, ''), 1), '')
                 )
        END,
        ', '
    ) AS AllProfilePictures ,



  SUM(
    CASE
        WHEN CU.ProfilePicture IS NOT NULL AND CU.ProfilePicture != ''
            THEN 1   -- this is real picture
        WHEN CU.ProfilePicture IS NULL OR CU.ProfilePicture = ''
            THEN 1   -- this is initials avatar
        ELSE 0
    END
) AS TotalProfileCount




FROM #CombinedUsers CU
INNER JOIN tbl_roles R ON R.ID = CU.UserAccounts_Role
GROUP BY CU.UserAccounts_Role, R.RoleName
ORDER BY R.RoleName;


        -- Clean up temporary table
        DROP TABLE #CombinedUsers;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        -- Clean up temporary table if it exists
        IF OBJECT_ID('tempdb..#CombinedUsers') IS NOT NULL
            DROP TABLE #CombinedUsers;

        INSERT INTO dbo.DB_Errors
        VALUES
        (SUSER_SNAME(),
         ERROR_NUMBER(),
         ERROR_STATE(),
         ERROR_SEVERITY(),
         ERROR_LINE(),
         ERROR_PROCEDURE(),
         ERROR_MESSAGE(),
         GETDATE());

        IF (XACT_STATE()) = -1
            ROLLBACK TRANSACTION
        IF (XACT_STATE()) = 1
            COMMIT TRANSACTION

        THROW;
    END CATCH
END
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
    -- 044: an Employer login must point at its employer (Ref_ID = Employer.id). This
    -- procedure would point it at a new EmployerAccounts row instead, whose id the app then
    -- reads as an employer id. Employer logins are made from Tracker > Employer > Portal login.
    IF (@UserAccounts_Role = 13)
        THROW 50044, 'Employer logins are created from Tracker (Employer > Portal login), not from User Accounts.', 1;
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

PRINT '044: Employer logins are listed from their employer; User Accounts cannot create them.';
GO
