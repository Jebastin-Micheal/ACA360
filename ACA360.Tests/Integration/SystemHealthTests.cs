using System.Net;
using ACA360.Logging.Interfaces;
using ACA360.Tests.Integration.Infrastructure;
using ACA360.Web.Controllers;
using Microsoft.Extensions.DependencyInjection;

namespace ACA360.Tests.Integration;

/// <summary>Administration > System Health (script 062).</summary>
[Collection(AppCollection.Name)]
public class SystemHealthTests
{
    private readonly AcaWebFactory _app;

    public SystemHealthTests(AcaWebFactory app) => _app = app;

    [Fact]
    public void SlowPagesCountTogetherWhateverTheId()
    {
        Assert.Equal("GET /ErrorTriage/ErrorTriageDashboard/{id}", SystemHealthController.PageOf("GET", "/ErrorTriage/ErrorTriageDashboard/1154"));
        Assert.Equal("GET /Employer/{id}/Plans/{id}", SystemHealthController.PageOf("GET", "/Employer/12/Plans/7"));
        Assert.Equal("POST /ErrorTriage/UpdateField", SystemHealthController.PageOf("POST", "/ErrorTriage/UpdateField"));
    }

    [LocalDbFact]
    public async Task AdministratorsSeeTheHealthPageAndNobodyElse()
    {
        // An information line is saved as Info, not as an error (it used to count as one).
        var marker = "ACA360TESTS health " + Guid.NewGuid().ToString("N")[..8];
        using (var scope = _app.Services.CreateScope())
            scope.ServiceProvider.GetRequiredService<ILoggerService>().LogInfo(marker, "SystemHealthTests", "", "");
        try
        {
            Assert.Equal("Info", LocalDb.Scalar<string>("SELECT TOP 1 Level FROM dbo.tbl_ErrorLogs WHERE Message = @marker", new { marker }));

            foreach (var role in new[] { RoleIds.SuperAdmin, RoleIds.Admin })
            {
                using var client = await _app.SignedInAs(TestUsers.CreateStaff(role, "health"));
                var response = await client.PageAsync("/SystemHealth");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var html = await response.Content.ReadAsStringAsync();
                Assert.Contains("Slowest pages", html);
                Assert.Contains("Last full backup", html);
                Assert.False(html.Contains("could not be read"), System.Text.RegularExpressions.Regex.Match(html, "The database figures could not be read[^<]*").Value);
            }
            using (var analyst = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.DataAnalyst, "health-no")))
            {
                var response = await analyst.PageAsync("/SystemHealth");
                Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
            }
        }
        finally
        {
            LocalDb.Execute("DELETE FROM dbo.tbl_ErrorLogs WHERE Message = @marker", new { marker });
        }
    }
}
