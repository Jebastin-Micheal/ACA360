using ACA360.BusinessLogic.Services;
using ACA360.Core.Models;
using static ACA360.BusinessLogic.Services.DummyDataGenerator;

namespace ACA360.Tests.Unit.Services;

/// <summary>
/// The Dummy Data page's generator, without the database: how many rows of each kind, unique
/// SSNs and EINs, every affiliate with an employee, the shares of warnings and errors, and the
/// same seed giving the same file. (ImportPipelineTests runs its files through validation.)
/// </summary>
public class DummyDataGeneratorTests
{
    private static readonly DummyEmployer Employer = new(7, "Acme, Inc. & Sons", "123456789");

    private static DummyDataRequest Request(int employees = 200, int affiliates = 3, int warn = 0, int err = 0) => new()
    {
        EmployerIds = { 7 }, FilingYear = 2025, Affiliates = affiliates, Plans = 4, Employees = employees,
        DependentsMin = 1, DependentsMax = 3, WarningPercent = warn, ErrorPercent = err,
    };

    private static DummyWorkbook Make(DummyDataRequest request, int seed = 1, ISet<string>? taken = null) =>
        Generate(request, Employer, new Random(seed), taken ?? new HashSet<string>(), new HashSet<string>());

    [Fact]
    public void TheWorkbookHasTheRowsAskedFor()
    {
        var book = Make(Request());
        Assert.Equal(4, book.Rows(EmployerSheet).Count);   // the main employer and 3 affiliates
        Assert.Equal(4, book.Rows(PlanSheet).Count);
        Assert.Equal(4, book.Rows(PremiumSheet).Count);
        Assert.Equal(200, book.Rows(EmployeeSheet).Count);
        Assert.Equal(book.Rows(DependentSheet).Count, book.Summary.Dependents);
        Assert.Equal(200, book.Summary.CleanEmployees);
        Assert.All(book.Rows(EmployeeSheet).Concat(book.Rows(DependentSheet)), r => Assert.Equal("12-3456789", r["Primary EIN"]));
    }

    [Fact]
    public void TheMainEmployerNamesItselfAndItsNameIsCleaned()
    {
        var main = Make(Request()).Rows(EmployerSheet)[0];
        Assert.Equal("12-3456789", main["Affiliated EIN"]);
        Assert.Equal("ACME INC & SONS", main["Employer Name"]);
    }

    [Fact]
    public void EveryAffiliateHasAnEmployeeAndNoneUsesAnEinAlreadyTaken()
    {
        var taken = new HashSet<string> { "123456789" };
        var before = taken.Count;
        var book = Make(Request(), taken: taken);
        var affiliates = book.Rows(EmployerSheet).Skip(1).Select(r => (string)r["Affiliated EIN"]!).ToList();
        Assert.Equal(3, affiliates.Distinct().Count());
        Assert.All(affiliates, ein => Assert.Matches(@"^\d{2}-\d{7}$", ein));
        Assert.Equal(before + 3, taken.Count);   // each made-up EIN was new and is now taken
        var associated = book.Rows(EmployeeSheet).Select(r => (string)r["EIN Associated with EE (May be an Affiliate EIN)"]!).ToHashSet();
        Assert.All(affiliates, ein => Assert.Contains(ein, associated));
        Assert.All(book.Rows(EmployerSheet).Skip(1), r => Assert.StartsWith("DUMMY ", (string)r["Employer Name"]!));
    }

    [Fact]
    public void SsnsAreUniqueAndAcceptedByTheImport()
    {
        var book = Make(Request(employees: 2000));
        var ssns = book.Rows(EmployeeSheet).Select(r => (string)r["Employee SSN"]!)
            .Concat(book.Rows(DependentSheet).Select(r => (string)r["Dependent SSN"]!)).ToList();
        Assert.Equal(ssns.Count, ssns.Distinct().Count());
        Assert.All(ssns, s =>
        {
            Assert.Matches(@"^\d{3}-\d{2}-\d{4}$", s);
            Assert.False(s.StartsWith("000") || s.StartsWith("666") || s.StartsWith('9'), s);
        });
    }

    [Fact]
    public void DependentsBelongToCoveredEmployeesAndStartWithThem()
    {
        var book = Make(Request());
        var covered = book.Rows(EmployeeSheet).Where(r => (int)r["Coverage Elected?"]! == 1)
            .ToDictionary(r => (string)r["Employee SSN"]!, r => (string)r["Coverage Start Date"]!);
        Assert.All(book.Rows(DependentSheet), d =>
        {
            Assert.True(covered.TryGetValue((string)d["Employee SSN"]!, out var start));
            Assert.Equal(start, d["Dependent Coverage Start Date"]);
            Assert.True(DateTime.Parse((string)d["Dependent Birthdate"]!, System.Globalization.CultureInfo.InvariantCulture)
                        < DateTime.Parse(start, System.Globalization.CultureInfo.InvariantCulture));
        });
        var perEmployee = book.Rows(DependentSheet).GroupBy(d => d["Employee SSN"]).Select(g => g.Count()).ToList();
        Assert.All(perEmployee, n => Assert.InRange(n, 1, 3));
    }

    [Theory]
    [InlineData(10, 0, 20, 0)]
    [InlineData(0, 25, 0, 50)]
    [InlineData(30, 20, 60, 40)]
    public void WarningsAndErrorsComeInTheSharesAskedFor(int warn, int err, int warnings, int errors)
    {
        var book = Make(Request(warn: warn, err: err));
        // Plan warnings add one extra plan each, on top of the employees with a warning.
        int planKinds = warn > 0 ? Issues.Count(i => i.Target == DummyIssueTarget.Plan) : 0;
        Assert.Equal(warnings + planKinds, book.Summary.Warnings.Values.Sum());
        Assert.Equal(4 + planKinds, book.Summary.Plans);
        Assert.Equal(errors, book.Summary.Errors.Values.Sum());
        Assert.Equal(200 - warnings - errors, book.Summary.CleanEmployees);
        // Taken in turn: with enough rows, every kind shows up.
        if (warnings >= Issues.Count(i => !i.IsError)) Assert.Equal(Issues.Count(i => !i.IsError), book.Summary.Warnings.Count);
        if (errors >= Issues.Count(i => i.IsError)) Assert.Equal(Issues.Count(i => i.IsError), book.Summary.Errors.Count);
    }

    [Fact]
    public void OnlyTheTickedKindsAreUsed()
    {
        var request = Request(warn: 20, err: 20);
        request.IssueCodes = new() { "ZIP_STATE", "SSN_000" };
        var book = Make(request);
        Assert.Equal(new[] { "ZIP code does not match the state" }, book.Summary.Warnings.Keys);
        Assert.Equal(new[] { "SSN starting with 000" }, book.Summary.Errors.Keys);
        Assert.Equal(4, book.Summary.Plans);   // no plan kind ticked: no extra plan
    }

    [Fact]
    public void AgeBandingKeepsTheAffordabilityWarning()
    {
        // Age bands are checked for affordability since script 055.
        var request = Request(warn: 50);
        request.Banding = "Age";
        Assert.Contains(ChosenIssues(request), i => i.Code == "NOT_AFFORDABLE");
        Assert.Contains("Salary too low for the plan's premium (affordability)", Make(request).Summary.Warnings.Keys);
    }

    [Theory]
    [InlineData("Monthly", 1)]
    [InlineData("Salary", 3)]
    [InlineData("Age", 5)]
    public void PremiumsHaveOneRowPerBand(string banding, int bandsPerPlan)
    {
        var request = Request();
        request.Banding = banding;
        var book = Make(request);
        Assert.Equal(4 * bandsPerPlan, book.Rows(PremiumSheet).Count);
        Assert.All(book.Rows(PremiumSheet), r => Assert.Equal(banding == "Monthly" ? "Monthly" : banding.ToUpperInvariant(), r["Banding Type"]));
    }

    [Fact]
    public void TheEmployeeMixComesInTheSharesAskedFor()
    {
        var request = Request();
        request.PartTimePercent = 10; request.NewHirePercent = 20; request.UnionPercent = 5; request.CoveragePercent = 100;
        request.TerminatedPercent = 25; request.CobraPercent = 40; request.RetireePercent = 20;
        var book = Make(request);
        var mix = book.Summary.Mix;
        Assert.Equal(20, mix["Part time"]);
        Assert.Equal(40, mix["New hires"]);
        Assert.Equal(10, mix["Union"]);
        Assert.Equal(190, mix["Elected coverage"]);   // everyone but the union members
        Assert.InRange(mix["Left in the year"], 1, 50);
        var employees = book.Rows(EmployeeSheet);
        Assert.Equal(20, employees.Count(r => (int)r["Status"]! == 0));
        // Leavers' coverage ends at the end of the month they leave; COBRA and retiree coverage start the day after.
        Assert.All(employees.Where(r => r["Termination Date"] != null && (int)r["Coverage Elected?"]! == 1), r =>
        {
            var term = DateTime.Parse((string)r["Termination Date"]!, System.Globalization.CultureInfo.InvariantCulture);
            var end = DateTime.Parse((string)r["Coverage End Date"]!, System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(new DateTime(term.Year, term.Month, DateTime.DaysInMonth(term.Year, term.Month)), end);
            if ((int)r["COBRA Benefits elected?"]! == 1) Assert.Equal(Date(end.AddDays(1)), r["COBRA Coverage Effective Date"]);
            if ((int)r["Retiree Benefits elected?"]! == 1) Assert.Equal(Date(end.AddDays(1)), r["Retiree Plan Start Date"]);
        });
        Assert.Equal(mix["COBRA"], employees.Count(r => (int)r["COBRA Benefits elected?"]! == 1));
    }

    [Fact]
    public void NewNamesAreCleanedAndDuplicatesDropped() =>
        Assert.Equal(new[] { "NEW CO LLC", "OTHER (US) INC" }, NewNames("New Co., LLC\r\n\r\nnew co llc\nOther (US) Inc."));

    [Fact]
    public void TheSameSeedMakesTheSameFile()
    {
        var a = Make(Request(warn: 10, err: 10), seed: 99);
        var b = Make(Request(warn: 10, err: 10), seed: 99);
        var c = Make(Request(warn: 10, err: 10), seed: 100);
        static string Text(DummyWorkbook w) => string.Join("|", w.Sheets.SelectMany(s => s.Value.SelectMany(r => r.Values.Select(v => v?.ToString()))));
        Assert.Equal(Text(a), Text(b));
        Assert.NotEqual(Text(a), Text(c));
    }

    [Theory]
    [InlineData(0, 100, 1, 0, 0, 0, "Choose an employer or type a new employer name.")]
    [InlineData(1, 100, 1, 3, 2, 0, "smallest number of dependents")]
    [InlineData(1, 100, 1, 0, 0, 70, "100%")]
    [InlineData(1, 3, 3, 0, 0, 0, "Each affiliate needs at least one employee")]
    public void BadRequestsAreRefusedInPlainWords(int employers, int employees, int affiliates, int depMin, int depMax, int percent, string expected)
    {
        var request = new DummyDataRequest
        {
            EmployerIds = Enumerable.Range(1, employers).ToList(), FilingYear = 2025, Employees = employees, Affiliates = affiliates,
            DependentsMin = depMin, DependentsMax = depMax, WarningPercent = percent, ErrorPercent = percent,
        };
        Assert.Contains(Check(request), p => p.Contains(expected));
    }

    [Fact]
    public void AGoodRequestHasNoProblems() => Assert.Empty(Check(Request()));
}
