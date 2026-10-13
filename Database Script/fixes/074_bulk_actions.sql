/* =============================================================================
   074 - Bulk actions (idea #20)

   The list pages let staff pick several rows and act on them at once. Most
   bulk actions repeat the existing one-row action for each row (and so its
   checks); two need the database:

   1. sp_EmployeeCodes_SetLocked: lock or unlock the codes of several
      employees for a tax year (locked codes are left alone when codes are
      generated), each change written to EmployeeCodeAuditTrail as
      FieldName 'IsLocked' so the employee's history shows who did it.
   2. sp_AssignableStaff: the account managers or data analysts that
      employers can be assigned to (the assignment screens key them by
      their staff account, tbl_User.Ref_ID).

   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE dbo.sp_EmployeeCodes_SetLocked
    @EmployeeIds NVARCHAR(MAX),   -- JSON array of employee ids
    @Year        INT,
    @Locked      BIT,
    @UserId      INT,
    @UserName    NVARCHAR(256) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Changed TABLE (EmployeeId BIGINT, EmployerId BIGINT, WasLocked BIT);

    UPDATE C
    SET    C.IsLocked = @Locked
    OUTPUT inserted.employeeId, inserted.employerId, deleted.IsLocked INTO @Changed
    FROM   dbo.EmployeeCode C
    WHERE  C.filingYear = @Year
      AND  C.employeeId IN (SELECT TRY_CAST(value AS BIGINT) FROM OPENJSON(@EmployeeIds))
      AND  ISNULL(C.IsLocked, 0) <> @Locked;

    INSERT INTO dbo.EmployeeCodeAuditTrail (EmployeeId, EmployerId, FilingYear, MonthCode, AppliesToAllMonths, LineNumber,
                                            FieldName, OldValue, NewValue, ChangeSource, ChangeReason, SafeHarborApplied,
                                            ChangedByUserId, ChangedByUserName, IsSystemGenerated, ChangedOn)
    SELECT EmployeeId, EmployerId, @Year, 'ALL', 1, 0,
           'IsLocked', CASE WHEN ISNULL(WasLocked, 0) = 1 THEN '1' ELSE '0' END, CASE WHEN @Locked = 1 THEN '1' ELSE '0' END,
           'MANUAL', CASE WHEN @Locked = 1 THEN 'Codes locked (bulk)' ELSE 'Codes unlocked (bulk)' END, NULL,
           @UserId, @UserName, 0, GETDATE()
    FROM   @Changed;

    SELECT COUNT(*) AS Changed FROM @Changed;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_AssignableStaff
    @Side VARCHAR(2)   -- AM or DA
AS
BEGIN
    SET NOCOUNT ON;
    SELECT U.Ref_ID AS RefId, U.Role_ID AS RoleId, U.User_Name AS UserName,
           COALESCE(NULLIF(LTRIM(RTRIM(CONCAT(S.Staff_FName, ' ', S.Staff_LName))), ''), U.User_Name) AS Name, R.RoleName
    FROM   dbo.tbl_User U
    JOIN   dbo.tbl_Roles R ON R.ID = U.Role_ID
    LEFT   JOIN dbo.StaffAccounts S ON S.Staff_ID = U.Ref_ID
    WHERE  ISNULL(U.Flag, 1) = 1
      AND  U.Ref_ID IS NOT NULL
      AND  ((@Side = 'AM' AND R.RoleName IN ('Account Manager', 'Account Manager Supervisor'))
         OR (@Side = 'DA' AND R.RoleName IN ('Data Analyst', 'DA Supervisor')))
    ORDER  BY Name;
END
GO

PRINT '074: bulk actions (lock/unlock codes, assignable staff).';
GO
