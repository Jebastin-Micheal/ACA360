using ACA360.BusinessLogic.Interfaces;
using ACA360.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ACA360.Tests.Integration;

/// <summary>Year over year (script 064): the Penalty Risk comparison and the dashboard's "exposure growing" list.</summary>
[Collection(AppCollection.Name)]
public class YearOverYearTests : IDisposable
{
    private readonly AcaWebFactory _app;

    public YearOverYearTests(AcaWebFactory app) => _app = app;

    public void Dispose()
    {
        TestEmployees.RemoveAll();
        TestImports.RemoveAll();
    }

    [LocalDbFact]
    public async Task ThisYearIsComparedWithLastYear()
    {
        // The same employer (EIN) in 2024 and 2025: one employee offered coverage in 2024; in 2025
        // another employee is not offered coverage all year, with an estimated penalty of 2,900.
        var lastYear = TestImports.CreateEmployer(2024);
        var thisYear = TestImports.CreateEmployer(2025);
        LocalDb.Execute("UPDATE dbo.Employer SET taxid = @ein WHERE id = @id", new { ein = lastYear.Ein, id = thisYear.Id });
        TestEmployees.Create(lastYear.Id, 2024, "YOYONE");
        TestEmployees.Create(thisYear.Id, 2025, "YOYONE");
        var uncovered = TestEmployees.Create(thisYear.Id, 2025, "YOYTWO");
        LocalDb.Execute(
            @"UPDATE dbo.EmployeeCode SET ALLM_COC = '1H', ALLM_LCMP = NULL, ALLM_SHC = NULL, IsAtPenaltyRisk = 1, PenaltyMonths = 12, EstimatedPenalty = 2900
              WHERE employeeId = @id", new { id = uncovered.Id });

        using var scope = _app.Services.CreateScope();
        var aca = scope.ServiceProvider.GetRequiredService<IACALogicService>();
        var (now, before) = await aca.GetYearOverYearAsync(thisYear.Id, 2025);
        Assert.NotNull(before);
        Assert.Equal((2, 12, 1, 2900m), (now.Employees, now.NotOfferedMonths, now.AtRiskEmployees, now.EstimatedPenalty));
        Assert.Equal((1, 0, 12, 0m), (before!.Employees, before.NotOfferedMonths, before.OfferedMonths, before.EstimatedPenalty));

        var growth = Assert.Single(await aca.GetExposureGrowthAsync(2025, new[] { thisYear.Id }));
        Assert.Equal((thisYear.Id, 0m, 2900m, 2900m), (growth.EmployerId, growth.LastYear, growth.ThisYear, growth.Growth));

        // The Penalty Risk page shows the comparison.
        using var admin = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.SuperAdmin, "yoy"));
        var page = await (await admin.PageAsync($"/ACA/PenaltyRisk?employerId={thisYear.Id}&year=2025")).Content.ReadAsStringAsync();
        Assert.Contains("Compared with 2024", page);
        // Opening the page re-assesses the risk, so the estimate is the page's own; it rose from nothing.
        var penaltyRow = page[page.IndexOf("Estimated penalty</th>", StringComparison.Ordinal)..];
        Assert.Contains("<td class=\"text-end small\">$0</td>", penaltyRow[..200]);
        Assert.Contains("&#x2B;$", penaltyRow[..400]);   // "+$…" (Razor encodes the plus sign)

        // An Account Manager with only this employer sees it on the dashboard.
        var am = TestUsers.CreateStaff(RoleIds.AccountManager, "yoy-am");
        TestUsers.Assign(am, thisYear.Id);
        using var amClient = await _app.SignedInAs(am);
        var dashboard = await (await amClient.PageAsync($"/Dashboard?year=2025")).Content.ReadAsStringAsync();
        Assert.Contains("Penalty exposure growing", dashboard);
        Assert.Contains(thisYear.Name, dashboard);
    }
}
