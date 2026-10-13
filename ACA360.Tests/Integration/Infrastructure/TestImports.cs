using System.Security.Cryptography;
using Dapper;
using OfficeOpenXml;

namespace ACA360.Tests.Integration.Infrastructure;

public sealed record TestEmployer(int Id, string Ein, string Name, int Year);

/// <summary>
/// For upload → validate → import: a throw-away employer (name starting with
/// <see cref="EmployerName"/>, EIN 45-9xxxxxx), the client import workbook filled in from the
/// template the app hands out, and removal of everything an import writes for them.
/// </summary>
public static class TestImports
{
    public const string EmployerName = "ACA360TESTS IMPORT";
    public const string FilePrefix = "aca360.tests.";

    public static readonly string Folder = Path.Combine(Path.GetTempPath(), "aca360-tests");

    public static TestEmployer CreateEmployer(int year)
    {
        var ein = $"45-9{RandomNumberGenerator.GetInt32(0, 1_000_000):D6}";
        var name = $"{EmployerName} {Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        var id = LocalDb.Scalar<int>(
            @"INSERT INTO dbo.Employer (name, taxid, filingYear, FormType, address, city, state, zip, phoneNumber, contactName)
              OUTPUT inserted.id VALUES (@name, @ein, @year, '1095-C', '1 Test Plaza', 'Testville', 'NJ', '07001', '2015550100', 'TEST CONTACT');",
            new { name, ein, year });
        return new TestEmployer(id, ein, name, year);
    }

    /// <summary>One row per dictionary, keyed by the template's header text.</summary>
    public sealed class Workbook
    {
        public Dictionary<string, List<Dictionary<string, object?>>> Sheets { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Workbook Add(string sheet, Dictionary<string, object?> row)
        {
            if (!Sheets.TryGetValue(sheet, out var rows)) Sheets[sheet] = rows = new();
            rows.Add(row);
            return this;
        }

        /// <summary>Writes the rows into a copy of wwwroot/templates/ACA_General_Import_Template.xlsx.</summary>
        public string Save(string label, IEnumerable<string>? dropSheets = null)
        {
            ExcelPackage.LicenseContext = LicenseContext.Commercial;
            Directory.CreateDirectory(Folder);
            var path = Path.Combine(Folder, $"{FilePrefix}{label}.{Guid.NewGuid().ToString("N")[..8]}.xlsx");
            var template = Path.Combine(AcaWebFactory.WebRoot, "wwwroot", "templates", "ACA_General_Import_Template.xlsx");

            using var package = new ExcelPackage(new FileInfo(template));
            foreach (var drop in dropSheets ?? Array.Empty<string>())
                package.Workbook.Worksheets.Delete(drop);

            foreach (var (sheetName, rows) in Sheets)
            {
                var ws = package.Workbook.Worksheets[sheetName] ?? throw new InvalidOperationException($"The template has no sheet '{sheetName}'.");
                var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int c = 1; c <= (ws.Dimension?.End.Column ?? 0); c++)
                {
                    var text = ws.Cells[1, c].Text?.Trim();
                    if (!string.IsNullOrEmpty(text)) headers[text] = c;
                }
                // Clear any example rows the template carries.
                if (ws.Dimension != null && ws.Dimension.End.Row > 1)
                    ws.DeleteRow(2, ws.Dimension.End.Row - 1);

                int r = 2;
                foreach (var row in rows)
                {
                    foreach (var (header, value) in row)
                    {
                        if (!headers.TryGetValue(header, out var col))
                            throw new InvalidOperationException($"Sheet '{sheetName}' has no column '{header}'.");
                        ws.Cells[r, col].Value = value;
                    }
                    r++;
                }
            }
            package.SaveAs(new FileInfo(path));
            return path;
        }
    }

    /// <summary>Removes test employers, their file logs and everything imported for them.</summary>
    public static void RemoveAll()
    {
        if (LocalDb.SkipReason != null) return;
        using var db = LocalDb.Open();
        db.Execute(@"
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            DECLARE @er TABLE (id INT PRIMARY KEY);
            INSERT @er SELECT id FROM dbo.Employer WHERE name LIKE @name + '%' OR taxid IN (SELECT taxid FROM dbo.Employer WHERE name LIKE @name + '%');
            -- Affiliates an import created under a test employer (Dummy Data files name them DUMMY ...).
            INSERT @er SELECT id FROM dbo.Employer WHERE companyId IN (SELECT id FROM @er) AND id NOT IN (SELECT id FROM @er);
            DECLARE @f TABLE (id INT PRIMARY KEY);
            INSERT @f SELECT FileLogId FROM dbo.UploadedFileLog
                      WHERE OriginalFileName LIKE @file + '%'
                         OR TaxID IN (SELECT taxid FROM dbo.Employer WHERE id IN (SELECT id FROM @er));
            DECLARE @ee TABLE (id INT PRIMARY KEY);
            INSERT @ee SELECT id FROM dbo.Employee WHERE EmployerId IN (SELECT id FROM @er) OR FileLogId IN (SELECT id FROM @f);
            DECLARE @c TABLE (id INT PRIMARY KEY);
            INSERT @c SELECT id FROM dbo.EmployeeCode WHERE employerId IN (SELECT id FROM @er) OR employeeId IN (SELECT id FROM @ee);

            DELETE FROM dbo.EmployeeFlags        WHERE EmployeeCodeId IN (SELECT id FROM @c) OR EmployerId IN (SELECT id FROM @er);
            DELETE FROM dbo.CoveredIndividual    WHERE employeeCodeId IN (SELECT id FROM @c) OR EmployerId IN (SELECT id FROM @er) OR FileLogId IN (SELECT id FROM @f);
            DELETE FROM dbo.EmployeeEnrollment   WHERE employeeCodeId IN (SELECT id FROM @c);
            DELETE FROM dbo.EmployeeHireSpan     WHERE employeeCodeId IN (SELECT id FROM @c);
            DELETE FROM dbo.EmployeeIchra        WHERE employeeCodeId IN (SELECT id FROM @c);
            DELETE FROM dbo.EmployeePayroll      WHERE employeeCodeId IN (SELECT id FROM @c);
            DELETE FROM dbo.EmployeeStatus       WHERE employeeCodeId IN (SELECT id FROM @c);
            DELETE FROM dbo.EmployeeCodeAuditTrail WHERE EmployeeId IN (SELECT id FROM @ee) OR EmployerId IN (SELECT id FROM @er);
            DELETE FROM dbo.AuditLog             WHERE employeeId IN (SELECT id FROM @ee) OR employerId IN (SELECT id FROM @er);
            IF OBJECT_ID('dbo.EmployeeCodeMonthDetail') IS NOT NULL DELETE FROM dbo.EmployeeCodeMonthDetail WHERE EmployeeCodeId IN (SELECT id FROM @c);
            DELETE FROM dbo.EmployeeCode         WHERE id IN (SELECT id FROM @c);
            IF OBJECT_ID('dbo.EmployeeChangeHistory') IS NOT NULL DELETE FROM dbo.EmployeeChangeHistory WHERE EmployeeId IN (SELECT id FROM @ee) OR FileLogId IN (SELECT id FROM @f);
            IF OBJECT_ID('dbo.EmployerColumnMaps') IS NOT NULL DELETE FROM dbo.EmployerColumnMaps WHERE EmployerEin IN (SELECT REPLACE(taxid, '-', '') FROM dbo.Employer WHERE id IN (SELECT id FROM @er));
            DELETE FROM dbo.Employee             WHERE id IN (SELECT id FROM @ee);
            DELETE FROM dbo.Premium              WHERE EmployerId IN (SELECT id FROM @er) OR FileLogId IN (SELECT id FROM @f);
            DELETE FROM dbo.EmployerPlan         WHERE employerId IN (SELECT id FROM @er) OR FileLogId IN (SELECT id FROM @f);
            DELETE FROM dbo.EmployerPenaltyCalculation WHERE EmployerId IN (SELECT id FROM @er);
            DELETE FROM dbo.EmployerFilingStatus WHERE EmployerId IN (SELECT id FROM @er);
            DELETE FROM dbo.FlagEngineRuleErrors WHERE EmployerId IN (SELECT id FROM @er);
            DELETE FROM dbo.Employer_AggregatedGroupMembers WHERE EmployerId IN (SELECT id FROM @er);
            DELETE FROM dbo.FilingAuditLog       WHERE EmployerId IN (SELECT id FROM @er);

            DELETE FROM dbo.StagingRowErrors     WHERE FileLogId IN (SELECT id FROM @f);
            DELETE FROM dbo.StagingAutoCorrections WHERE FileLogId IN (SELECT id FROM @f);
            IF OBJECT_ID('dbo.StagingIssueSkips') IS NOT NULL DELETE FROM dbo.StagingIssueSkips WHERE FileLogId IN (SELECT id FROM @f);
            IF OBJECT_ID('dbo.IssueAcknowledgements') IS NOT NULL DELETE FROM dbo.IssueAcknowledgements WHERE FileLogId IN (SELECT id FROM @f);
            DELETE FROM dbo.Staging_Dependents   WHERE FileLogId IN (SELECT id FROM @f);
            DELETE FROM dbo.Staging_Employees    WHERE FileLogId IN (SELECT id FROM @f);
            DELETE FROM dbo.Staging_Employers    WHERE FileLogId IN (SELECT id FROM @f);
            DELETE FROM dbo.Staging_Plans        WHERE FileLogId IN (SELECT id FROM @f);
            DELETE FROM dbo.Staging_Premiums     WHERE FileLogId IN (SELECT id FROM @f);
            DELETE FROM dbo.ImportValidationSummary WHERE FileLogId IN (SELECT id FROM @f);
            DELETE FROM dbo.FileUploadErrors     WHERE FileLogId IN (SELECT id FROM @f);
            DELETE FROM dbo.FileLifecycleHistory WHERE FileLogId IN (SELECT id FROM @f);
            DELETE FROM dbo.UploadedFileLog      WHERE FileLogId IN (SELECT id FROM @f);
            DELETE FROM dbo.Tracker_ProcessHistory WHERE EmployerServiceId IN (SELECT EmployerServiceId FROM dbo.Tracker_EmployerService WHERE EmployerId IN (SELECT id FROM @er));
            DELETE FROM dbo.Tracker_ReceiptId      WHERE EmployerId IN (SELECT id FROM @er);
            DELETE FROM dbo.Tracker_EmployerService WHERE EmployerId IN (SELECT id FROM @er);
            DELETE FROM dbo.Tracker_Affiliate      WHERE EmployerId IN (SELECT id FROM @er);
            DELETE FROM dbo.Tracker_EmployerExtension WHERE EmployerId IN (SELECT id FROM @er);
            DELETE FROM dbo.tbl_Medtracker_Note    WHERE employerid IN (SELECT id FROM @er);
            DELETE FROM dbo.tbl_Medtracker_Service WHERE employerid IN (SELECT id FROM @er);
            DELETE FROM dbo.tbl_Medtracker_Contact WHERE employerid IN (SELECT id FROM @er);
            DELETE FROM dbo.tbl_Medtracker_Affiliate WHERE employerid IN (SELECT id FROM @er);
            DELETE FROM dbo.tbl_Medtracker_Employer WHERE employerid IN (SELECT id FROM @er);
            DELETE FROM dbo.tbl_Portfolio_Assignment   WHERE EmployerID IN (SELECT id FROM @er);
            DELETE FROM dbo.tbl_Operational_Assignment WHERE EmployerID IN (SELECT id FROM @er);
            DELETE FROM dbo.tbl_Employer_Broker_Assignment WHERE EmployerID IN (SELECT id FROM @er);
            DELETE FROM dbo.tbl_AM_Assignment_History  WHERE EmployerID IN (SELECT id FROM @er);
            DELETE FROM dbo.tbl_DA_Assignment_History  WHERE EmployerID IN (SELECT id FROM @er);
            DELETE FROM dbo.GenerateMultiEmployerBatchHistoryEmployers WHERE EmployerId IN (SELECT id FROM @er);
            DELETE FROM dbo.IrsSubmissionErrors    WHERE SubmissionId IN (SELECT SubmissionId FROM dbo.IrsSubmissionLog WHERE EmployerId IN (SELECT id FROM @er));
            DELETE FROM dbo.IrsSubmissionLog       WHERE EmployerId IN (SELECT id FROM @er) AND ParentSubmissionId IS NOT NULL;
            DELETE FROM dbo.IrsSubmissionLog       WHERE EmployerId IN (SELECT id FROM @er);
            DELETE FROM dbo.ApiClient              WHERE EmployerId IN (SELECT id FROM @er);
            UPDATE dbo.Employer SET companyId = NULL WHERE companyId IN (SELECT id FROM @er);
            DELETE FROM dbo.Employer             WHERE id IN (SELECT id FROM @er);
            COMMIT TRANSACTION;",
            new { name = EmployerName, file = FilePrefix });

        if (Directory.Exists(Folder))
            foreach (var file in Directory.GetFiles(Folder, FilePrefix + "*")) File.Delete(file);
    }
}
