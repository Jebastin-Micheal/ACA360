using ACA360.BusinessLogic.Services;
using ACA360.Core.Constants;
using ACA360.Core.Models;
using ACA360.Repositories.Interfaces;
using Hangfire;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OfficeOpenXml;

namespace ACA360.Tests.Unit.Services;

/// <summary>Builds small .xlsx files in memory: sheet name -> rows (first row = headers).</summary>
internal static class Workbooks
{
    public static MemoryStream Build(params (string Sheet, object?[][] Rows)[] sheets)
    {
        ExcelPackage.LicenseContext = LicenseContext.Commercial;   // same configuration as ACA360
        using var package = new ExcelPackage();
        foreach (var (name, rows) in sheets)
        {
            var sheet = package.Workbook.Worksheets.Add(name);
            for (var r = 0; r < rows.Length; r++)
                for (var c = 0; c < rows[r].Length; c++)
                {
                    sheet.Cells[r + 1, c + 1].Value = rows[r][c];
                    if (rows[r][c] is DateTime) sheet.Cells[r + 1, c + 1].Style.Numberformat.Format = "yyyy-mm-dd";
                }
        }
        return new MemoryStream(package.GetAsByteArray());
    }
}

public class PlanYearScanServiceTests
{
    private readonly PlanYearScanService _service = new(NullLogger<PlanYearScanService>.Instance);

    private static object?[] Premium(DateTime start, DateTime? end) => new object?[] { "Gold", start, end };
    private static readonly object?[] PremiumHeader = { "Plan", "Start Date", "End Date" };

    [Fact]
    public void PremiumsCoveringTheYearRaiseNoWarning()
    {
        using var file = Workbooks.Build(("Premium", new[] { PremiumHeader, Premium(new(2025, 1, 1), new(2025, 12, 31)) }));
        var result = _service.Scan(file, 2025);
        Assert.True(result.Scanned);
        Assert.Equal(1, result.PremiumRowsCoveringYear);
        Assert.False(result.HasWarnings);
    }

    [Fact]
    public void PremiumsForAnotherYearWarnAndSuggestThatYear()
    {
        using var file = Workbooks.Build(("Premium", new[] { PremiumHeader, Premium(new(2024, 1, 1), new(2024, 12, 31)), Premium(new(2024, 6, 1), new(2024, 12, 31)) }));
        var result = _service.Scan(file, 2025);
        Assert.Equal(2, result.PremiumRowsFound);
        Assert.Equal(0, result.PremiumRowsCoveringYear);
        Assert.Contains(result.Warnings, w => w.Contains("None of the 2 premium rows cover 2025"));
        Assert.Equal(2024, result.SuggestedYear);
        Assert.Equal(new[] { 2024 }, result.YearsInFile);
    }

    [Fact]
    public void AnOpenEndDateReachesLaterYears()
    {
        using var file = Workbooks.Build(("Premium", new[] { PremiumHeader, Premium(new(2023, 1, 1), null) }));
        Assert.Equal(1, _service.Scan(file, 2025).PremiumRowsCoveringYear);
    }

    [Fact]
    public void CoveragePeriodsOutsideTheYearWarn()
    {
        using var file = Workbooks.Build(("Employee", new[]
        {
            new object?[] { "SSN", "Coverage Start Date", "Coverage End Date" },
            new object?[] { "x", new DateTime(2022, 1, 1), new DateTime(2022, 12, 31) },
        }));
        var result = _service.Scan(file, 2025);
        Assert.Equal(1, result.CoverageRowsFound);
        Assert.Contains(result.Warnings, w => w.Contains("not employed for all twelve months"));
    }

    [Fact]
    public void HeadersAreMatchedIgnoringCaseAndSpacing()
    {
        using var file = Workbooks.Build(("employee", new[]
        {
            new object?[] { "coverage   start date", "COVERAGE END DATE" },
            new object?[] { new DateTime(2025, 3, 1), new DateTime(2025, 9, 30) },
        }));
        Assert.Equal(1, _service.Scan(file, 2025).CoverageRowsCoveringYear);
    }

    [Fact]
    public void OtherSheetsAreIgnored()
    {
        using var file = Workbooks.Build(("Notes", new[] { PremiumHeader, Premium(new(2010, 1, 1), new(2010, 2, 1)) }));
        var result = _service.Scan(file, 2025);
        Assert.True(result.Scanned);
        Assert.Equal(0, result.PremiumRowsFound);
        Assert.False(result.HasWarnings);
    }

    [Fact]
    public void AFileItCannotReadIsSkippedQuietly()
    {
        var result = _service.Scan(new MemoryStream(new byte[] { 1, 2, 3, 4 }), 2025);
        Assert.False(result.Scanned);
        Assert.False(result.HasWarnings);
    }

    [Fact]
    public void NoFileOrNoYearMeansNoScan()
    {
        Assert.False(_service.Scan(null!, 2025).Scanned);
        using var file = Workbooks.Build(("Premium", new[] { PremiumHeader }));
        Assert.False(_service.Scan(file, 0).Scanned);
    }
}

public class ExcelStructureValidatorServiceTests
{
    private readonly ExcelStructureValidatorService _service = new(NullLogger<ExcelStructureValidatorService>.Instance);

    private static TemplateColumnMap Rule(string sheet, string column, bool required = true, string? alternates = null) =>
        new() { SourceSheetName = sheet, SourceColumnName = column, IsRequired = required, AlternateNames = alternates };

    private static MemoryStream EmployeeSheet(params string[] headers) =>
        Workbooks.Build(("Employee", new[] { headers.Cast<object?>().ToArray(), headers.Select(_ => (object?)"x").ToArray() }));

    [Fact]
    public void AFileWithEveryRequiredColumnIsValid()
    {
        using var file = EmployeeSheet("Primary EIN", "Employee SSN", "First Name");
        var result = _service.ValidateStructure(file, new() { Rule("Employee", "Primary EIN"), Rule("Employee", "Employee SSN") });
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void HeadersAreMatchedIgnoringCaseSpacesAndSymbols()
    {
        using var file = EmployeeSheet("PRIMARY_EIN", "employee-ssn");
        Assert.True(_service.ValidateStructure(file, new() { Rule("employee", "Primary EIN"), Rule("EMPLOYEE", "Employee SSN") }).IsValid);
    }

    [Fact]
    public void AMissingRequiredColumnIsReported()
    {
        using var file = EmployeeSheet("Primary EIN");
        var result = _service.ValidateStructure(file, new() { Rule("Employee", "Primary EIN"), Rule("Employee", "Employee SSN") });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'Employee SSN'"));
    }

    [Fact]
    public void AnAlternateNameIsAccepted()
    {
        using var file = EmployeeSheet("SSN");
        Assert.True(_service.ValidateStructure(file, new() { Rule("Employee", "Employee SSN", alternates: "Social Security Number, SSN") }).IsValid);
    }

    [Fact]
    public void OptionalColumnsMayBeMissing()
    {
        using var file = EmployeeSheet("Primary EIN");
        Assert.True(_service.ValidateStructure(file, new() { Rule("Employee", "Primary EIN"), Rule("Employee", "Middle Name", required: false) }).IsValid);
    }

    [Fact]
    public void AMissingSheetIsReported()
    {
        using var file = EmployeeSheet("Primary EIN");
        var result = _service.ValidateStructure(file, new() { Rule("Plan", "Plan Name") });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Missing required worksheet: 'Plan'"));
    }

    [Fact]
    public void AnEmptySheetIsReported()
    {
        using var file = Workbooks.Build(("Employee", Array.Empty<object?[]>()), ("Other", new[] { new object?[] { "x" } }));
        var result = _service.ValidateStructure(file, new() { Rule("Employee", "Primary EIN") });
        Assert.Contains(result.Errors, e => e.Contains("empty or missing headers"));
    }

    [Fact]
    public void ANonExcelFileIsRejected()
    {
        // Rejected with a message, not an exception (which failed and retried the background job).
        var result = _service.ValidateStructure(new MemoryStream(new byte[] { 1, 2, 3 }), new() { Rule("Employee", "Primary EIN") });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("not an Excel workbook"));
    }
}

public class FileUploadWorkflowServiceTests
{
    private readonly Mock<IFileUploadLogService> _logs = new();
    private FileUploadWorkflowService Service => new(_logs.Object, new Mock<IBackgroundJobClient>().Object);

    // Who may open which file is decided in the database (dbo.fn_FileVisibleToUser, the File
    // Upload list's own rule); Integration/FileAccessTests checks that rule. Here: the service
    // asks it for exactly the file and the signed-in user, and refuses without a user.

    [Fact]
    public async Task AccessIsWhatTheDatabaseRuleSays()
    {
        _logs.Setup(l => l.GetVisibleFileIdsAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<int>())).ReturnsAsync(new HashSet<int>());
        _logs.Setup(l => l.GetVisibleFileIdsAsync(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 42 })), 6))
             .ReturnsAsync(new HashSet<int> { 42 });
        Assert.True(await Service.VerifyFileAccessAsync(42, "6"));
        Assert.False(await Service.VerifyFileAccessAsync(43, "6"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("abc")]
    public async Task WithoutASignedInUserNothingIsVisible(string userId)
    {
        Assert.False(await Service.VerifyFileAccessAsync(42, userId));
        Assert.Empty(await Service.GetVisibleFileIdsAsync(new[] { 42 }, userId));
        _logs.Verify(l => l.GetVisibleFileIdsAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ABatchIsCheckedInOneCall()
    {
        _logs.Setup(l => l.GetVisibleFileIdsAsync(It.IsAny<IEnumerable<int>>(), 6)).ReturnsAsync(new HashSet<int> { 1, 3 });
        Assert.Equal(new[] { 1, 3 }, (await Service.GetVisibleFileIdsAsync(new[] { 1, 2, 3 }, "6")).OrderBy(i => i));
        _logs.Verify(l => l.GetVisibleFileIdsAsync(It.IsAny<IEnumerable<int>>(), 6), Times.Once);
    }

    [Theory]
    [InlineData(UserRoles.DataAnalyst, true)]
    [InlineData(UserRoles.DASupervisor, true)]
    [InlineData(UserRoles.SuperAdmin, true)]
    [InlineData(UserRoles.AccountManager, false)]
    [InlineData(UserRoles.Admin, false)]
    public void OnlyAnalystsAndAboveSelfAssignUploads(string role, bool expected) => Assert.Equal(expected, Service.IsDAOrAbove(role));

    [Theory]
    [InlineData(UserRoles.SuperAdmin, true)]
    [InlineData(UserRoles.DataAnalyst, true)]
    [InlineData(UserRoles.AccountManager, true)]
    [InlineData(UserRoles.Broker, false)]
    [InlineData(UserRoles.Employer, false)]
    [InlineData(UserRoles.StaffAC, false)]
    [InlineData(UserRoles.StaffAU, false)]
    public void ImportIsLimitedToFileHandlers(string role, bool expected)
    {
        Assert.Equal(expected, Service.CanImport(role));
        Assert.Equal(expected, UserRoles.FileHandlers.Split(',').Contains(role));   // the two lists agree
    }

    [Fact]
    public void TheFileHashIsLowerCaseSha256()
    {
        using var stream = new MemoryStream("abc"u8.ToArray());
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", Service.ComputeSha256Hash(stream));
    }
}
