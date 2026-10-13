using System.Security.Cryptography;
using ACA360.BusinessLogic.Interfaces;
using ACA360.BusinessLogic.Services;
using ACA360.Core.Helpers;
using ACA360.Core.Models;
using ACA360.Repositories.Interfaces;
using ACA360.Tests.Integration.Infrastructure;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace ACA360.Tests.Integration;

/// <summary>
/// A client file from upload to the live tables, through the same three stages the background
/// jobs run: the structure check (FileProcessingService), reading into staging and the ACA rules
/// (DataProcessingService.ExtractAndValidateDataAsync), and the merge (ImportToLiveAsync).
/// The file is the template the app hands out, filled in; it is read from disk, the way the
/// app reads a file that is not in Azure, so nothing is uploaded anywhere. Each test imports
/// into its own throw-away employer (see <see cref="TestImports"/>).
/// </summary>
[Collection(AppCollection.Name)]
public partial class ImportPipelineTests : IDisposable
{
    private const int Year = 2025;
    private const int StandardTemplate = 2;
    private readonly AcaWebFactory _app;
    private readonly IServiceScope _scope;

    public ImportPipelineTests(AcaWebFactory app)
    {
        _app = app;
        _scope = app.Services.CreateScope();
    }

    public void Dispose()
    {
        _scope.Dispose();
        TestImports.RemoveAll();
    }

    private T Service<T>() where T : notnull => _scope.ServiceProvider.GetRequiredService<T>();

    /// <summary>
    /// A made-up SSN that passes the structure rules (area 100-665, group 10-99, serial 1000-9999).
    /// The import rejects 9xx area numbers, which the SSA never issues, so they cannot be used here.
    /// </summary>
    private static string Ssn() => $"{RandomNumberGenerator.GetInt32(100, 666)}-{RandomNumberGenerator.GetInt32(10, 100)}-{RandomNumberGenerator.GetInt32(1000, 10000)}";

    private static TestImports.Workbook GoodFile(TestEmployer employer, string ssnOne, string ssnTwo, string stateTwo = "NJ")
    {
        const string plan = "ACA360TESTS PPO";
        var book = new TestImports.Workbook();
        book.Add("Employer Tab", new()
        {
            // No Affiliated EIN: the row describes the primary employer only (this stopped the import before script 048).
            ["Primary EIN"] = employer.Ein, ["Affiliated EIN"] = null, ["Employer Name"] = employer.Name,
            ["Foreign Address Ind"] = 0, ["Address"] = "1 TEST PLAZA", ["Address 2"] = null, ["City"] = "TESTVILLE",
            ["State/Province"] = "NJ", ["Zip"] = "07001", ["Country"] = "US", ["Phone"] = "2015550100",
            ["Contact"] = "TEST CONTACT", ["Title"] = "HR MANAGER", ["Origin Code"] = null, ["SHOP Identifier"] = null, ["Notes"] = null,
        });
        book.Add("Plan Information", new()
        {
            ["Primary EIN"] = employer.Ein, ["Plan Name"] = plan, ["Plan Type"] = "Medical", ["Offered to Spouse"] = 1,
            ["Offered to Dependents"] = 1, ["Waiting period (number of days)"] = 0, ["Eligible First of the Month"] = 1,
            ["Funding Type"] = 1, ["Plan Renewal Month"] = 1, ["Plan Terminates on Date of Termination"] = 0,
            ["Minimum Value"] = 1, ["Banding Type"] = "Monthly", ["Premium Cap"] = null,
        });
        book.Add("Premium", new()
        {
            ["Primary EIN"] = employer.Ein, ["Plan Name"] = plan, ["Banding Type"] = "Monthly", ["Start"] = "January",
            ["End"] = "December", ["Start Date"] = $"{Year}-01-01", ["End Date"] = $"{Year}-12-31", ["EE Monthly Contribution"] = "95.50",
        });
        foreach (var (first, ssn, state) in new[] { ("IMPORTONE", ssnOne, "NJ"), ("IMPORTTWO", ssnTwo, stateTwo) })
        {
            book.Add("Employee", new()
            {
                ["Primary EIN"] = employer.Ein, ["EIN Associated with EE (May be an Affiliate EIN)"] = employer.Ein,
                ["Employee Legal First Name"] = first, ["Employee Middle Initial"] = null, ["Employee Legal Last Name"] = TestEmployees.LastName,
                ["Employee Suffix"] = null, ["Employee SSN"] = ssn, ["Employee Birthdate"] = "1985-04-12", ["Status"] = 1,
                ["Expatriate"] = 0, ["Status Start Date"] = "2019-06-03", ["Status End Date"] = null, ["Hire Date"] = "2019-06-03",
                ["Termination Date"] = null, ["W-2 Income/ Annual Salary (if applicable)"] = 78500, ["Additional Income"] = null,
                ["Hourly rate (if applicable)"] = null, ["Email Address"] = $"{TestUsers.Prefix}{first.ToLowerInvariant()}@example.com",
                ["Foreign Address"] = 0, ["Address 1"] = "4415 TEST BLVD", ["Address 2"] = null, ["City"] = "TESTVILLE",
                ["State/ Province"] = state, ["Zip"] = "07001", ["Country"] = "US", ["Lowest Cost Plan Offered (Plan Name)"] = plan,
                ["Date Eligible for Coverage"] = $"{Year}-01-01", ["Coverage Elected?"] = 1, ["Coverage Start Date"] = $"{Year}-01-01",
                ["Coverage End Date"] = null, ["Union Employee? (Union offers benefits)"] = 0,
                ["Date Employer First Pays to Union Benefits"] = null, ["Date Employer Last Pays to Union Benefits"] = null,
                ["COBRA Benefits elected?"] = 0, ["COBRA Coverage Effective Date"] = null, ["COBRA Coverage End Date"] = null,
                ["Retiree Benefits elected?"] = 0, ["Retiree Plan Start Date"] = null, ["Retiree Plan End Date"] = null,
            });
        }
        book.Add("Dependents", new()
        {
            ["Primary EIN"] = employer.Ein, ["EIN Associated with EE (May be an Affiliate EIN)"] = employer.Ein,
            ["Employee SSN"] = ssnOne, ["Dependent Legal First Name"] = "DEPENDENTONE", ["Dependent Middle Initial"] = null,
            ["Dependent Legal Last Name"] = TestEmployees.LastName, ["Dependent Suffix"] = null, ["Dependent SSN"] = Ssn(),
            ["Dependent Birthdate"] = "2015-02-17", ["Dependent Coverage Start Date"] = $"{Year}-01-01", ["Dependent Coverage End Date"] = null,
        });
        return book;
    }

    /// <summary>Creates the file log the way FileUploadController.Upload does, for a file on disk.</summary>
    private async Task<int> LogFile(string path, TestEmployer employer, TestUser uploader)
    {
        using var stream = File.OpenRead(path);
        return await Service<IFileUploadLogService>().CreateLogAsync(new UploadedFileLog
        {
            OriginalFileName = Path.GetFileName(path),
            StoredFileName = Path.GetFileName(path),
            FilePath = path,
            FileHash = Convert.ToHexString(SHA256.HashData(stream)),
            EmployerName = employer.Name,
            TaxID = employer.Ein,
            ImportTemplateId = StandardTemplate,
            PlanYear = Year,
            UploadedByUserId = uploader.UserId.ToString(),
            UploadedAt = DateTime.UtcNow,
            ValidationStatusId = VS.Pending,
            WorkflowStatusId = WS.PendingAssignment,
        });
    }

    private static UploadedFileLog Log(int id) =>
        LocalDb.Open().QuerySingle<UploadedFileLog>("SELECT * FROM dbo.UploadedFileLog WHERE FileLogId = @id", new { id });

    /// <summary>What FileUploadController.AssignFile changes, without queueing the job (the test runs it).</summary>
    private static void Assign(int fileLogId, TestUser analyst) =>
        LocalDb.Execute("UPDATE dbo.UploadedFileLog SET AssignedToUserId = @u, WorkflowStatusId = @ws, ValidationStatusId = @vs WHERE FileLogId = @id",
            new { u = analyst.UserId.ToString(), ws = WS.Processing, vs = VS.Validating, id = fileLogId });

    private static string Errors(int fileLogId) => string.Join("\n", LocalDb.Open().Query<string>(
        @"SELECT TOP 60 CONCAT(r.Severity, ' ', e.StagingTableName, ' row ', e.StagingRowId, ' rule ', r.RuleId, ' ', r.TargetColumn, ': ', r.ErrorMessage)
          FROM dbo.StagingRowErrors e JOIN dbo.ValidationRules r ON r.RuleId = e.RuleId WHERE e.FileLogId = @fileLogId",
        new { fileLogId }));

    [LocalDbFact]
    public async Task AGoodFileGoesFromUploadToTheLiveTables()
    {
        var employer = TestImports.CreateEmployer(Year);
        var analyst = TestUsers.CreateStaff(RoleIds.DataAnalyst, "import");
        var (ssnOne, ssnTwo) = (Ssn(), Ssn());
        var fileLogId = await LogFile(GoodFile(employer, ssnOne, ssnTwo).Save("good"), employer, analyst);

        // 1. Structure check: every sheet and column the template needs is there.
        await Service<IFileProcessingService>().ProcessFileAsync(fileLogId, StandardTemplate);
        Assert.Equal(WS.PendingAssignment, Log(fileLogId).WorkflowStatusId);

        // 2. Assigned to an analyst: read into staging and run the ACA rules.
        Assign(fileLogId, analyst);
        await Service<IDataProcessingService>().ExtractAndValidateDataAsync(fileLogId);
        var validated = Log(fileLogId);
        Assert.True(validated.ValidationStatusId is VS.Clean or VS.ValidWithWarnings,
            $"validation ended at {validated.ValidationStatusId}:\n{Errors(fileLogId)}");
        Assert.Equal(2, LocalDb.Scalar<int>("SELECT COUNT(*) FROM dbo.Staging_Employees WHERE FileLogId = @fileLogId", new { fileLogId }));

        // 3. The preview the Import button checks first.
        var analysis = await Service<IDataProcessingRepository>().GetImportAnalysisSummaryAsync(fileLogId, "Merge");
        Assert.True(analysis.Entities.Sum(e => e.Total) > 0);
        Assert.All(analysis.Entities, e => Assert.Equal(0, e.ErrorRows));

        // 4. Merge into the live tables.
        await Service<IDataProcessingService>().ImportToLiveAsync(fileLogId, "Merge", analyst.UserId.ToString());
        var imported = Log(fileLogId);
        Assert.True(imported.WorkflowStatusId == WS.Complete, $"import ended at {imported.WorkflowStatusId}: " + string.Join(" / ", LocalDb.Open().Query<string>(
            @"SELECT CONCAT(ActionName, ': ', Message) FROM dbo.FileLifecycleHistory WHERE FileLogId = @fileLogId ORDER BY HistoryId DESC", new { fileLogId }).Take(2))
            + " / summary: " + LocalDb.Scalar<string?>("SELECT ValidationMessage FROM dbo.ImportValidationSummary WHERE FileLogId = @fileLogId", new { fileLogId }));

        var counts = LocalDb.Open().QuerySingle<(int ByFile, int ByEmployer)>(
            "SELECT (SELECT COUNT(*) FROM dbo.Employee WHERE FileLogId = @fileLogId), (SELECT COUNT(*) FROM dbo.Employee WHERE EmployerId = @id)", new { fileLogId, id = employer.Id });
        var summary = LocalDb.Open().QuerySingleOrDefault<(int? TotalEmployees, string? ValidationMessage, bool? IsImportComplete)>(
            "SELECT TotalEmployees, ValidationMessage, IsImportComplete FROM dbo.ImportValidationSummary WHERE FileLogId = @fileLogId", new { fileLogId });
        Assert.True(counts.ByEmployer == 2, $"employees by file {counts.ByFile}, under the employer {counts.ByEmployer}; summary {summary}");

        using var db = LocalDb.Open();
        var employees = db.Query<(string FirstName, string LastName, string Ssn)>(
            "SELECT firstName, lastName, ssn FROM dbo.Employee WHERE EmployerId = @id", new { id = employer.Id }).ToList();
        Assert.Equal(new[] { "IMPORTONE", "IMPORTTWO" }, employees.Select(e => e.FirstName.ToUpperInvariant()).OrderBy(n => n));
        // Person names keep letters only (the IRS name rule): the file says ACA360TESTS.
        Assert.All(employees, e => Assert.Equal("ACATESTS", e.LastName.ToUpperInvariant()));
        string Digits(string? s) => new((s ?? "").Where(char.IsDigit).ToArray());
        Assert.Equal(new[] { ssnOne, ssnTwo }.Select(Digits).OrderBy(s => s), employees.Select(e => Digits(e.Ssn)).OrderBy(s => s));
        Assert.True(db.ExecuteScalar<int>("SELECT COUNT(*) FROM dbo.EmployerPlan WHERE employerId = @id", new { id = employer.Id }) >= 1);
        Assert.True(db.ExecuteScalar<int>("SELECT COUNT(*) FROM dbo.Premium WHERE EmployerId = @id", new { id = employer.Id }) >= 1);
        var dependents = db.Query<string>(
            @"SELECT ci.firstName FROM dbo.CoveredIndividual ci
              WHERE ci.EmployerId = @id OR ci.employeeCodeId IN (SELECT c.id FROM dbo.EmployeeCode c WHERE c.employerId = @id)", new { id = employer.Id }).ToList();
        Assert.Contains("DEPENDENTONE", dependents.Select(d => d.ToUpperInvariant()));
        // The row without an Affiliated EIN made no aggregated-group member and no affiliate employer.
        Assert.Equal(0, db.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM dbo.Employer_AggregatedGroupMembers WHERE EmployerId = @id AND LTRIM(RTRIM(ISNULL(MemberEIN, ''))) = ''", new { id = employer.Id }));
        Assert.Equal(0, db.ExecuteScalar<int>("SELECT COUNT(*) FROM dbo.Employer WHERE companyId = @id", new { id = employer.Id }));
    }

    [LocalDbFact]
    public async Task AFileWithoutTheEmployeeSheetIsRejectedAtTheStructureCheck()
    {
        var employer = TestImports.CreateEmployer(Year);
        var uploader = TestUsers.CreateStaff(RoleIds.DataAnalyst, "import-sheet");
        var book = GoodFile(employer, Ssn(), Ssn());
        book.Sheets.Remove("Employee");
        var fileLogId = await LogFile(book.Save("nosheet", dropSheets: new[] { "Employee" }), employer, uploader);

        await Service<IFileProcessingService>().ProcessFileAsync(fileLogId, StandardTemplate);

        var log = Log(fileLogId);
        Assert.Equal(VS.StructureError, log.ValidationStatusId);
        Assert.Equal(WS.SystemRejected, log.WorkflowStatusId);
    }

    [LocalDbFact]
    public async Task ABadSsnStopsTheFileAtValidationAndNothingIsImported()
    {
        var employer = TestImports.CreateEmployer(Year);
        var analyst = TestUsers.CreateStaff(RoleIds.DataAnalyst, "import-bad");
        var fileLogId = await LogFile(GoodFile(employer, "12-34", Ssn()).Save("badssn"), employer, analyst);

        await Service<IFileProcessingService>().ProcessFileAsync(fileLogId, StandardTemplate);
        Assign(fileLogId, analyst);
        await Service<IDataProcessingService>().ExtractAndValidateDataAsync(fileLogId);

        var log = Log(fileLogId);
        Assert.Equal(VS.DataError, log.ValidationStatusId);
        var errors = Errors(fileLogId);
        Assert.Contains("SSN", errors, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, LocalDb.Scalar<int>("SELECT COUNT(*) FROM dbo.Employee WHERE EmployerId = @id", new { id = employer.Id }));
    }

    /// <summary>
    /// A warning does not keep a row out of the import: an employee whose ZIP code (07001) does not match
    /// the state (a warning) stays valid, the analysis shows no excluded rows (so Confirm & Import
    /// is enabled) and the employee is imported. Before script 053 any issue made the row invalid.
    /// </summary>
    [LocalDbFact]
    public async Task AnEmployeeWithOnlyAWarningIsImported()
    {
        var employer = TestImports.CreateEmployer(Year);
        var analyst = TestUsers.CreateStaff(RoleIds.DataAnalyst, "import-warn");
        var fileLogId = await LogFile(GoodFile(employer, Ssn(), Ssn(), stateTwo: "CA").Save("warning"), employer, analyst);

        await Service<IFileProcessingService>().ProcessFileAsync(fileLogId, StandardTemplate);
        Assign(fileLogId, analyst);
        await Service<IDataProcessingService>().ExtractAndValidateDataAsync(fileLogId);

        Assert.Equal(VS.ValidWithWarnings, Log(fileLogId).ValidationStatusId);
        Assert.Contains("Warning Staging_Employees", Errors(fileLogId));
        Assert.DoesNotContain("Error ", Errors(fileLogId));
        Assert.Equal(0, LocalDb.Scalar<int>("SELECT COUNT(*) FROM dbo.Staging_Employees WHERE FileLogId = @fileLogId AND IsValid = 0", new { fileLogId }));

        var analysis = await Service<IDataProcessingRepository>().GetImportAnalysisSummaryAsync(fileLogId, "Merge");
        Assert.All(analysis.Entities, e => { Assert.Equal(0, e.ErrorRows); Assert.Equal(0, e.Excluded); });
        Assert.Equal(1, analysis.Entities.Single(e => e.EntityType == "Employee").WarningRows);

        await Service<IDataProcessingService>().ImportToLiveAsync(fileLogId, "Merge", analyst.UserId.ToString());
        Assert.Equal(WS.Complete, Log(fileLogId).WorkflowStatusId);
        Assert.Equal(2, LocalDb.Scalar<int>("SELECT COUNT(*) FROM dbo.Employee WHERE EmployerId = @id", new { id = employer.Id }));
    }

    /// <summary>
    /// The Dummy Data page's files, through validation. Clean files come out Clean with no issue at
    /// all, with every premium banding and with the employee mix (part time, new hires, leavers,
    /// COBRA, retirees, union) switched on, and import whole. With warnings, every ticked kind
    /// fires, nothing else does, and nothing is excluded. With errors, exactly the chosen
    /// employees carry errors and every error kind fires.
    /// </summary>
    [LocalDbTheory]
    [InlineData(0, 0, "Monthly", false, true)]
    [InlineData(0, 0, "Salary", true, true)]
    [InlineData(0, 0, "Age", true, false)]
    [InlineData(0, 0, "Mixed", true, false)]
    [InlineData(20, 0, "Mixed", true, false)]
    [InlineData(0, 20, "Monthly", false, false)]
    public async Task DummyDataFilesValidateAsAsked(int warningPercent, int errorPercent, string banding, bool mix, bool import)
    {
        var employer = TestImports.CreateEmployer(Year);
        var analyst = TestUsers.CreateStaff(RoleIds.DataAnalyst, "dummy");
        var request = new DummyDataRequest
        {
            EmployerIds = { employer.Id }, FilingYear = Year, Affiliates = 2, Plans = 3, Banding = banding, Employees = 60,
            DependentsMin = 0, DependentsMax = 3, WarningPercent = warningPercent, ErrorPercent = errorPercent, Seed = 4242,
        };
        if (mix)
        {
            request.PartTimePercent = 20; request.NewHirePercent = 40; request.CoveragePercent = 80; request.UnionPercent = 10;
            request.TerminatedPercent = 25; request.CobraPercent = 50; request.RetireePercent = 30;
        }
        var book = DummyDataGenerator.Generate(request, new DummyEmployer(employer.Id, employer.Name, employer.Ein), new Random(4242),
            new HashSet<string>(), new HashSet<string>());
        if (mix) Assert.All(new[] { "Part time", "New hires", "Left in the year", "COBRA", "Retiree", "Union" },
            k => Assert.True(book.Summary.Mix[k] > 0, $"no {k} in the file"));
        Directory.CreateDirectory(TestImports.Folder);
        var path = Path.Combine(TestImports.Folder, $"{TestImports.FilePrefix}dummy.{Guid.NewGuid():N}.xlsx");
        File.WriteAllBytes(path, DummyDataService.Write(book, Path.Combine(AcaWebFactory.WebRoot, "wwwroot", "templates", "ACA_General_Import_Template.xlsx")));
        var fileLogId = await LogFile(path, employer, analyst);

        await Service<IFileProcessingService>().ProcessFileAsync(fileLogId, StandardTemplate);
        Assign(fileLogId, analyst);
        await Service<IDataProcessingService>().ExtractAndValidateDataAsync(fileLogId);

        using var db = LocalDb.Open();
        var issues = db.Query<(string Table, int RowId, int RuleId, string Severity)>(
            @"SELECT e.StagingTableName, e.StagingRowId, e.RuleId, r.Severity FROM dbo.StagingRowErrors e
              JOIN dbo.ValidationRules r ON r.RuleId = e.RuleId WHERE e.FileLogId = @fileLogId", new { fileLogId }).ToList();
        Assert.Equal(60, db.ExecuteScalar<int>("SELECT COUNT(*) FROM dbo.Staging_Employees WHERE FileLogId = @fileLogId", new { fileLogId }));
        var status = Log(fileLogId).ValidationStatusId;

        if (warningPercent == 0 && errorPercent == 0)
        {
            Assert.True(issues.Count == 0, "a clean file has issues:\n" + Errors(fileLogId));
            Assert.Equal(VS.Clean, status);
            if (!import) return;

            // It imports whole: every employee, dependent and affiliate (the cleanup removes them).
            await Service<IDataProcessingService>().ImportToLiveAsync(fileLogId, "Merge", analyst.UserId.ToString());
            Assert.Equal(WS.Complete, Log(fileLogId).WorkflowStatusId);
            var affiliates = db.Query<int>("SELECT id FROM dbo.Employer WHERE companyId = @id", new { id = employer.Id }).ToList();
            Assert.Equal(2, affiliates.Count);
            var family = affiliates.Append(employer.Id).ToList();
            Assert.Equal(60, db.ExecuteScalar<int>("SELECT COUNT(*) FROM dbo.Employee WHERE EmployerId IN @family", new { family }));
            Assert.Equal(book.Summary.Dependents, db.ExecuteScalar<int>(
                @"SELECT COUNT(*) FROM dbo.CoveredIndividual ci JOIN dbo.EmployeeCode c ON c.id = ci.employeeCodeId
                  JOIN dbo.Employee e ON e.id = c.employeeId WHERE e.EmployerId IN @family", new { family }));
        }
        else if (errorPercent == 0)
        {
            Assert.True(status == VS.ValidWithWarnings, $"validation ended at {status}:\n" + Errors(fileLogId));
            Assert.True(issues.All(i => i.Severity is "Warning" or "Information"), "not only warnings:\n" + Errors(fileLogId));
            var kinds = DummyDataGenerator.ChosenIssues(request).Where(i => !i.IsError).ToList();
            Assert.True(kinds.Select(i => i.RuleId).OrderBy(r => r).SequenceEqual(issues.Select(i => i.RuleId).Distinct().OrderBy(r => r)),
                "warning kinds:\n" + Errors(fileLogId));
            // One flagged row per warned employee (on the employee or one dependent) and per extra plan.
            var rows = issues.Select(i => (i.Table, i.RowId)).Distinct().Count();
            Assert.True(rows == 12 + kinds.Count(k => k.Target == DummyIssueTarget.Plan), $"{rows} flagged rows:\n" + Errors(fileLogId));
            var analysis = await Service<IDataProcessingRepository>().GetImportAnalysisSummaryAsync(fileLogId, "Merge");
            Assert.All(analysis.Entities, e => { Assert.Equal(0, e.ErrorRows); Assert.Equal(0, e.Excluded); });
        }
        else
        {
            Assert.Equal(VS.DataError, status);
            Assert.True(issues.All(i => i.Table == "Staging_Employees"), "issues outside the employees:\n" + Errors(fileLogId));
            var errorRows = issues.Where(i => i.Severity is not ("Warning" or "Information")).Select(i => i.RowId).Distinct().Count();
            var flagged = issues.Select(i => i.RowId).Distinct().Count();
            Assert.True(errorRows == 12 && flagged == 12, $"{errorRows} employees with errors, {flagged} with issues:\n" + Errors(fileLogId));
            var named = DummyDataGenerator.Issues.Where(i => i.IsError).Select(i => i.RuleId).ToHashSet();
            Assert.True(named.All(r => issues.Any(i => i.RuleId == r)), "an error kind did not fire:\n" + Errors(fileLogId));
        }
    }

    // ---- The validation rules script 055 fixed

    private static readonly string Template = Path.Combine(AcaWebFactory.WebRoot, "wwwroot", "templates", "ACA_General_Import_Template.xlsx");
    private const string Salary = "W-2 Income/ Annual Salary (if applicable)";

    private static string Digits(object? value) => new((value?.ToString() ?? "").Where(char.IsDigit).ToArray());

    /// <summary>A small clean Dummy Data workbook: one plan, no dependents, nobody hired in the year.</summary>
    private static DummyWorkbook SmallBook(TestEmployer employer, int employees, string banding, int seed) =>
        DummyDataGenerator.Generate(new DummyDataRequest
        {
            EmployerIds = { employer.Id }, FilingYear = Year, Plans = 1, Banding = banding, Employees = employees,
            DependentsMin = 0, DependentsMax = 0, NewHirePercent = 0, Seed = seed,
        }, new DummyEmployer(employer.Id, employer.Name, employer.Ein), new Random(seed), new HashSet<string>(), new HashSet<string>());

    private async Task<int> ValidateAsync(DummyWorkbook book, TestEmployer employer, TestUser analyst, string label)
    {
        Directory.CreateDirectory(TestImports.Folder);
        var path = Path.Combine(TestImports.Folder, $"{TestImports.FilePrefix}{label}.{Guid.NewGuid():N}.xlsx");
        File.WriteAllBytes(path, DummyDataService.Write(book, Template));
        var fileLogId = await LogFile(path, employer, analyst);
        await Service<IFileProcessingService>().ProcessFileAsync(fileLogId, StandardTemplate);
        Assign(fileLogId, analyst);
        await Service<IDataProcessingService>().ExtractAndValidateDataAsync(fileLogId);
        return fileLogId;
    }

    /// <summary>The rules each employee row triggered, by SSN digits.</summary>
    private static ILookup<string, int> RulesBySsn(int fileLogId) => LocalDb.Open().Query<(string Ssn, int RuleId)>(
        @"SELECT S.EmployeeSSN, E.RuleId FROM dbo.StagingRowErrors E
          JOIN dbo.Staging_Employees S ON S.StagingEmployeeId = E.StagingRowId
          WHERE E.FileLogId = @fileLogId AND E.StagingTableName = 'Staging_Employees'", new { fileLogId })
        .ToLookup(r => Digits(r.Ssn), r => r.RuleId);

    [LocalDbFact]
    public async Task TheRulesScript055FixedDoWhatTheySay()
    {
        var employer = TestImports.CreateEmployer(Year);
        var analyst = TestUsers.CreateStaff(RoleIds.DataAnalyst, "rules");
        var book = SmallBook(employer, 6, "Monthly", 55);
        var e = book.Rows(DummyDataGenerator.EmployeeSheet);
        e[0]["EIN Associated with EE (May be an Affiliate EIN)"] = "12-3456789";        // not in the Employer tab: rule 74
        e[1]["Termination Date"] = DummyDataGenerator.Date(new DateTime(Year, 6, 30));  // status still open: rule 19
        e[1]["Status End Date"] = null;
        e[2]["State/ Province"] = "NJ"; e[2]["Zip"] = "98101";                          // a Washington ZIP: rule 107
        e[3]["State/ Province"] = "WA"; e[3]["Zip"] = "98101";                          // matches: nothing
        e[4]["Employee Legal Last Name"] = "O'BRIEN";                                     // corrected: an Information note
        e[5]["Lowest Cost Plan Offered (Plan Name)"] = "NO SUCH PLAN";                   // one error: rule 135, not also 12
        var fileLogId = await ValidateAsync(book, employer, analyst, "rules");

        var rules = RulesBySsn(fileLogId);
        string Ssn(int i) => Digits(e[i]["Employee SSN"]);
        var report = Errors(fileLogId);
        int corrected = LocalDb.Scalar<int>("SELECT RuleId FROM dbo.ValidationRules WHERE RuleCode = 'AUTO_CORRECTED'");
        Assert.True(rules[Ssn(0)].Contains(74), "rule 74:\n" + report);
        Assert.True(rules[Ssn(1)].SequenceEqual(new[] { 19 }), "rule 19:\n" + report);
        Assert.True(rules[Ssn(2)].SequenceEqual(new[] { 107 }), "rule 107:\n" + report);
        Assert.True(!rules[Ssn(3)].Any(), "a matching ZIP was reported:\n" + report);
        Assert.True(rules[Ssn(4)].SequenceEqual(new[] { corrected }), "the correction:\n" + report);
        Assert.True(rules[Ssn(5)].SequenceEqual(new[] { 135 }), "missing plan:\n" + report);

        // The correction is kept, before and after, and the issue lists show it.
        using var db = LocalDb.Open();
        var change = db.QuerySingle<(string ColumnName, string OldValue, string NewValue)>(
            "SELECT ColumnName, OldValue, NewValue FROM dbo.StagingAutoCorrections WHERE FileLogId = @fileLogId", new { fileLogId });
        Assert.Equal(("EmployeeLegalLastName", "O'BRIEN", "OBRIEN"), change);
        var row = db.ExecuteScalar<long>(
            "SELECT StagingEmployeeId FROM dbo.Staging_Employees WHERE FileLogId = @fileLogId AND REPLACE(EmployeeSSN, '-', '') = @ssn",
            new { fileLogId, ssn = Ssn(4) });
        var shown = db.QuerySingle("dbo.sp_GetStagingErrorsByRowId",
            new { FileLogId = fileLogId, StagingTableName = "Staging_Employees", StagingRowId = row }, commandType: System.Data.CommandType.StoredProcedure);
        Assert.Equal("EmployeeLegalLastName", (string)shown.TargetColumn);
        Assert.Equal("Information", (string)shown.Severity);
        Assert.Contains("\"O'BRIEN\" became \"OBRIEN\"", (string)shown.ErrorMessage);
        // Rule 74's error keeps one employee out; the Information note does not count.
        Assert.Equal(VS.DataError, Log(fileLogId).ValidationStatusId);
    }

    [LocalDbFact]
    public async Task AgeBandedPremiumsAreCheckedForAffordabilityByAge()
    {
        var employer = TestImports.CreateEmployer(Year);
        var analyst = TestUsers.CreateStaff(RoleIds.DataAnalyst, "age-bands");
        var book = SmallBook(employer, 4, "Age", 56);
        // 10 a month under 30, 200 a month from 30 to 39, 10 above.
        foreach (var premium in book.Rows(DummyDataGenerator.PremiumSheet))
            premium["EE Monthly Contribution"] = (string)premium["Start"]! == "30" ? "200.00" : "10.00";
        var e = book.Rows(DummyDataGenerator.EmployeeSheet);
        e[0]["Employee Birthdate"] = DummyDataGenerator.Date(new DateTime(Year - 25, 6, 15)); e[0][Salary] = 20000;   // 24 on 1 January: 120 a year
        e[1]["Employee Birthdate"] = DummyDataGenerator.Date(new DateTime(Year - 35, 6, 15)); e[1][Salary] = 20000;   // 34: 2,400 a year, 12% of pay
        foreach (var other in e.Skip(2)) { other["Employee Birthdate"] = DummyDataGenerator.Date(new DateTime(Year - 50, 1, 1)); other[Salary] = 60000; }
        var fileLogId = await ValidateAsync(book, employer, analyst, "age-bands");

        var rules = RulesBySsn(fileLogId);
        var report = Errors(fileLogId);
        Assert.True(rules[Digits(e[1]["Employee SSN"])].Contains(105), "the 34-year-old's premium is not affordable:\n" + report);
        Assert.True(rules.Count(g => g.Contains(105)) == 1, "only the 34-year-old:\n" + report);
    }

    // ---- Acknowledged warnings (script 061) -------------------------------------------------

    /// <summary>
    /// A warning acknowledged with a reason in one file is acknowledged again, with that reason, when
    /// a later file for the same employer and year raises it for the same employee; the same warning
    /// for another employee is not.
    /// </summary>
    [LocalDbFact]
    public async Task AnAcknowledgedWarningCarriesOverToTheNextFile()
    {
        var employer = TestImports.CreateEmployer(Year);
        var analyst = TestUsers.CreateStaff(RoleIds.DataAnalyst, "ack");
        DummyWorkbook Book()
        {
            var book = SmallBook(employer, 4, "Monthly", 61);
            var e = book.Rows(DummyDataGenerator.EmployeeSheet);
            e[1]["State/ Province"] = "NJ"; e[1]["Zip"] = "98101";   // a warning (107)
            e[2]["State/ Province"] = "NJ"; e[2]["Zip"] = "98101";   // the same warning, another employee
            return book;
        }
        var rows = Book().Rows(DummyDataGenerator.EmployeeSheet);
        string Ssn(int i) => Digits(rows[i]["Employee SSN"]);

        var first = await ValidateAsync(Book(), employer, analyst, "ack1");
        using var db = LocalDb.Open();
        long Issue(int file, int i) => db.ExecuteScalar<long>(
            @"SELECT E.StagingRowErrorId FROM dbo.StagingRowErrors E JOIN dbo.Staging_Employees S ON S.StagingEmployeeId = E.StagingRowId
              WHERE E.FileLogId = @file AND E.RuleId = 107 AND REPLACE(S.EmployeeSSN, '-', '') = @ssn", new { file, ssn = Ssn(i) });
        Assert.True(await Service<IErrorTriageService>().UpdateErrorStatusAsync(Issue(first, 1), "Skipped", first, "tester", "Checked with the employer"));

        var second = await ValidateAsync(Book(), employer, analyst, "ack2");
        var carried = db.QuerySingle<(string Status, string Reason, int? From)>(
            @"SELECT E.Status, S.Reason, S.CarriedFromFileLogId FROM dbo.StagingRowErrors E
              JOIN dbo.StagingIssueSkips S ON S.FileLogId = E.FileLogId AND S.StagingRowId = E.StagingRowId AND S.RuleId = E.RuleId
              WHERE E.StagingRowErrorId = @id", new { id = Issue(second, 1) });
        Assert.Equal(("Skipped", "Checked with the employer", (int?)first), carried);
        Assert.NotEqual("Skipped", db.ExecuteScalar<string>("SELECT Status FROM dbo.StagingRowErrors WHERE StagingRowErrorId = @id", new { id = Issue(second, 2) }));

        // The triage list says why, and where it came from.
        var view = await Service<IErrorTriageService>().BuildTriageViewModelAsync(second,
            new PaginationEntity { PageIndex = 1, PageSize = 50, SortOrder = "asc" }, "");
        var shown = view!.Errors.Single(e => e.StagingRowErrorId == Issue(second, 1));
        Assert.Equal(("Checked with the employer", (int?)first), (shown.AckReason, shown.AckCarriedFromFileLogId));
    }

    // ---- What changed (script 057) ----------------------------------------------------------

    /// <summary>
    /// A second file for an employer already imported: "What changed" names the new employee,
    /// the leaver (termination date, before and after), the changed city, and the employee in
    /// the system who is not in the file; the modal's tab shows it to someone who may open the
    /// file and to nobody else.
    /// </summary>
    [LocalDbFact]
    public async Task WhatChangedComparesTheFileWithTheSystem()
    {
        var employer = TestImports.CreateEmployer(Year);
        var analyst = TestUsers.CreateStaff(RoleIds.DataAnalyst, "changes");
        var outsider = TestUsers.CreateStaff(RoleIds.DataAnalyst, "changes-out");
        var (one, two, three) = (Ssn(), Ssn(), Ssn());

        async Task<int> Validate(TestImports.Workbook book, string label)
        {
            var id = await LogFile(book.Save(label), employer, analyst);
            await Service<IFileProcessingService>().ProcessFileAsync(id, StandardTemplate);
            Assign(id, analyst);
            await Service<IDataProcessingService>().ExtractAndValidateDataAsync(id);
            Assert.True(Log(id).ValidationStatusId is VS.Clean or VS.ValidWithWarnings, Errors(id));
            return id;
        }

        var first = await Validate(GoodFile(employer, one, two), "changes1");
        await Service<IDataProcessingService>().ImportToLiveAsync(first, "Merge", analyst.UserId.ToString());
        Assert.Equal(WS.Complete, Log(first).WorkflowStatusId);

        var book = GoodFile(employer, one, three);           // two is missing, three is new
        var leaver = book.Sheets["Employee"][0];
        leaver["Termination Date"] = $"{Year}-06-30";
        leaver["Status End Date"] = $"{Year}-06-30";
        leaver["City"] = "NEWTOWN";
        var second = await Validate(book, "changes2");

        var changes = await Service<IDataProcessingRepository>().GetImportChangesAsync(second, null, null, 1, null);
        int Count(string kind) => changes.Kinds.Single(k => k.Kind == kind).Changes;
        Assert.Equal(1, Count("NEW_EMPLOYEE"));
        Assert.Equal(1, Count("TERMINATED"));
        Assert.Equal(1, Count("MISSING"));
        Assert.Equal(0, Count("STATUS"));
        var term = changes.Items.Single(c => c.Kind == "TERMINATED");
        Assert.Equal(("Termination date", (string?)null, $"06/30/{Year}", "Leaving"), (term.Field, term.OldValue, term.NewValue, term.Note));
        Assert.Contains(changes.Items, c => c.Kind == "DETAILS" && c.Field == "City" && c.NewValue == "NEWTOWN" && c.OldValue == "TESTVILLE");
        Assert.EndsWith(Digits(two)[^4..], changes.Items.Single(c => c.Kind == "MISSING").Name);
        Assert.EndsWith(Digits(three)[^4..], changes.Items.Single(c => c.Kind == "NEW_EMPLOYEE").Name);

        using (var client = await _app.SignedInAs(analyst))
        {
            var html = await (await client.AjaxAsync($"/FileUpload/AnalysisChanges?id={second}")).Content.ReadAsStringAsync();
            Assert.Contains("is not in this file", html);
            Assert.Contains("NEWTOWN", html);
            var leavers = await (await client.AjaxAsync($"/FileUpload/AnalysisChanges?id={second}&kind=TERMINATED")).Content.ReadAsStringAsync();
            Assert.DoesNotContain("NEWTOWN", leavers);
            Assert.Contains("Leaving", leavers);
        }
        using (var other = await _app.SignedInAs(outsider))
            Assert.Equal(System.Net.HttpStatusCode.NotFound, (await other.AjaxAsync($"/FileUpload/AnalysisChanges?id={second}")).StatusCode);
    }

    // ---- Why this code? (script 058) --------------------------------------------------------

    /// <summary>
    /// After the codes are generated, each month's working values are kept and explain the codes
    /// on file; the page answers someone who may see the employee and nobody else.
    /// </summary>
    [LocalDbFact]
    public async Task GeneratedCodesComeWithTheirReasons()
    {
        var employer = TestImports.CreateEmployer(Year);
        var analyst = TestUsers.CreateStaff(RoleIds.DataAnalyst, "why");
        var fileLogId = await LogFile(GoodFile(employer, Ssn(), Ssn()).Save("why"), employer, analyst);
        await Service<IFileProcessingService>().ProcessFileAsync(fileLogId, StandardTemplate);
        Assign(fileLogId, analyst);
        await Service<IDataProcessingService>().ExtractAndValidateDataAsync(fileLogId);
        await Service<IDataProcessingService>().ImportToLiveAsync(fileLogId, "Merge", analyst.UserId.ToString());
        Assert.Equal(WS.Complete, Log(fileLogId).WorkflowStatusId);

        var aca = Service<IACALogicService>();
        await aca.GenerateCodesForEmployerAsync(employer.Id, Year);
        var employeeId = LocalDb.Scalar<int>("SELECT TOP 1 id FROM dbo.Employee WHERE EmployerId = @id ORDER BY id", new { id = employer.Id });

        var why = await aca.GetCodeExplanationAsync(employeeId, Year);
        Assert.NotNull(why);
        Assert.True(why!.HasDetail);
        Assert.Equal(113.20m, why.MonthlyFpl);
        Assert.All(why.Months, m =>
        {
            Assert.NotNull(m.Detail);
            Assert.NotEmpty(m.Why14);
            Assert.False(m.ChangedByHand, $"{m.MonthName}: filed {m.Line14}/{m.Line16}, calculated {m.Calculated14}/{m.Calculated16}");
        });
        // Enrolled all year in the family plan, hired in 2019: 1E and 2C every month.
        Assert.All(why.Months, m => Assert.Equal(("1E", "2C"), (m.Line14, m.Line16)));
        Assert.Contains("ACA360TESTS PPO", why.Months[0].Why14[0]);

        using (var client = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.SuperAdmin, "why-admin")))
        {
            var html = await (await client.AjaxAsync($"/ACA/CodeExplanation?employeeId={employeeId}&year={Year}")).Content.ReadAsStringAsync();
            Assert.Contains("2C: enrolled in the coverage offered.", html);
            Assert.Contains("December", html);
        }
        using (var other = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.AccountManager, "why-out")))
            Assert.Equal(System.Net.HttpStatusCode.Forbidden,
                (await other.AjaxAsync($"/ACA/CodeExplanation?employeeId={employeeId}&year={Year}")).StatusCode);
    }

    // ---- Error Triage (script 056) ----------------------------------------------------------

    private static async Task<HttpResponseMessage> PostJson(AcaClient client, string url, object body, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = System.Net.Http.Json.JsonContent.Create(body) };
        request.Headers.Add("RequestVerificationToken", token);
        return await client.Http.SendAsync(request);
    }

    private static async Task<System.Text.Json.JsonElement> Json(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    /// <summary>
    /// The Correction Editor: someone who cannot see the file cannot open or change it; only data
    /// columns can be changed; a change is kept with who made it; re-validating (each save does)
    /// keeps the skipped warnings skipped and the earlier auto-corrections on record; only a
    /// warning can be skipped; an imported file is read-only.
    /// </summary>
    [LocalDbFact]
    public async Task TheCorrectionEditorKeepsToItsFileAndRemembersWhatHappened()
    {
        var employer = TestImports.CreateEmployer(Year);
        var analyst = TestUsers.CreateStaff(RoleIds.DataAnalyst, "triage");
        var outsider = TestUsers.CreateStaff(RoleIds.DataAnalyst, "triage-out");
        var book = SmallBook(employer, 4, "Monthly", 57);
        var e = book.Rows(DummyDataGenerator.EmployeeSheet);
        e[0]["Employee Legal Last Name"] = "O'BRIEN";                    // auto-corrected
        e[1]["State/ Province"] = "NJ"; e[1]["Zip"] = "98101";           // a warning (107)
        e[2]["Employee Legal First Name"] = null;                         // an error (44)
        var fileLogId = await ValidateAsync(book, employer, analyst, "triage");
        Assert.Equal(VS.DataError, Log(fileLogId).ValidationStatusId);

        using var db = LocalDb.Open();
        long RowOf(int i) => db.ExecuteScalar<long>(
            "SELECT StagingEmployeeId FROM dbo.Staging_Employees WHERE FileLogId = @fileLogId AND REPLACE(EmployeeSSN, '-', '') = @ssn",
            new { fileLogId, ssn = Digits(e[i]["Employee SSN"]) });
        long IssueOf(int i, int rule) => db.ExecuteScalar<long>(
            "SELECT StagingRowErrorId FROM dbo.StagingRowErrors WHERE FileLogId = @fileLogId AND StagingRowId = @row AND RuleId = @rule",
            new { fileLogId, row = RowOf(i), rule });
        var missingName = RowOf(2);
        object Edit(string column, string value) => new
        {
            FileLogId = fileLogId, TableName = "Staging_Employees", RowId = missingName,
            Values = new Dictionary<string, string> { [column] = value },
        };

        // Someone the File Upload list does not show this file to gets nothing.
        using (var other = await _app.SignedInAs(outsider))
        {
            Assert.Equal(System.Net.HttpStatusCode.NotFound, (await other.PageAsync($"/ErrorTriage/ErrorTriageDashboard/{fileLogId}")).StatusCode);
            Assert.Equal(System.Net.HttpStatusCode.NotFound, (await other.AjaxAsync(
                $"/ErrorTriage/GetStagingRow?fileLogId={fileLogId}&tableName=Staging_Employees&rowId={missingName}")).StatusCode);
            var token = AcaClient.Token(await (await other.GetAsync("/Calendar/Index")).Content.ReadAsStringAsync());
            Assert.Equal(System.Net.HttpStatusCode.NotFound,
                (await PostJson(other, "/ErrorTriage/UpdateField", Edit("EmployeeLegalFirstName", "INTRUDER"), token)).StatusCode);
            Assert.Equal(System.Net.HttpStatusCode.NotFound, (await PostJson(other, "/ErrorTriage/UpdateErrorStatus",
                new { FileLogId = fileLogId, StagingRowErrorId = IssueOf(1, 107), Status = "Skipped" }, token)).StatusCode);
        }

        using var client = await _app.SignedInAs(analyst);
        var page = await client.PageAsync($"/ErrorTriage/ErrorTriageDashboard/{fileLogId}");
        Assert.Equal(System.Net.HttpStatusCode.OK, page.StatusCode);
        var tokenA = AcaClient.Token(await page.Content.ReadAsStringAsync());

        // A row of another file, or a column that is not data, is refused.
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.AjaxAsync(
            $"/ErrorTriage/GetStagingRow?fileLogId={fileLogId}&tableName=Staging_Employees&rowId={missingName + 1_000_000_000}")).StatusCode);
        var refused = await Json(await PostJson(client, "/ErrorTriage/UpdateField", Edit("FileLogId", "1"), tokenA));
        Assert.False(refused.GetProperty("isSuccess").GetBoolean());
        Assert.Equal(fileLogId, db.ExecuteScalar<int>("SELECT FileLogId FROM dbo.Staging_Employees WHERE StagingEmployeeId = @missingName", new { missingName }));

        // Only a warning can be acknowledged, and only with a reason (script 061).
        var noReason = await PostJson(client, "/ErrorTriage/UpdateErrorStatus",
            new { FileLogId = fileLogId, StagingRowErrorId = IssueOf(1, 107), Status = "Skipped" }, tokenA);
        Assert.True(noReason.StatusCode == System.Net.HttpStatusCode.BadRequest, $"{noReason.StatusCode}: {await noReason.Content.ReadAsStringAsync()}");
        Assert.False((await Json(await PostJson(client, "/ErrorTriage/UpdateErrorStatus",
            new { FileLogId = fileLogId, StagingRowErrorId = IssueOf(2, 44), Status = "Skipped", Reason = "x" }, tokenA))).GetProperty("success").GetBoolean());
        Assert.True((await Json(await PostJson(client, "/ErrorTriage/UpdateErrorStatus",
            new { FileLogId = fileLogId, StagingRowErrorId = IssueOf(1, 107), Status = "Skipped", Reason = "Correct as it is" }, tokenA))).GetProperty("success").GetBoolean());
        // Stored as typed: the status the list looks for, and the reason in its own case.
        Assert.Equal(("Skipped", "Correct as it is"), db.QuerySingle<(string, string)>(
            @"SELECT E.Status, S.Reason FROM dbo.StagingRowErrors E JOIN dbo.StagingIssueSkips S ON S.FileLogId = E.FileLogId AND S.StagingRowId = E.StagingRowId AND S.RuleId = E.RuleId
              WHERE E.StagingRowErrorId = @id", new { id = IssueOf(1, 107) }));

        // Fix the error: the file re-validates, the warning stays skipped, both corrections are on record.
        var saved = await Json(await PostJson(client, "/ErrorTriage/UpdateField", Edit("EmployeeLegalFirstName", "FIXED"), tokenA));
        Assert.True(saved.GetProperty("isSuccess").GetBoolean(), saved.ToString());
        Assert.Equal(0, saved.GetProperty("newErrorCount").GetInt32());
        Assert.Equal(VS.ValidWithWarnings, Log(fileLogId).ValidationStatusId);
        Assert.Equal("Skipped", db.ExecuteScalar<string>("SELECT Status FROM dbo.StagingRowErrors WHERE StagingRowErrorId = @id", new { id = IssueOf(1, 107) }));

        var changes = db.Query<(string Source, string ColumnName, string? OldValue, string NewValue, string? ChangedBy)>(
            "SELECT Source, ColumnName, OldValue, NewValue, ChangedBy FROM dbo.StagingAutoCorrections WHERE FileLogId = @fileLogId ORDER BY Source",
            new { fileLogId }).ToList();
        Assert.Equal(2, changes.Count);
        Assert.Equal(("Auto", "EmployeeLegalLastName", "O'BRIEN", "OBRIEN"), (changes[0].Source, changes[0].ColumnName, changes[0].OldValue, changes[0].NewValue));
        Assert.Equal(("Manual", "EmployeeLegalFirstName", "FIXED"), (changes[1].Source, changes[1].ColumnName, changes[1].NewValue));
        Assert.True(string.IsNullOrEmpty(changes[1].OldValue));
        Assert.False(string.IsNullOrEmpty(changes[1].ChangedBy));
        Assert.Equal(1, db.ExecuteScalar<int>(
            @"SELECT COUNT(*) FROM dbo.StagingRowErrors E JOIN dbo.ValidationRules R ON R.RuleId = E.RuleId
              WHERE E.FileLogId = @fileLogId AND R.RuleCode = 'AUTO_CORRECTED'", new { fileLogId }));

        // Saving the same value again changes nothing and records nothing.
        Assert.True((await Json(await PostJson(client, "/ErrorTriage/UpdateField", Edit("EmployeeLegalFirstName", "FIXED"), tokenA)))
            .GetProperty("isSuccess").GetBoolean());
        Assert.Equal(2, db.ExecuteScalar<int>("SELECT COUNT(*) FROM dbo.StagingAutoCorrections WHERE FileLogId = @fileLogId", new { fileLogId }));

        // The Corrections tab lists both.
        var corrections = await (await client.AjaxAsync($"/ErrorTriage/CorrectionsPartial?id={fileLogId}")).Content.ReadAsStringAsync();
        Assert.Contains("OBRIEN", corrections);
        Assert.Contains("FIXED", corrections);
        Assert.Contains("Made in the editor", corrections);

        // Once imported, the rows are read-only.
        LocalDb.Execute("UPDATE dbo.UploadedFileLog SET WorkflowStatusId = @ws WHERE FileLogId = @fileLogId", new { ws = WS.Complete, fileLogId });
        var late = await Json(await PostJson(client, "/ErrorTriage/UpdateField", Edit("EmployeeLegalFirstName", "LATE"), tokenA));
        Assert.False(late.GetProperty("isSuccess").GetBoolean());
        Assert.Equal("FIXED", db.ExecuteScalar<string>(
            "SELECT EmployeeLegalFirstName FROM dbo.Staging_Employees WHERE StagingEmployeeId = @missingName", new { missingName }));
    }
}
