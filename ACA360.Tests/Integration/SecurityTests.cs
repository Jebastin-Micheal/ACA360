using System.Net;
using ACA360.Tests.Integration.Infrastructure;

namespace ACA360.Tests.Integration;

[Collection(AppCollection.Name)]
public class SecurityTests
{
    private readonly AcaWebFactory _app;

    public SecurityTests(AcaWebFactory app) => _app = app;

    private static bool Refused(HttpResponseMessage response) =>
        response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
        || (response.StatusCode == HttpStatusCode.Redirect
            && (response.Headers.Location!.OriginalString.Contains("AccessDenied") || AcaClient.IsSignInPage(response.Headers.Location)));

    [LocalDbFact]
    public async Task APostWithoutTheAntiforgeryTokenIsRejected()
    {
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.Admin, "csrf"));
        var response = await client.Http.PostAsync("/Tour/Seen", new FormUrlEncodedContent(new Dictionary<string, string> { ["tour"] = "home:1" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [LocalDbFact]
    public async Task ATokenFromAnotherUserIsRejected()
    {
        using var first = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.Admin, "csrf-a"));
        using var second = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.Admin, "csrf-b"));
        var token = AcaClient.Token(await (await first.GetAsync("/Faq/Index")).Content.ReadAsStringAsync());
        var response = await second.Http.PostAsync("/Tour/Seen", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["tour"] = "home:1", ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [LocalDbFact]
    public async Task PagesSendTheSecurityHeaders()
    {
        using var client = _app.NewClient();
        var response = await client.PageAsync("/Account/Login");
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [LocalDbFact]
    public async Task TheAppManifestIsPublicAndNeverCached()
    {
        using var client = _app.NewClient();
        var response = await client.GetAsync("/manifest.webmanifest");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/manifest+json", response.Content.Headers.ContentType!.MediaType);
        Assert.True(response.Headers.CacheControl!.NoCache);
    }

    [LocalDbTheory]
    [InlineData(RoleIds.AccountManager, false)]
    [InlineData(RoleIds.AcaDirector, false)]
    [InlineData(RoleIds.Admin, true)]
    public async Task OnlyAdministratorsOpenTheJobDashboard(int roleId, bool allowed)
    {
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(roleId, "hangfire"));
        var response = await client.PageAsync("/hangfire");
        if (allowed) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        else Assert.True(Refused(response), $"{(int)response.StatusCode} {response.Headers.Location}");
    }

    [LocalDbFact]
    public async Task OnlyFaqEditorsCanChangeTheFaq()
    {
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.AccountManager, "faq"));
        Assert.True(Refused(await client.AjaxAsync("/Faq/GetFaqForm")));
        var save = await client.PostFormAsync("/Faq/DeleteFaq", "/Faq/Index", new Dictionary<string, string> { ["id"] = "does-not-matter" }, ajax: true);
        Assert.True(Refused(save), $"{(int)save.StatusCode} {save.Headers.Location}");
    }

    [LocalDbFact]
    public async Task NonAdministratorsCannotOpenUserAccounts()
    {
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.StaffAC, "useracc"));
        Assert.True(Refused(await client.AjaxAsync("/UserAccounts/Add_New")));
    }
}

[Collection(AppCollection.Name)]
public class TourTests
{
    private readonly AcaWebFactory _app;

    public TourTests(AcaWebFactory app) => _app = app;

    [LocalDbFact]
    public async Task ATourIsRememberedOnceSeen()
    {
        var user = TestUsers.CreateStaff(RoleIds.Admin, "tour");
        using var client = await _app.SignedInAs(user);

        Assert.False((await client.JsonAsync("/Tour/Status?tour=home:1")).GetProperty("seen").GetBoolean());
        var seen = await client.PostFormAsync("/Tour/Seen", "/Faq/Index", new Dictionary<string, string> { ["tour"] = "home:1" }, ajax: true);
        Assert.Equal(HttpStatusCode.OK, seen.StatusCode);
        Assert.True((await client.JsonAsync("/Tour/Status?tour=home:1")).GetProperty("seen").GetBoolean());
        Assert.False((await client.JsonAsync("/Tour/Status?tour=home:2")).GetProperty("seen").GetBoolean());

        var stored = LocalDb.Scalar<string>("SELECT WidgetOrder FROM dbo.UserDashboardLayout WHERE UserId = @id AND DashboardKey = 'tours'", new { id = user.UserId });
        Assert.Contains("home:1", stored);
    }
}
