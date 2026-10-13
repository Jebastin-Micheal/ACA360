/* =============================================================================
   040_disable_orphan_logins.sql

   Broker and Employer logins whose account record does not exist. Such a login
   signs in with no name and no employers, so it can do nothing useful and is
   only a way in. Nothing is deleted: the login is switched off with Flag = 0
   (dbo.fn_UserLoginAllowed refuses it; Flag = 1 switches it back on) and any
   session still open for it is revoked.

   How a login is linked (tbl_User.Ref_ID):
     Broker   (Role_ID 10) -> dbo.BrokerAccounts.Broker_ID
     Employer (Role_ID 13) -> dbo.Employer.id (the employer portal login; see
                              TrackerEmployerRepository.GetPortalUserByEmployerAsync)
   An Employer login whose employer exists is kept even when there is no
   EmployerAccounts row (e.g. marees2808@gmail.com -> 1013,
   test_employer1@gmail.com -> 1021).

   Found locally: employer@client.com (Ref_ID 99991) and broker@agency.com
   (Ref_ID 99992), demo logins created 2025-11-20.

   This replaces section 2 of 037 (brokers only, off by default). Prints the
   logins it changed. Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

DECLARE @Orphans TABLE (User_ID BIGINT, [User_Name] NVARCHAR(256), Role_ID INT, Ref_ID BIGINT);

INSERT @Orphans (User_ID, [User_Name], Role_ID, Ref_ID)
SELECT u.User_ID, u.[User_Name], u.Role_ID, u.Ref_ID
FROM   dbo.tbl_User u
WHERE  ISNULL(u.Flag, 1) <> 0
  AND  (   (u.Role_ID = 10 AND NOT EXISTS (SELECT 1 FROM dbo.BrokerAccounts b WHERE b.Broker_ID = u.Ref_ID))
        OR (u.Role_ID = 13 AND NOT EXISTS (SELECT 1 FROM dbo.Employer e WHERE e.id = u.Ref_ID)));

UPDATE u
SET    u.Flag = 0, u.ModifiedOn = GETDATE()
FROM   dbo.tbl_User u
JOIN   @Orphans o ON o.User_ID = u.User_ID;

-- Sign them out now rather than when their session expires.
UPDATE s
SET    s.RevokedAt = SYSUTCDATETIME()
FROM   dbo.tbl_AuthSessions s
JOIN   @Orphans o ON s.Subject = CONCAT('user:', o.User_ID)
WHERE  s.RevokedAt IS NULL;

SELECT 'Login without account record switched off' AS [Change],
       User_ID, [User_Name],
       CASE Role_ID WHEN 10 THEN 'Broker' ELSE 'Employer' END AS [Role], Ref_ID
FROM   @Orphans;

COMMIT TRANSACTION;
GO
