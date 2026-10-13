using System.Net;
using ACA360.BusinessLogic.Interfaces;
using ACA360.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ACA360.Tests.Integration;

/// <summary>Filing deadlines (script 063): edited by administrators, counted down on the dashboard, shown in the Calendar.</summary>
[Collection(AppCollection.Name)]
public class FilingDeadlineTests
{
    private const string Title = "ACA360TESTS deadline";
    private readonly AcaWebFactory _app;

    public FilingDeadlineTests(AcaWebFactory app) => _app = app;

    [LocalDbFact]
    public async Task AnAdministratorAddsADeadlineThatEveryoneThenSees()
    {
        var due = DateTime.Today.AddDays(1);
        try
        {
            using var admin = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.Admin, "deadlines"));
            var page = await (await admin.PageAsync("/FilingDeadlines")).Content.ReadAsStringAsync();
            Assert.Contains("Tax year 2025", page);
            Assert.Contains("Check", page);   // seeded state dates wait for confirmation

            var saved = await admin.PostFormAsync("/FilingDeadlines/Save", "/FilingDeadlines", new Dictionary<string, string>
            {
                ["FilingDeadlineId"] = "0", ["FilingYear"] = "2026", ["Jurisdiction"] = "zz", ["Kind"] = "Other",
                ["Title"] = Title, ["DueDate"] = due.ToString("yyyy-MM-dd"), ["Notes"] = "Made by a test",
            });
            Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
            var id = LocalDb.Scalar<int>("SELECT FilingDeadlineId FROM dbo.FilingDeadlines WHERE Title = @Title AND IsActive = 1", new { Title });
            Assert.True(id > 0);
            Assert.Equal("ZZ", LocalDb.Scalar<string>("SELECT Jurisdiction FROM dbo.FilingDeadlines WHERE FilingDeadlineId = @id", new { id }));

            // Every role sees it on the dashboard and in the Calendar.
            using var analyst = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.DataAnalyst, "deadlines-da"));
            var dashboard = await (await analyst.PageAsync("/Dashboard")).Content.ReadAsStringAsync();
            Assert.Contains("Upcoming deadlines", dashboard);
            Assert.Contains(Title, dashboard);
            Assert.Contains("1 day", dashboard);
            var events = await analyst.JsonAsync("/Calendar/GetEvents");
            Assert.Contains(events.EnumerateArray(), e => e.GetProperty("id").GetString() == $"deadline-{id}");
            // ...but cannot edit the dates.
            Assert.NotEqual(HttpStatusCode.OK, (await analyst.PageAsync("/FilingDeadlines")).StatusCode);

            var removed = await admin.PostFormAsync("/FilingDeadlines/Delete", "/FilingDeadlines", new Dictionary<string, string> { ["id"] = id.ToString() });
            Assert.Equal(HttpStatusCode.Redirect, removed.StatusCode);
            Assert.False(LocalDb.Scalar<bool>("SELECT IsActive FROM dbo.FilingDeadlines WHERE FilingDeadlineId = @id", new { id }));
        }
        finally
        {
            LocalDb.Execute("DELETE FROM dbo.FilingDeadlines WHERE Title = @Title", new { Title });
        }
    }

    /// <summary>The Employer dashboard's mailing and e-filing countdown reads the IRS dates (it assumed 1 and 31 March).</summary>
    [LocalDbFact]
    public async Task TheEmployerDashboardCountsDownToTheIrsDates()
    {
        var employer = LocalDb.Scalar<int>("SELECT TOP 1 id FROM dbo.Employer WHERE filingYear = 2025 ORDER BY id");
        if (employer == 0) return;
        using var scope = _app.Services.CreateScope();
        var dto = await scope.ServiceProvider.GetRequiredService<IDashboardService>().GetEmployerDashboardParityAsync(employer, 2025, 0);
        Assert.Equal(new DateTime(2026, 3, 2), dto.MailingDeadline);
        Assert.Equal(new DateTime(2026, 3, 31), dto.EfilingDeadline);
    }
}
