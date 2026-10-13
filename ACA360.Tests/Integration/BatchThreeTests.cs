using System.Net;
using ACA360.Tests.Integration.Infrastructure;
using Dapper;
using OfficeOpenXml;

namespace ACA360.Tests.Integration;

/// <summary>
/// The October 2026 third batch that needs no import: safe harbor advice (071), State Filings (072),
/// daily reminders (070), bulk actions (074) and grids that remember (idea #17).
/// </summary>
[Collection(AppCollection.Name)]
public class BatchThreeTests : IDisposable
{
    private const int Year = 2025;
    private readonly AcaWebFactory _app;

    public BatchThreeTests(AcaWebFactory app) => _app = app;

    public void Dispose()
    {
        TestEmployees.RemoveAll();
        TestImports.RemoveAll();
    }

    /// <summary>One month's working figures, as sp_Generate1095Codes keeps them (script 058).</summary>
    private static void Month(TestEmployee e, int month, decimal premium, decimal w2, string? line16, bool enrolled = false,
        string fundingType = "1", string line14 = "1E")
    {
        LocalDb.Execute(
            @"INSERT INTO dbo.EmployeeCodeMonthDetail (EmployeeCodeId, MonthNum, Employed, IsFullTime, Premium, Enrolled, HourlyRate, AnnualW2,
                                                     HiredMonths, MonthlyFpl, AffordabilityPct, FullTimeHours, Line14, Line15, Line16, FundingType, PlanName,
                                                     CalculatedAt, CalculatedBy)
              SELECT id, @month, 1, 1, @premium, @enrolled, 0, @w2, 12, 113.20, 0.0902, 130, @line14, @premium, @line16, @fundingType, 'TEST PPO',
                     SYSDATETIME(), 'tests'
              FROM dbo.EmployeeCode WHERE employeeId = @id AND filingYear = @year",
            new { id = e.Id, year = e.Year, month, premium, w2, line16, enrolled, fundingType, line14 });
    }

    /// <summary>
    /// For the months that need an affordability safe harbor, the page says which one covers them:
    /// an offer of $100 to someone on $60,000 is covered three ways; $500 to someone on $30,000 by none.
    /// </summary>
    [LocalDbFact]
    public async Task SafeHarborAdviceFindsTheOffersNoSafeHarborCovers()
    {
        var employer = TestImports.CreateEmployer(Year);
        var fine = TestEmployees.Create(employer.Id, Year, "SHFINE");
        var costly = TestEmployees.Create(employer.Id, Year, "SHCOSTLY");
        for (int m = 1; m <= 12; m++)
        {
            Month(fine, m, 100m, 60000m, "2G");
            Month(costly, m, 500m, 30000m, null);
        }
        using var admin = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.SuperAdmin, "safeharbor"));
        var response = await admin.PageAsync($"/ACA/SafeHarbor?employerId={employer.Id}&year={Year}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Not affordable (1)", html);
        Assert.Contains("12 of 24 months", html);   // each safe harbor covers SHFINE's 12 of the 24 tested
        Assert.Contains("data-sh-row=\"uncovered\"", html);
        // Advice only: nothing changed.
        Assert.Equal(0, LocalDb.Scalar<int>("SELECT COUNT(*) FROM dbo.EmployeeCodeAuditTrail WHERE EmployeeId IN (@a, @b)", new { a = fine.Id, b = costly.Id }));
    }

    /// <summary>
    /// State Filings counts each state's residents, covered, and covered by a self-insured plan; the
    /// workbook lists them (every role that can open State Filings may see full SSNs).
    /// </summary>
    [LocalDbFact]
    public async Task StateFilingsCountResidentsAndExportThem()
    {
        var employer = TestImports.CreateEmployer(Year);
        var selfInsured = TestEmployees.Create(employer.Id, Year, "STATESELF");     // test employees live in NJ
        var insured = TestEmployees.Create(employer.Id, Year, "STATEINS");
        for (int m = 1; m <= 12; m++)
        {
            Month(selfInsured, m, 50m, 50000m, "2C", enrolled: true, fundingType: "2");
            Month(insured, m, 50m, 50000m, "2C", enrolled: m <= 6, fundingType: "1");
        }

        using var admin = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.SuperAdmin, "states"));
        var page = await (await admin.PageAsync($"/StateFilings?year={Year}")).Content.ReadAsStringAsync();
        Assert.Contains(employer.Name, page);
        Assert.Contains("title=\"2 residents, 2 covered, 1 self-insured\"", page);

        var residents = await (await admin.AjaxAsync($"/StateFilings/Residents?employerId={employer.Id}&year={Year}&state=NJ")).Content.ReadAsStringAsync();
        Assert.Contains("Jan, Feb, Mar, Apr, May, Jun", residents);
        Assert.Contains("All year", residents);

        var full = await admin.GetAsync($"/StateFilings/Export?employerId={employer.Id}&year={Year}&state=NJ");
        Assert.Equal(HttpStatusCode.OK, full.StatusCode);
        Assert.Contains(selfInsured.Ssn, Cells(await full.Content.ReadAsByteArrayAsync()));

        // An account manager exports their own employers, not others'.
        var am = TestUsers.CreateStaff(RoleIds.AccountManager, "states-am");
        TestUsers.Assign(am, employer.Id);
        using var amClient = await _app.SignedInAs(am);
        Assert.Equal(HttpStatusCode.OK, (await amClient.GetAsync($"/StateFilings/Export?employerId={employer.Id}&year={Year}&state=NJ")).StatusCode);
        var other = TestImports.CreateEmployer(Year);
        Assert.NotEqual(HttpStatusCode.OK, (await amClient.GetAsync($"/StateFilings/Export?employerId={other.Id}&year={Year}&state=NJ")).StatusCode);
    }

    private static List<string> Cells(byte[] xlsx)
    {
        ExcelPackage.LicenseContext = LicenseContext.Commercial;
        using var package = new ExcelPackage(new MemoryStream(xlsx));
        return package.Workbook.Worksheets.SelectMany(ws => ws.Cells.Select(c => c.Text)).ToList();
    }

    /// <summary>
    /// Deadline reminders go out once at 14 days and once at 7, however often the job runs; admins get
    /// the System Health digest, other staff do not. Run in a transaction that is rolled back.
    /// </summary>
    [LocalDbFact]
    public void DeadlineRemindersAreSentOnceAtEachStage()
    {
        var analyst = TestUsers.CreateStaff(RoleIds.DataAnalyst, "remind");
        var admin = TestUsers.CreateStaff(RoleIds.SuperAdmin, "remind-admin");
        using var db = LocalDb.Open();
        using var tx = db.BeginTransaction();
        var deadline = db.ExecuteScalar<int>(
            @"INSERT INTO dbo.FilingDeadlines (FilingYear, Jurisdiction, Kind, Title, DueDate, NeedsCheck, IsActive, ModifiedBy, ModifiedAt)
              OUTPUT inserted.FilingDeadlineId VALUES (2099, 'IRS', 'E-file', 'ACA360TESTS deadline', '2099-03-31', 0, 1, 'tests', SYSDATETIME())", transaction: tx);
        db.Execute(@"INSERT INTO dbo.tbl_ErrorLogs (Level, CallSite, Type, Message, StackTrace, AdditionalInfo, User_Ip, LoggedOnDate)
                     VALUES ('Error', 'tests', 'Error', 'ACA360TESTS error', '', '', '', '2099-03-24 10:00')", transaction: tx);
        int For(TestUser u, string like) => db.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM dbo.tbl_UserNotifications WHERE UserId = @u AND (EventKey LIKE @like)", new { u = u.UserId, like }, tx);

        foreach (var day in new[] { "2099-03-17", "2099-03-18", "2099-03-24", "2099-03-25" })
            db.Execute("EXEC dbo.sp_Notify_Daily @Today = @day", new { day }, tx);

        Assert.Equal(1, For(analyst, $"Deadline:{deadline}:14"));
        Assert.Equal(1, For(analyst, $"Deadline:{deadline}:7"));
        Assert.Equal(0, For(analyst, "Health:%"));
        Assert.True(For(admin, "Health:%") >= 1);
        tx.Rollback();
    }

    /// <summary>
    /// Locking codes in bulk: only employees the user may reach, the change on each employee's code
    /// history; unlocking puts it back.
    /// </summary>
    [LocalDbFact]
    public async Task CodesAreLockedAndUnlockedInBulk()
    {
        // The action works on the filing year in session; without one chosen that is this year.
        var year = DateTime.UtcNow.Year;
        var mine = TestImports.CreateEmployer(year);
        var theirs = TestImports.CreateEmployer(year);
        var a = TestEmployees.Create(mine.Id, year, "BULKA");
        var b = TestEmployees.Create(mine.Id, year, "BULKB");
        var c = TestEmployees.Create(theirs.Id, year, "BULKC");
        var am = TestUsers.CreateStaff(RoleIds.AccountManager, "bulk-am");
        TestUsers.Assign(am, mine.Id);

        async Task<HttpResponseMessage> SetLocked(AcaClient client, bool locked, params int[] ids)
        {
            var fields = ids.Select(id => new KeyValuePair<string, string>("employeeIds", id.ToString())).ToList();
            fields.Add(new("locked", locked ? "true" : "false"));
            fields.Add(new("__RequestVerificationToken", AcaClient.Token(await (await client.GetAsync("/Notifications")).Content.ReadAsStringAsync())));
            var request = new HttpRequestMessage(HttpMethod.Post, "/ACA/SetCodesLocked") { Content = new FormUrlEncodedContent(fields) };
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            return await client.Http.SendAsync(request);
        }

        using var admin = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.SuperAdmin, "bulk-admin"));
        var locked = await SetLocked(admin, true, a.Id, b.Id);
        Assert.True(locked.IsSuccessStatusCode, await locked.Content.ReadAsStringAsync());
        Assert.Equal(2, LocalDb.Scalar<int>("SELECT COUNT(*) FROM dbo.EmployeeCode WHERE employeeId IN (@a, @b) AND IsLocked = 1", new { a = a.Id, b = b.Id }));
        Assert.Equal(2, LocalDb.Scalar<int>(
            "SELECT COUNT(DISTINCT EmployeeId) FROM dbo.EmployeeCodeAuditTrail WHERE EmployeeId IN (@a, @b) AND FieldName = 'IsLocked' AND NewValue = '1'",
            new { a = a.Id, b = b.Id }));

        var history = await (await admin.AjaxAsync($"/Employee/History?id={a.Id}")).Content.ReadAsStringAsync();
        Assert.Contains("Codes locked", history);

        Assert.True((await SetLocked(admin, false, a.Id)).IsSuccessStatusCode);
        Assert.Equal(0, LocalDb.Scalar<int>("SELECT CAST(IsLocked AS INT) FROM dbo.EmployeeCode WHERE employeeId = @a", new { a = a.Id }));

        // An account manager cannot lock an employee of an employer they are not assigned to.
        using var amClient = await _app.SignedInAs(am);
        Assert.NotEqual(HttpStatusCode.OK, (await SetLocked(amClient, true, a.Id, c.Id)).StatusCode);
        Assert.Equal(0, LocalDb.Scalar<int>("SELECT CAST(ISNULL(IsLocked, 0) AS INT) FROM dbo.EmployeeCode WHERE employeeId = @c", new { c = c.Id }));
    }

    /// <summary>
    /// Assigning employers in bulk adds them to the account manager's list; the employers they already
    /// had stay (the assignment screen replaces the whole list, so this is easy to get wrong).
    /// </summary>
    [LocalDbFact]
    public async Task BulkAssignAddsEmployersAndKeepsTheOnesAlreadyAssigned()
    {
        var had = TestImports.CreateEmployer(Year);
        var adding = TestImports.CreateEmployer(Year);
        var am = TestUsers.CreateStaff(RoleIds.AccountManager, "assign-am");
        TestUsers.Assign(am, had.Id);
        using var admin = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.SuperAdmin, "assign-admin"));

        var staff = await admin.JsonAsync("/Employer/StaffForAssignment?side=AM");
        Assert.Contains(staff.EnumerateArray(), s => s.GetProperty("refId").GetInt32() == am.RefId);

        var response = await admin.PostFormAsync("/Employer/BulkAssign", "/Notifications", new Dictionary<string, string>
        {
            ["staffRefId"] = am.RefId.ToString(), ["roleId"] = RoleIds.AccountManager.ToString(), ["side"] = "AM",
            ["employerIds"] = adding.Id.ToString(),
        }, ajax: true);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var active = LocalDb.Open().Query<int>(
            "SELECT EmployerID FROM dbo.tbl_Portfolio_Assignment WHERE AM_UserID = @u AND IsActive = 1 AND EndDate IS NULL", new { u = am.UserId }).ToList();
        Assert.Contains(had.Id, active);
        Assert.Contains(adding.Id, active);

        // Only those who manage assignments.
        using var amClient = await _app.SignedInAs(am);
        Assert.NotEqual(HttpStatusCode.OK, (await amClient.AjaxAsync("/Employer/StaffForAssignment?side=AM")).StatusCode);
    }

    /// <summary>A list page's state is kept per user and grid; empty state removes it; odd keys are refused.</summary>
    [LocalDbFact]
    public async Task GridStateIsKeptPerUser()
    {
        var user = TestUsers.CreateStaff(RoleIds.DataAnalyst, "grid");
        using var client = await _app.SignedInAs(user);
        const string key = "/plan/planlistpartial";
        var state = "{\"search\":\"ppo\",\"pageSize\":\"25\",\"sortColumn\":\"0\",\"sortOrder\":\"desc\",\"hiddenColumns\":[\"Type\"],\"filters\":{\"#flt_funding\":\"1\"}}";
        Assert.Equal(HttpStatusCode.OK, (await client.PostFormAsync("/UserPreference/SaveGridState", "/Notifications",
            new Dictionary<string, string> { ["key"] = key, ["state"] = state }, ajax: true)).StatusCode);

        var saved = await client.JsonAsync($"/UserPreference/GridState?key={Uri.EscapeDataString(key)}");
        Assert.Equal("ppo", saved.GetProperty("search").GetString());
        Assert.Equal("Type", saved.GetProperty("hiddenColumns")[0].GetString());

        using var other = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.DataAnalyst, "grid-other"));
        Assert.Equal("{}", await (await other.AjaxAsync($"/UserPreference/GridState?key={Uri.EscapeDataString(key)}")).Content.ReadAsStringAsync());

        await client.PostFormAsync("/UserPreference/SaveGridState", "/Notifications", new Dictionary<string, string> { ["key"] = key, ["state"] = "{}" }, ajax: true);
        Assert.Equal("{}", await (await client.AjaxAsync($"/UserPreference/GridState?key={Uri.EscapeDataString(key)}")).Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, (await client.AjaxAsync("/UserPreference/GridState?key=" + Uri.EscapeDataString("<script>"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostFormAsync("/UserPreference/SaveGridState", "/Notifications",
            new Dictionary<string, string> { ["key"] = key, ["state"] = "not json" }, ajax: true)).StatusCode);
    }
}
