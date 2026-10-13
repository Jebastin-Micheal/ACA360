/* =============================================================================
   002_F-02_deactivated_users_can_still_log_in.sql

   FIXES   F-02 — deactivating a user does not stop them logging in.
           Also fixes a second bug with the same root cause (see PART 4).

   WHY     dbo.sp_DeactivateUser disables access by setting tbl_User.Flag = 0 and
           marking the profile row Inactive. But sp_Login_access has no predicate
           on Flag, and AccountService.LoginAsync reads the column into
           UserModel.Flag and never tests it — that assignment is the only
           reference to the property in the entire C# solution.

           Deactivation is therefore cosmetic. A terminated employee, a removed
           broker or a revoked client contact keeps working credentials, full
           permissions and their prior employer assignments indefinitely.

   CAREFUL — READ BEFORE RUNNING

           Flag is THREE-valued in the live data, not two:

               1     active   (legacy seeded accounts)
               NULL  active   (every account created since — sp_CreateUserAccounts
                               inserts without naming the column, and there is no
                               DEFAULT on it)
               0     DEACTIVATED

           In the 20 Aug snapshot that is 16 rows at 1 and 21 rows at NULL.

           So the obvious predicate "Flag = 1" is WRONG and would lock out every
           account created through the current path. Everything below treats NULL
           as active: ISNULL(Flag, 1) <> 0.

   WHAT    1. Backfills Flag = 1 where it is NULL. No behaviour change — those
              rows are already treated as active — but it removes the ambiguity.
           2. Adds a DEFAULT of 1 on tbl_User.Flag so new inserts stop producing
              NULLs. This is done with a constraint rather than by rewriting
              sp_CreateUserAccounts, which has several insert paths.
           3. ALTERs sp_Login_access to reject Flag = 0 on both result sets.
           4. ALTERs sp_GetViewAsBrokerTargets, which already filtered on
              "Flag = 1" and so has been silently hiding every broker created
              through the current path from the View As picker.

   DRIFT WARNING
           PARTs 3 and 4 replace whole procedure bodies, reproduced from the
           20 Aug snapshot with only the WHERE clause changed. If the live
           definitions have moved on since that snapshot, diff them first —
           running this would revert the difference.

   SAFE TO RE-RUN
           Yes. The backfill is scoped to NULLs, the constraint is guarded by an
           existence check, and ALTER PROCEDURE is naturally idempotent.
   ============================================================================= */

USE [db_ACA360];
GO

SET NOCOUNT ON;
GO

/* ---------------------------------------------------------------- PART 1 ----
   Backfill. Reported before and after so the row count is visible.
   ---------------------------------------------------------------------------- */
PRINT '--- PART 1: tbl_User.Flag distribution BEFORE ---';
SELECT  FlagValue = CASE WHEN Flag IS NULL THEN '(null)' ELSE CAST(Flag AS VARCHAR(10)) END,
        Meaning   = CASE WHEN Flag IS NULL THEN 'active (implicit)'
                         WHEN Flag = 0     THEN 'DEACTIVATED'
                         ELSE                   'active' END,
        Accounts  = COUNT(*)
FROM    dbo.tbl_User
GROUP BY Flag
ORDER BY Flag;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @Backfilled INT;

    UPDATE dbo.tbl_User
    SET    Flag = 1
    WHERE  Flag IS NULL;

    SET @Backfilled = @@ROWCOUNT;
    PRINT '--- Accounts backfilled from NULL to 1: ' + CAST(@Backfilled AS VARCHAR(10)) + ' ---';

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT '--- PART 1 rolled back. ---';
    THROW;
END CATCH
GO

/* ---------------------------------------------------------------- PART 2 ----
   Default, so new rows stop arriving as NULL. Guarded: re-running is a no-op.
   ---------------------------------------------------------------------------- */
IF NOT EXISTS (
    SELECT 1
    FROM   sys.default_constraints dc
    JOIN   sys.columns c ON c.object_id = dc.parent_object_id
                        AND c.column_id = dc.parent_column_id
    WHERE  dc.parent_object_id = OBJECT_ID(N'dbo.tbl_User')
      AND  c.name = 'Flag')
BEGIN
    ALTER TABLE dbo.tbl_User
        ADD CONSTRAINT DF_tbl_User_Flag DEFAULT (1) FOR Flag;
    PRINT '--- PART 2: DEFAULT (1) added on tbl_User.Flag ---';
END
ELSE
    PRINT '--- PART 2: a default already exists on tbl_User.Flag, left alone ---';
GO

/* ---------------------------------------------------------------- PART 3 ----
   sp_Login_access — reject deactivated accounts.

   The account is filtered out rather than reported as disabled, so the caller
   sees the same "Invalid login attempt." as any unknown user and account state
   is not disclosed. AccountService.LoginAsync already handles a null user.
   ---------------------------------------------------------------------------- */
ALTER   PROCEDURE [dbo].[sp_Login_access] -- 'accounting@aca360.com'  
(                        
    @uname NVARCHAR(100)        
)                
AS            
BEGIN        
    SET NOCOUNT ON;        
    
    -- =========================================================  
    -- 1st ResultSet: User Credentials & Profile Data  
    -- =========================================================  
    SELECT           
       u.User_ID,        
        u.User_Name as [User_Name] ,          
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
        u.LockoutEnd,           -- ADD    
        u.AccessFailedCount,    -- ADD    
        u.LockoutEnabled,       -- ADD    
        r.RoleName AS Role_Name ,    
        LTRIM(RTRIM(  
            COALESCE(s.Staff_FName, e.Employer_FName, b.Broker_FName, '') + ' ' +   
            COALESCE(s.Staff_LName, e.Employer_LName, b.Broker_LName, '')  
        )) AS [Profile_Name]
          
    FROM tbl_User u            
    LEFT JOIN tbl_Roles r ON r.ID = u.Role_ID    
      
    -- Safe Joins: Must match both the Ref_ID and the specific Role  
    LEFT JOIN StaffAccounts s ON u.Ref_ID = s.Staff_ID AND r.ID = u.Role_ID        
    LEFT JOIN EmployerAccounts e ON u.Ref_ID = e.Employer_ID AND r.ID = u.Role_ID  
    LEFT JOIN BrokerAccounts b ON u.Ref_ID = b.Broker_ID AND r.ID = u.Role_ID      
          
    WHERE u.User_Name = @uname
      AND ISNULL(u.Flag, 1) <> 0;   -- F-02: deactivated accounts cannot authenticate
    
    -- =========================================================  
    -- 2nd ResultSet: Menus and Action Permissions  
    -- =========================================================  
    SELECT           
        m.Id AS MenuId,          
        m.MenuName,          
        m.Controller,          
        at.ActionName AS Action,          
        'Enabled' AS PermissionType          
    FROM tbl_User u          
    JOIN tbl_AccessPermissions ap ON ap.RoleId = u.Role_ID AND ap.IsEnabled = 1          
    JOIN tbl_Menus m ON m.Id = ap.MenuId AND m.IsActive = 1 AND m.Flag = 0          
    JOIN tbl_ActionTypes at ON at.Id = ap.ActionID          
    WHERE u.User_Name = @uname
      AND ISNULL(u.Flag, 1) <> 0;   -- F-02: deactivated accounts cannot authenticate
END    
GO

/* ---------------------------------------------------------------- PART 4 ----
   sp_GetViewAsBrokerTargets — same root cause, separate symptom.

   This already read "u.Flag = 1", so brokers created through the current path
   (Flag NULL) never appeared in the View As picker. After PART 1 the plain
   equality would work, but ISNULL is kept so a NULL arriving from any insert
   path that bypasses the new default still resolves to active.
   ---------------------------------------------------------------------------- */
ALTER PROCEDURE [dbo].[sp_GetViewAsBrokerTargets]
    @ActorUserId INT
AS
BEGIN
    SET NOCOUNT ON;

    /* The caller's role is re-checked here rather than trusted from the
       application layer: a Broker or Employer gets an empty set regardless of
       what the C# passes in. */
    DECLARE @ActorRole NVARCHAR(100);
    SELECT @ActorRole = r.RoleName
    FROM   dbo.tbl_User u
    JOIN   dbo.tbl_Roles r ON r.ID = u.Role_ID
    WHERE  u.User_ID = @ActorUserId;

    IF @ActorRole IS NULL OR @ActorRole IN ('Broker', 'Employer')
    BEGIN
        SELECT CAST(0 AS INT) AS UserId,
               CAST('' AS NVARCHAR(500)) AS DisplayName,
               CAST(0 AS INT) AS EmployerCount
        WHERE  1 = 0;
        RETURN;
    END

    SELECT
        UserId        = CAST(u.User_ID AS INT),
        DisplayName   = u.User_Name,
        EmployerCount = ISNULL(a.Cnt, 0)
    FROM   dbo.tbl_User u
    JOIN   dbo.tbl_Roles r ON r.ID = u.Role_ID
    OUTER APPLY (
        SELECT Cnt = COUNT(DISTINCT ba.EmployerID)
        FROM   dbo.tbl_Employer_Broker_Assignment ba
        WHERE  ba.Broker_UserID = u.User_ID
          AND  ba.IsActive = 1
          AND  ba.UnassignedDate IS NULL
    ) a
    WHERE  r.RoleName = 'Broker'
      AND  ISNULL(u.Flag, 1) <> 0   -- F-02: NULL means active (created pre-default)
    ORDER BY u.User_Name;
END
GO

/* -------------------------------------------------------------- VERIFY ----- */
PRINT '--- tbl_User.Flag distribution AFTER ---';
SELECT  FlagValue = CASE WHEN Flag IS NULL THEN '(null)' ELSE CAST(Flag AS VARCHAR(10)) END,
        Meaning   = CASE WHEN Flag IS NULL THEN 'active (implicit)'
                         WHEN Flag = 0     THEN 'DEACTIVATED'
                         ELSE                   'active' END,
        Accounts  = COUNT(*)
FROM    dbo.tbl_User
GROUP BY Flag
ORDER BY Flag;

PRINT '--- Accounts that can no longer log in (expect only genuine deactivations) ---';
SELECT  u.User_ID, u.User_Name, r.RoleName, u.Flag, u.ModifiedOn
FROM    dbo.tbl_User u
LEFT JOIN dbo.tbl_Roles r ON r.ID = u.Role_ID
WHERE   ISNULL(u.Flag, 1) = 0
ORDER BY u.User_Name;

PRINT '--- Brokers now visible in the View As picker ---';
SELECT  Brokers = COUNT(*)
FROM    dbo.tbl_User u
JOIN    dbo.tbl_Roles r ON r.ID = u.Role_ID
WHERE   r.RoleName = 'Broker' AND ISNULL(u.Flag, 1) <> 0;
GO
