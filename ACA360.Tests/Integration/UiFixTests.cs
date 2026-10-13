using System.Data;
using ACA360.Web.Helpers;
using System.Net;
using ACA360.Tests.Integration.Infrastructure;
using Dapper;

namespace ACA360.Tests.Integration;

/// <summary>Issues found by the UI test of 2026-10-01.</summary>
[Collection(AppCollection.Name)]
public class UiFixTests
{
    private readonly AcaWebFactory _app;

    public UiFixTests(AcaWebFactory app) => _app = app;

    private static List<IDictionary<string, object>> UserAccountsList(string search)
    {
        using var db = LocalDb.Open();
        return db.Query("dbo.sp_UserAccountsList", new { PageIndex = 1, PageSize = 500, Search = search },
                commandType: CommandType.StoredProcedure)
            .Select(r => (IDictionary<string, object>)r).ToList();
    }

    // ---- 1. User Accounts: switched-off logins, and logins listed under someone else's record

    [LocalDbFact]
    public void ASwitchedOffLoginIsListedAsLoginOffNotActive()
    {
        var user = TestUsers.CreateStaff(RoleIds.AccountManager, "loginoff");
        var row = Assert.Single(UserAccountsList(user.Email));
        Assert.True((bool)row["LoginEnabled"]);
        Assert.Equal(1, Convert.ToInt32(row["UserAccounts_Status"]));

        LocalDb.Execute("UPDATE dbo.tbl_User SET Flag = 0 WHERE User_ID = @id", new { id = user.UserId });
        row = Assert.Single(UserAccountsList(user.Email));
        Assert.False((bool)row["LoginEnabled"]);
        Assert.Equal(2, Convert.ToInt32(row["UserAccounts_Status"]));   // Inactive: the Active filter leaves it out
    }

    [LocalDbFact]
    public void AnEmployerLoginIsNotListedUnderAStaffRecordWithTheSameId()
    {
        var staff = TestUsers.CreateStaff(RoleIds.Admin, "sameid");
        // An Employer login whose Ref_ID happens to equal that Staff_ID.
        LocalDb.Execute(
            "INSERT INTO dbo.tbl_User (User_Name, Role_ID, Flag, Ref_ID, isMFA, CreatedOn) VALUES (@name, 13, 1, @ref, 0, GETDATE())",
            new { name = TestUsers.Prefix + "employer-collision@example.com", @ref = staff.RefId });

        var rows = UserAccountsList(staff.Email);
        var row = Assert.Single(rows);
        Assert.Equal(RoleIds.Admin, Convert.ToInt32(row["UserAccounts_Role"]));
    }

    [LocalDbFact]
    public async Task TheUserAccountsPageShowsLoginOff()
    {
        var off = TestUsers.CreateStaff(RoleIds.AccountManager, "page-off");
        LocalDb.Execute("UPDATE dbo.tbl_User SET Flag = 0 WHERE User_ID = @id", new { id = off.UserId });
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.SuperAdmin, "page-admin"));

        var html = await (await client.AjaxAsync(
            "/UserAccounts/UserAccountsListPartial?pageIndex=1&pageSize=10&search=" + Uri.EscapeDataString(off.Email))).Content.ReadAsStringAsync();
        Assert.Contains("Login off", html);
        Assert.DoesNotContain(">Active<", html);
    }

    // ---- 2. Broker Management opens the broker view

    [LocalDbFact]
    public async Task EachPartnerMenuEntryOpensItsOwnView()
    {
        using var db = LocalDb.Open();
        var menus = db.Query<(int Id, string MenuName)>(
            "SELECT Id, LTRIM(RTRIM(MenuName)) FROM dbo.tbl_Menus WHERE LTRIM(RTRIM(Controller)) = 'Partner' AND IsActive = 1 AND Flag = 0").ToList();
        if (menus.Count == 0) return;   // no Partner menu locally
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.SuperAdmin, "partner"));

        // Each entry's own address (/Partner, /Partner/Brokers), as the sidebar links it, with no ?menuId=.
        var home = await (await client.PageAsync("/Partner")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("menuId=", home);
        foreach (var (id, name) in menus)
        {
            var path = "/Partner" + (MenuLinks.ActionFor("Partner", name) == "Index" ? "" : "/" + MenuLinks.ActionFor("Partner", name));
            var html = await (await client.PageAsync(path)).Content.ReadAsStringAsync();
            Assert.Contains($"href=\"{path}\"", home);
            Assert.Contains(System.Net.WebUtility.HtmlEncode(name), html);
            var brokersFirst = html.Contains("col-xl-5 order-first");
            Assert.Equal(name.Contains("Broker", StringComparison.OrdinalIgnoreCase), brokersFirst);

            // The sidebar highlights this entry, not the other Partner entry.
            var active = System.Text.RegularExpressions.Regex.Matches(html,
                "<li class=\"menu-item active\">\\s*<a href=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
            Assert.Contains(path, active);
            Assert.Single(active, a => a.StartsWith("/Partner", StringComparison.OrdinalIgnoreCase));
        }

        var plain = await (await client.PageAsync("/Partner")).Content.ReadAsStringAsync();
        Assert.Contains("Firm Management", plain);
        Assert.DoesNotContain("col-xl-5 order-first", plain);
    }

    // ---- 3. Forms page heading

    [LocalDbFact]
    public async Task TheFormsPageHeadingIsSpelledRight()
    {
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.SuperAdmin, "forms"));
        var html = await (await client.PageAsync("/FormGeneration")).Content.ReadAsStringAsync();
        Assert.Contains("Form Generation Hub", html);
        Assert.DoesNotContain("Geneation", html);
    }

    // ---- 4. Unknown addresses show the app's error page

    [LocalDbFact]
    public async Task AnUnknownPageShowsTheAppsNotFoundPage()
    {
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.Admin, "notfound"));
        var response = await client.PageAsync("/NoSuchPage/AtAll");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Page Not Found", await response.Content.ReadAsStringAsync());
    }

    [LocalDbFact]
    public async Task AjaxCallersStillGetAPlain404()
    {
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.Admin, "notfound-ajax"));
        var response = await client.AjaxAsync("/NoSuchPage/AtAll");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync());
    }

    [LocalDbFact]
    public async Task AnonymousVisitorsAreStillSentToSignIn()
    {
        using var client = _app.NewClient();
        var response = await client.PageAsync("/Employer");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(AcaClient.IsSignInPage(response.Headers.Location));
    }

    // ---- 2026-10-01 Chrome UI test

    [LocalDbFact]
    public async Task TheDashboardGreetsTheUserByName()
    {
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.SuperAdmin, "greet"));
        var html = await (await client.PageAsync("/Dashboard")).Content.ReadAsStringAsync();
        var text = System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " "), @"\s+", " ");
        Assert.Contains("Welcome back, Test GREET", text, StringComparison.OrdinalIgnoreCase);
    }

    [LocalDbFact]
    public async Task TheTrackerListShowsEachRecordsFilingYear()
    {
        var withYear = LocalDb.Scalar<int?>("SELECT TOP 1 filingYear FROM dbo.Employer WHERE ISNULL(IsDeleted, 0) = 0 AND filingYear > 0 ORDER BY id");
        if (withYear is not int year) return;
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.SuperAdmin, "trk-year"));
        var html = System.Net.WebUtility.HtmlDecode(await (await client.AjaxAsync("/TrackerEmployer/GetList?Page=1&PageSize=100")).Content.ReadAsStringAsync());
        Assert.Contains($" · {year}</small>", html);
    }

    [Fact]
    public void OnlyTheEmployersPageUsesTheEmployerBreadcrumb()
    {
        var views = Path.Combine(RepoRoot(), "ACA360.Web", "Views");
        var offenders = Directory.GetFiles(views, "*.cshtml", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("new BreadcrumbItem { Title = \"Employer\""))
            .Select(f => Path.GetRelativePath(views, f).Replace('\\', '/'))
            .Where(f => f != "Employer/Index.cshtml")
            .ToList();
        Assert.True(offenders.Count == 0, "Pages outside Employers with an \"Employer\" breadcrumb:\n" + string.Join("\n", offenders));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ACA360.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("ACA360.sln not found");
    }
}
