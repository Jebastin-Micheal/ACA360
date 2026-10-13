/* =============================================================================
   037_disable_broken_logins.sql

   Logins whose link to their account record is broken. Nothing is deleted: a login
   is switched off with Flag = 0 (dbo.fn_UserLoginAllowed refuses it), and can be
   switched back on with Flag = 1.

   1. Duplicate staff logins (always applied).
      Two staff logins pointing at the same StaffAccounts row share one person's
      profile, dashboard scope and assignments. The login whose User_Name is NOT the
      staff record's e-mail is the stray one and is switched off; the login that
      matches the record is kept. (Found locally: james123@gmail.com on staff #2,
      whose record belongs to jamesmicheal007@gmail.com; it never signed in.)
      Logins without any staff link (Ref_ID NULL, e.g. the built-in admin) are not
      touched.

   2. Broker logins with no BrokerAccounts row (OFF by default).
      Such a login signs in with no name and an empty book of business. Locally this
      is broker@agency.com (Ref_ID 99992), which looks like a demo login and was
      used recently, so it is only switched off when @DisableOrphanBrokerLogins = 1.

   Each section prints the logins it changed (or would change). Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

DECLARE @DisableOrphanBrokerLogins BIT = 0;   -- set to 1 to also run section 2

BEGIN TRANSACTION;

-- 1. Duplicate staff logins.
DECLARE @Dupes TABLE (User_ID BIGINT, [User_Name] NVARCHAR(256), Ref_ID BIGINT, KeptLogin NVARCHAR(256));

INSERT @Dupes (User_ID, [User_Name], Ref_ID, KeptLogin)
SELECT u.User_ID, u.[User_Name], u.Ref_ID, keep.[User_Name]
FROM   dbo.tbl_User u
JOIN   dbo.StaffAccounts s ON s.Staff_ID = u.Ref_ID
JOIN   dbo.tbl_User keep
       ON  keep.Ref_ID = u.Ref_ID
       AND keep.User_ID <> u.User_ID
       AND ISNULL(keep.Role_ID, 0) NOT IN (10, 13)
       AND keep.[User_Name] = s.Staff_Email
WHERE  ISNULL(u.Role_ID, 0) NOT IN (10, 13)
  AND  u.Ref_ID IS NOT NULL
  AND  u.[User_Name] <> s.Staff_Email
  AND  ISNULL(u.Flag, 1) <> 0;

UPDATE u
SET    u.Flag = 0, u.ModifiedOn = GETDATE()
FROM   dbo.tbl_User u
JOIN   @Dupes d ON d.User_ID = u.User_ID;

SELECT 'Duplicate staff login switched off' AS [Change], User_ID, [User_Name], Ref_ID, KeptLogin
FROM   @Dupes;

-- 2. Broker logins with no broker record.
DECLARE @Orphans TABLE (User_ID BIGINT, [User_Name] NVARCHAR(256), Ref_ID BIGINT);

INSERT @Orphans (User_ID, [User_Name], Ref_ID)
SELECT u.User_ID, u.[User_Name], u.Ref_ID
FROM   dbo.tbl_User u
WHERE  u.Role_ID = 10
  AND  ISNULL(u.Flag, 1) <> 0
  AND  NOT EXISTS (SELECT 1 FROM dbo.BrokerAccounts b WHERE b.Broker_ID = u.Ref_ID);

IF @DisableOrphanBrokerLogins = 1
    UPDATE u
    SET    u.Flag = 0, u.ModifiedOn = GETDATE()
    FROM   dbo.tbl_User u
    JOIN   @Orphans o ON o.User_ID = u.User_ID;

SELECT CASE WHEN @DisableOrphanBrokerLogins = 1 THEN 'Broker login without broker record switched off'
            ELSE 'Broker login without broker record (not changed; set @DisableOrphanBrokerLogins = 1)' END AS [Change],
       User_ID, [User_Name], Ref_ID
FROM   @Orphans;

COMMIT TRANSACTION;
GO
