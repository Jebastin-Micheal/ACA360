/* =============================================================================
   052_employee_page_indexes.sql

   The indexes the Employee page's queries rely on (sp_EmployeeList_Filter for the
   list and the Prev/Next list, sp_Employee_QuickStats for the badge counts, the
   flag grid and the import analysis). They were added straight to the developer
   database and are in no script, and the 24 Aug 2026 database copy has only some
   of them, so a database built from it scans whole tables on every open of the
   Employee page.

   Creates each one that is missing, with the definition the developer database
   has. Existing indexes are left alone. Safe to re-run. On a large database each
   new index takes a moment to build.
   ============================================================================= */
SET NOCOUNT ON;
GO

DECLARE @Index TABLE (Seq INT IDENTITY, TableName SYSNAME, IndexName SYSNAME, KeyColumns NVARCHAR(400), IncludeColumns NVARCHAR(400));
INSERT @Index (TableName, IndexName, KeyColumns, IncludeColumns) VALUES
    (N'Employee', N'IX_ACA360_Employee_Core_Filters', N'[IsDeleted], [firstName], [lastName], [ssn]', N'[city], [countryId], [email], [Flag], [fullName], [stateId], [Status], [zip]'),
    (N'Employee', N'IX_ImportAnalysis_Employee', N'[EmployerId], [ssn]', NULL),
    (N'EmployeeCode', N'IX_ACA360_EmployeeCode_Advanced', N'[employerId], [filingYear]', N'[employeeId], [getForm]'),
    (N'EmployeeCode', N'IX_EmployeeCode_Employer_Year1', N'[employerId], [filingYear]', N'[id]'),
    (N'EmployeeCode', N'IX_FlagGrid_YearEmployer', N'[filingYear], [employerId]', N'[employeeId]'),
    (N'EmployeeCode', N'IX_ImportAnalysis_Code', N'[filingYear], [employeeId]', N'[employerId]'),
    (N'EmployeeEnrollment', N'IX_ACA360_Enrollment_Plan_Filter', N'[employeeCodeId], [planId]', N'[contributionEndDate], [contributionStartDate], [unionMember]'),
    (N'EmployeeFlags', N'IX_EmployeeFlags_EmployeeCodeId', N'[EmployeeCodeId]', N'[FlagId], [Month]'),
    (N'EmployeeFlags', N'IX_EmployeeFlags_EmployerYear', N'[EmployerId], [TaxYear]', NULL),
    (N'EmployeeFlags', N'IX_FlagGrid_CodeFlag', N'[EmployeeCodeId], [FlagId]', NULL),
    (N'EmployeeHireSpan', N'IX_FlagGrid_HireCode', N'[employeeCodeId]', NULL),
    (N'EmployeePayroll', N'IX_FlagGrid_PayrollCode', N'[employeeCodeId]', NULL),
    (N'EmployeeStatus', N'IX_ACA360_EmployeeStatus_Filter', N'[status]', NULL),
    (N'EmployeeStatus', N'IX_FlagGrid_StatusCode', N'[employeeCodeId]', NULL),
    (N'Employer', N'IX_ImportAnalysis_Employer', N'[filingYear], [taxid]', N'[companyId]');

DECLARE @Seq INT = 1, @Max INT = (SELECT MAX(Seq) FROM @Index), @Created INT = 0,
        @Table SYSNAME, @Name SYSNAME, @Keys NVARCHAR(400), @Include NVARCHAR(400), @Sql NVARCHAR(MAX);
WHILE @Seq <= @Max
BEGIN
    SELECT @Table = TableName, @Name = IndexName, @Keys = KeyColumns, @Include = IncludeColumns FROM @Index WHERE Seq = @Seq;
    IF OBJECT_ID(N'dbo.' + QUOTENAME(@Table)) IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.' + QUOTENAME(@Table)) AND name = @Name)
    BEGIN
        SET @Sql = N'CREATE NONCLUSTERED INDEX ' + QUOTENAME(@Name) + N' ON dbo.' + QUOTENAME(@Table) + N' (' + @Keys + N')'
                 + ISNULL(N' INCLUDE (' + @Include + N')', N'') + N';';
        EXEC sys.sp_executesql @Sql;
        PRINT '  created ' + @Table + '.' + @Name;
        SET @Created += 1;
    END
    SET @Seq += 1;
END

PRINT '052: Employee page indexes in place (' + CAST(@Created AS VARCHAR(10)) + ' created).';
GO
