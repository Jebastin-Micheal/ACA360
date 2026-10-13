using System.Net;
using ACA360.Tests.Integration.Infrastructure;
using Dapper;

namespace ACA360.Tests.Integration;

/// <summary>
/// Signing in as an Employer (a login made from Tracker > Employer > Portal login, pointing at
/// its employer) and as a Broker (a login with a BrokerAccounts record).
/// </summary>
[Collection(AppCollection.Name)]
public class PortalSignInTests
{
    private readonly AcaWebFactory _app;

    public PortalSignInTests(AcaWebFactory app) => _app = app;

    /// <summary>Two different live main employers, and one employee of each.</summary>
    private static (int A, int EmployeeOfA, int B, int EmployeeOfB) Employers()
    {
        using var db = LocalDb.Open();
        var ids = db.Query<int>(
            @"SELECT TOP 2 e.id FROM dbo.Employer e
              WHERE ISNULL(e.IsDeleted, 0) = 0 AND ISNULL(e.companyId, 0) = 0
                AND EXISTS (SELECT 1 FROM dbo.Employee x WHERE x.EmployerId = e.id AND ISNULL(x.IsDeleted, 0) = 0)
              ORDER BY e.id").ToList();
        if (ids.Count < 2) throw new InvalidOperationException("The local database needs two employers with employees for these tests.");
        int EmployeeOf(int employerId) => LocalDb.Scalar<int>(
            "SELECT TOP 1 id FROM dbo.Employee WHERE EmployerId = @employerId AND ISNULL(IsDeleted, 0) = 0 ORDER BY id", new { employerId });
        return (ids[0], EmployeeOf(ids[0]), ids[1], EmployeeOf(ids[1]));
    }

    private static string EmployerName(int id) => LocalDb.Scalar<string>("SELECT name FROM dbo.Employer WHERE id = @id", new { id });

    private static int YearOf(int employerId) => LocalDb.Scalar<int>("SELECT filingYear FROM dbo.Employer WHERE id = @employerId", new { employerId });

    private static bool Refused(HttpResponseMessage response) =>
        response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound
        || (response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location!.OriginalString.Contains("AccessDenied"));

    private static async Task<string> Html(HttpResponseMessage response) =>
        WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

    // ---- Employer

    [LocalDbFact]
    public async Task AnEmployerSignsInStraightToTheirOwnEmployer()
    {
        var (a, _, _, _) = Employers();
        var user = TestUsers.CreateEmployer(a, "employer");
        using var client = _app.NewClient();

        var response = await client.SignInAsync(user.Email, user.Password);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Dashboard", response.Headers.Location!.OriginalString);   // one employer: no choosing

        var dashboard = await client.PageAsync("/Dashboard");
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        Assert.Contains(EmployerName(a), await Html(dashboard), StringComparison.OrdinalIgnoreCase);
    }

    [LocalDbFact]
    public async Task AnEmployerSeesTheirOwnEmployeesButNotAnotherEmployers()
    {
        var (a, employeeOfA, b, employeeOfB) = Employers();
        using var client = await _app.SignedInAs(TestUsers.CreateEmployer(a, "employer-scope"));

        var own = await client.AjaxAsync($"/Employee/Details?id={employeeOfA}&year={YearOf(a)}");
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);

        var other = await client.AjaxAsync($"/Employee/Details?id={employeeOfB}&year={YearOf(b)}");
        Assert.True(Refused(other), $"another employer's employee answered {(int)other.StatusCode} {other.Headers.Location}");
    }

    [LocalDbFact]
    public async Task AnEmployerCannotOpenStaffPages()
    {
        var (a, _, _, _) = Employers();
        using var client = await _app.SignedInAs(TestUsers.CreateEmployer(a, "employer-staff"));
        foreach (var url in new[] { "/UserAccounts", "/SystemSettings", "/TrackerEmployer" })
        {
            var response = await client.PageAsync(url);
            Assert.True(Refused(response), $"{url} answered {(int)response.StatusCode} {response.Headers.Location}");
        }
    }

    [LocalDbFact]
    public async Task ASwitchedOffEmployerLoginIsRefused()
    {
        var (a, _, _, _) = Employers();
        var user = TestUsers.CreateEmployer(a, "employer-off");
        LocalDb.Execute("UPDATE dbo.tbl_User SET Flag = 0 WHERE User_ID = @id", new { id = user.UserId });
        using var client = _app.NewClient();

        var response = await client.SignInAsync(user.Email, user.Password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);   // the sign-in page again, with a message
        Assert.Contains("Invalid login attempt", await response.Content.ReadAsStringAsync());
    }

    // ---- Broker

    [LocalDbFact]
    public async Task ABrokerSignsIn()
    {
        var user = TestUsers.CreateBroker("broker");
        using var client = _app.NewClient();

        var response = await client.SignInAsync(user.Email, user.Password);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.False(AcaClient.IsSignInPage(response.Headers.Location));

        var landing = await client.PageAsync(response.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, landing.StatusCode);
    }

    /// <summary>Employer ids the Select Employer list (sp_EmployersOnlyList) shows this user for one filing year.</summary>
    private static List<int> SelectEmployerList(TestUser user, string roleName, int year)
    {
        using var db = LocalDb.Open();
        return db.Query<int?>("dbo.sp_EmployersOnlyList",
                new { PageIndex = 1, PageSize = 1000, fillingyear = year, SortColumn = 0, SortOrder = "asc", UserId = (int)user.UserId, RoleName = roleName },
                commandType: System.Data.CommandType.StoredProcedure)
            .Where(id => id.HasValue).Select(id => id!.Value).ToList();
    }

    [LocalDbFact]
    public async Task ABrokersDashboardListsTheirBook()
    {
        var (a, _, b, _) = Employers();
        var user = TestUsers.CreateBroker("broker-book");
        TestUsers.AssignToBroker(user, a);
        using var client = await _app.SignedInAs(user);

        var html = await Html(await client.PageAsync("/Dashboard"));
        Assert.Contains(EmployerName(a), html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(EmployerName(b), html, StringComparison.OrdinalIgnoreCase);
    }

    [LocalDbFact]
    public async Task ABrokerOpensTheirOwnEmployer()
    {
        var (a, employeeOfA, _, _) = Employers();
        var user = TestUsers.CreateBroker("broker-own");
        TestUsers.AssignToBroker(user, a);
        using var client = await _app.SignedInAs(user);

        var select = await client.GetAsync($"/SelectEmployer/SelectThis?id={a}");
        Assert.Equal(HttpStatusCode.Redirect, select.StatusCode);
        Assert.DoesNotContain("AccessDenied", select.Headers.Location!.OriginalString);

        var own = await client.AjaxAsync($"/Employee/Details?id={employeeOfA}&year={YearOf(a)}");
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
    }

    [LocalDbFact]
    public void TheDatabaseResolvesABrokersEmployersFromTheirAssignments()
    {
        var (a, _, b, _) = Employers();
        var user = TestUsers.CreateBroker("broker-sql");
        TestUsers.AssignToBroker(user, a);

        using var db = LocalDb.Open();
        var ids = db.Query<int>("dbo.sp_GetEmployersByStaffId", new { StaffId = (int)user.UserId, RoleName = "Broker" },
            commandType: System.Data.CommandType.StoredProcedure).ToList();
        Assert.Equal(new[] { a }, ids);

        LocalDb.Execute("UPDATE dbo.tbl_Employer_Broker_Assignment SET IsActive = 0 WHERE Broker_UserID = @u", new { u = user.UserId });
        Assert.Empty(db.Query<int>("dbo.sp_GetEmployersByStaffId", new { StaffId = (int)user.UserId, RoleName = "Broker" },
            commandType: System.Data.CommandType.StoredProcedure));
        Assert.DoesNotContain(b, ids);
    }

    [LocalDbFact]
    public void TheSelectEmployerListShowsABrokerOnlyTheirBook()
    {
        var (a, _, b, _) = Employers();
        var user = TestUsers.CreateBroker("broker-list");
        TestUsers.AssignToBroker(user, a);

        var listed = SelectEmployerList(user, "Broker", YearOf(a));
        Assert.Contains(a, listed);
        Assert.DoesNotContain(b, listed);
        Assert.Empty(SelectEmployerList(TestUsers.CreateBroker("broker-list-none"), "Broker", YearOf(a)));
    }

    [LocalDbFact]
    public void TheSelectEmployerListShowsAnEmployerLoginOnlyItsEmployer()
    {
        var (a, _, b, _) = Employers();
        var listed = SelectEmployerList(TestUsers.CreateEmployer(a, "employer-list"), "Employer", YearOf(a));
        Assert.Equal(new[] { a }, listed);
        Assert.DoesNotContain(b, listed);
    }

    [LocalDbFact]
    public void TheSelectEmployerListShowsAnUnknownRoleNothing()
    {
        var (a, _, _, _) = Employers();
        Assert.Empty(SelectEmployerList(TestUsers.CreateBroker("role-unknown"), "Someone", YearOf(a)));
        Assert.NotEmpty(SelectEmployerList(TestUsers.CreateStaff(RoleIds.Admin, "role-admin"), "Admin", YearOf(a)));
    }

    [LocalDbFact]
    public async Task ABrokerCannotOpenAnotherEmployersEmployees()
    {
        var (a, _, b, employeeOfB) = Employers();
        var user = TestUsers.CreateBroker("broker-scope");
        TestUsers.AssignToBroker(user, a);
        using var client = await _app.SignedInAs(user);

        var other = await client.AjaxAsync($"/Employee/Details?id={employeeOfB}&year={YearOf(b)}");
        Assert.True(Refused(other), $"an employee outside the broker's book answered {(int)other.StatusCode} {other.Headers.Location}");
    }

    [LocalDbFact]
    public async Task ABrokerCannotOpenStaffPages()
    {
        using var client = await _app.SignedInAs(TestUsers.CreateBroker("broker-staff"));
        foreach (var url in new[] { "/UserAccounts", "/SystemSettings", "/TrackerEmployer" })
        {
            var response = await client.PageAsync(url);
            Assert.True(Refused(response), $"{url} answered {(int)response.StatusCode} {response.Headers.Location}");
        }
    }

    [LocalDbFact]
    public async Task AClosedBrokerAccountCannotSignIn()
    {
        var user = TestUsers.CreateBroker("broker-closed");
        LocalDb.Execute("UPDATE dbo.BrokerAccounts SET Broker_Status = 4 WHERE Broker_ID = @id", new { id = user.RefId });
        using var client = _app.NewClient();

        var response = await client.SignInAsync(user.Email, user.Password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Invalid login attempt", await response.Content.ReadAsStringAsync());
    }

    [LocalDbFact]
    public async Task ASwitchedOffBrokerLoginIsRefused()
    {
        var user = TestUsers.CreateBroker("broker-off");
        LocalDb.Execute("UPDATE dbo.tbl_User SET Flag = 0 WHERE User_ID = @id", new { id = user.UserId });
        using var client = _app.NewClient();

        var response = await client.SignInAsync(user.Email, user.Password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Invalid login attempt", await response.Content.ReadAsStringAsync());
    }
}
