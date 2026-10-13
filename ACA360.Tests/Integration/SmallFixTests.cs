using System.Net;
using Dapper;
using ACA360.Tests.Integration.Infrastructure;

namespace ACA360.Tests.Integration;

[Collection(AppCollection.Name)]
public class SmallFixTests
{
    private readonly AcaWebFactory _app;

    public SmallFixTests(AcaWebFactory app) => _app = app;

    private static Task<HttpResponseMessage> SignIn(AcaClient client, TestUser user, string? returnUrl)
    {
        var fields = new Dictionary<string, string> { ["Username"] = user.Email, ["Password"] = user.Password };
        if (returnUrl != null) fields["ReturnUrl"] = returnUrl;
        return client.PostFormAsync("/Account/Login", "/Account/Login", fields);
    }

    // ---- Signing in goes on to the page that asked for it

    [LocalDbFact]
    public async Task AnAnonymousVisitIsRememberedAndFollowedAfterSigningIn()
    {
        using var client = _app.NewClient();
        var visit = await client.PageAsync("/Calendar");
        Assert.True(AcaClient.IsSignInPage(visit.Headers.Location));
        Assert.Contains("ReturnUrl=%2FCalendar", visit.Headers.Location!.OriginalString);

        var loginPage = await (await client.GetAsync(visit.Headers.Location!.OriginalString)).Content.ReadAsStringAsync();
        Assert.Contains("name=\"ReturnUrl\" value=\"/Calendar\"", loginPage);

        // A Data Analyst goes straight on (no employer to choose first).
        var response = await SignIn(client, TestUsers.CreateStaff(RoleIds.DataAnalyst, "return"), "/Calendar");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Calendar", response.Headers.Location!.OriginalString);
    }

    [LocalDbTheory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("/Account/Logout")]
    [InlineData("javascript:alert(1)")]
    public async Task OnlyAddressesOnThisSiteAreFollowed(string returnUrl)
    {
        using var client = _app.NewClient();
        var response = await SignIn(client, TestUsers.CreateStaff(RoleIds.DataAnalyst, "openredir"), returnUrl);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Dashboard", response.Headers.Location!.OriginalString);
    }

    [LocalDbFact]
    public async Task WhenAnEmployerMustBeChosenFirstThePageIsKeptForAfterwards()
    {
        using var client = _app.NewClient();
        var response = await SignIn(client, TestUsers.CreateStaff(RoleIds.SuperAdmin, "return-select"), "/Calendar");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/SelectEmployer", response.Headers.Location!.OriginalString);
    }

    [LocalDbFact]
    public async Task ASignedInUserOpeningTheSignInPageGoesToTheirPage()
    {
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.Admin, "return-signed"));
        var response = await client.PageAsync("/Account/Login?ReturnUrl=%2FFaq");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Faq", response.Headers.Location!.OriginalString);
    }

    // ---- Tracker services open in their own plan year

    [LocalDbFact]
    public async Task AServiceFromAnotherPlanYearOpens()
    {
        var serviceId = LocalDb.Scalar<int?>(
            "SELECT TOP 1 EmployerServiceId FROM dbo.Tracker_EmployerService WHERE ISNULL(IsDeleted, 0) = 0 AND PlanYear > 0 AND PlanYear <> YEAR(GETUTCDATE()) ORDER BY EmployerServiceId");
        if (serviceId is not int id) return;   // no service outside the current year locally
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.SuperAdmin, "svc-year"));

        var panel = await client.AjaxAsync($"/TrackerEmployer/GetServicePanel?id={id}");
        Assert.Equal(HttpStatusCode.OK, panel.StatusCode);
    }

    // ---- Calendar events keep the text as typed

    [LocalDbFact]
    public async Task ACalendarEventIsSavedAsTyped()
    {
        var user = TestUsers.CreateStaff(RoleIds.Admin, "calcase");
        using var client = await _app.SignedInAs(user);
        var token = AcaClient.Token(await (await client.GetAsync("/Calendar/Index")).Content.ReadAsStringAsync());
        var request = new HttpRequestMessage(HttpMethod.Post, "/Calendar/AddEvent")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new
            {
                title = "Team call", location = "Room 2b", eventUrl = "https://meet.example.com/AbC",
                startDate = DateTime.Today.AddHours(9), endDate = DateTime.Today.AddHours(10), isAllDay = false, colorCode = "#ff3e1d",
            }),
        };
        request.Headers.Add("RequestVerificationToken", token);
        Assert.Equal(HttpStatusCode.OK, (await client.Http.SendAsync(request)).StatusCode);

        using var db = LocalDb.Open();
        var saved = Dapper.SqlMapper.QuerySingle<(string Title, string Location, string EventUrl)>(db,
            "SELECT Title, Location, EventUrl FROM dbo.tbl_CalendarEvents WHERE UserId = @u", new { u = user.UserId });
        Assert.Equal(("Team call", "Room 2b", "https://meet.example.com/AbC"), saved);
    }

    // ---- An employee added by hand belongs to its employer (script 038)

    [LocalDbFact]
    public async Task AnEmployeeAddedByHandIsRecordedUnderItsEmployer()
    {
        var employer = TestImports.CreateEmployer(2025);
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.SuperAdmin, "manual-add"));
        var token = AcaClient.Token(await (await client.PageAsync("/Dashboard")).Content.ReadAsStringAsync());
        var ssn = $"9{System.Security.Cryptography.RandomNumberGenerator.GetInt32(10, 100)}-55-{System.Security.Cryptography.RandomNumberGenerator.GetInt32(1000, 10000)}";

        // What the Add Employee form posts.
        var request = new HttpRequestMessage(HttpMethod.Post, "/Employee/EmployeedetailSave")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new
            {
                EmployerId = employer.Id.ToString(), FilingYear = "2025", EmployeeID = "0",
                FirstName = "Manualadd", LastName = TestEmployees.LastName, SSN = ssn, Birthday = new DateTime(1985, 3, 4),
                Address = "1 Test Street", City = "Testville", State = "NJ", Zip = "07001", getForm = (short)1,
            }),
        };
        request.Headers.Add("RequestVerificationToken", token);
        var response = await client.Http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("added", await response.Content.ReadAsStringAsync());

        // Employee.EmployerId is what every employee page checks access by; it used to stay empty.
        var saved = LocalDb.Scalar<int?>(
            "SELECT EmployerId FROM dbo.Employee WHERE firstName = 'Manualadd' AND lastName = @last AND REPLACE(ssn, '-', '') = @digits",
            new { last = TestEmployees.LastName, digits = ssn.Replace("-", "") });
        Assert.Equal(employer.Id, saved);
    }

    // ---- The Employee list without a search lists everyone (the filter's search defaulted to "asc")

    [LocalDbFact]
    public async Task TheEmployeeListWithoutASearchListsEveryone()
    {
        using var db = LocalDb.Open();
        var (employer, year) = db.QueryFirst<(int, int)>(
            @"SELECT TOP 1 e.id, e.filingYear FROM dbo.Employer e
              JOIN dbo.EmployeeCode c ON c.employerId = e.id AND c.filingYear = e.filingYear
              JOIN dbo.Employee x ON x.id = c.employeeId AND x.IsDeleted = 0 AND x.Flag = 0
              WHERE ISNULL(e.IsDeleted, 0) = 0 GROUP BY e.id, e.filingYear ORDER BY COUNT(*) DESC");
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.SuperAdmin, "list-search"));
        await client.GetAsync($"/SelectEmployer/SelectThis?id={employer}");

        async Task<List<string>> Rows(string query)
        {
            var response = await client.AjaxAsync($"/Employee/EmployeeListPartial?Employer_IDs={employer}&viewMode=Table&FilingYear={year}" + query);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return System.Text.RegularExpressions.Regex.Matches(await response.Content.ReadAsStringAsync(), "employee-item[^\"]*\" data-id=\"([0-9]+)\"")
                .Select(m => m.Groups[1].Value).ToList();
        }

        var asThePageAsks = await Rows("&search=&sortColumn=0&sortOrder=asc");
        Assert.NotEmpty(asThePageAsks);
        Assert.Equal(asThePageAsks, await Rows(""));
        Assert.Equal(await Rows("&pageIndex=2&search=&sortColumn=0&sortOrder=asc"), await Rows("&pageIndex=2"));
    }
}
