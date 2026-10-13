using System.Net;
using ACA360.Tests.Integration.Infrastructure;

namespace ACA360.Tests.Integration;

/// <summary>
/// Two-step sign-in with Duo (idea #25). Duo itself is the test app's <see cref="FakeDuo"/>, so nothing
/// leaves the machine: the tests check what the app does around it. Duo asks only those who turned
/// it on, administrators included; when Duo cannot be reached admins are stopped and others get in.
/// Without Duo keys (the normal test app) sign-in is unchanged; every other test signs in that way.
/// </summary>
[Collection(AppCollection.Name)]
public class DuoSignInTests : IDisposable
{
    private readonly AcaWebFactory _app;

    public DuoSignInTests(AcaWebFactory app)
    {
        _app = app;
        _app.Duo.Reset();
        _app.Duo.Configured = true;
    }

    public void Dispose() => _app.Duo.Reset();

    private static bool WentToDuo(HttpResponseMessage r) =>
        r.StatusCode == HttpStatusCode.Redirect && r.Headers.Location is { IsAbsoluteUri: true, Host: "duo.example.test" };

    private static bool SignedIn(HttpResponseMessage r) =>
        r.StatusCode == HttpStatusCode.Redirect && r.Headers.Location != null
        && !WentToDuo(r) && !AcaClient.IsSignInPage(r.Headers.Location);

    private static TestUser OptedIn(int roleId, string label)
    {
        var user = TestUsers.CreateStaff(roleId, label);
        LocalDb.Execute("UPDATE dbo.tbl_User SET isMFA = 1 WHERE User_ID = @u", new { u = user.UserId });
        return user;
    }

    [LocalDbFact]
    public async Task SomeoneWhoTurnedItOnSignsInOnlyAfterDuoApproves()
    {
        var admin = OptedIn(RoleIds.SuperAdmin, "duo-admin");
        using var client = _app.NewClient();
        var login = await client.SignInAsync(admin.Email, admin.Password);
        Assert.True(WentToDuo(login), $"{(int)login.StatusCode} {login.Headers.Location}");
        // Not signed in yet: a page sends them back to sign in.
        Assert.True(AcaClient.IsSignInPage((await client.GetAsync("/Notifications")).Headers.Location));

        var back = await client.GetAsync($"/Account/DuoCallback?state=state-{Uri.EscapeDataString(admin.Email)}&duo_code=approved");
        Assert.True(SignedIn(back), $"{(int)back.StatusCode} {back.Headers.Location}");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Notifications")).StatusCode);
    }

    [LocalDbFact]
    public async Task ADuoAnswerForAnotherSignInOrADeniedOneIsRefused()
    {
        var admin = OptedIn(RoleIds.Admin, "duo-refused");
        using var client = _app.NewClient();
        Assert.True(WentToDuo(await client.SignInAsync(admin.Email, admin.Password)));
        var wrongState = await client.GetAsync("/Account/DuoCallback?state=someone-else&duo_code=approved");
        Assert.Equal(HttpStatusCode.OK, wrongState.StatusCode);
        Assert.Contains("expired", await wrongState.Content.ReadAsStringAsync());

        Assert.True(WentToDuo(await client.SignInAsync(admin.Email, admin.Password)));
        var denied = await client.GetAsync($"/Account/DuoCallback?state=state-{Uri.EscapeDataString(admin.Email)}&duo_code=denied");
        Assert.Contains("Duo did not confirm", await denied.Content.ReadAsStringAsync());
        Assert.True(AcaClient.IsSignInPage((await client.GetAsync("/Notifications")).Headers.Location));
    }

    /// <summary>Each user decides, administrators included: only those who turned it on are asked.</summary>
    [LocalDbFact]
    public async Task DuoAsksOnlyThoseWhoTurnedItOn()
    {
        var admin = TestUsers.CreateStaff(RoleIds.SuperAdmin, "duo-admin-off");
        var opted = OptedIn(RoleIds.DataAnalyst, "duo-opted");

        using var a = _app.NewClient();
        Assert.True(SignedIn(await a.SignInAsync(admin.Email, admin.Password)));
        using var b = _app.NewClient();
        Assert.True(WentToDuo(await b.SignInAsync(opted.Email, opted.Password)));
    }

    [LocalDbFact]
    public async Task WhenDuoIsDownAdminsWaitAndOthersGetIn()
    {
        var admin = OptedIn(RoleIds.SuperAdmin, "duo-down-admin");
        var opted = OptedIn(RoleIds.DataAnalyst, "duo-down-opted");
        _app.Duo.Available = false;

        using var a = _app.NewClient();
        var blocked = await a.SignInAsync(admin.Email, admin.Password);
        Assert.Equal(HttpStatusCode.OK, blocked.StatusCode);
        Assert.Contains("cannot be reached", await blocked.Content.ReadAsStringAsync());

        using var b = _app.NewClient();
        Assert.True(SignedIn(await b.SignInAsync(opted.Email, opted.Password)));
    }

    /// <summary>
    /// The switch on Account Settings: any user, an administrator too, turns it on (kept even before the
    /// Duo keys are configured) and off again, and the next sign-in follows it.
    /// </summary>
    [LocalDbFact]
    public async Task EveryUserTurnsItOnAndOffInSettings()
    {
        _app.Duo.Configured = false;
        var admin = TestUsers.CreateStaff(RoleIds.Admin, "duo-switch");
        int Flag() => LocalDb.Scalar<int>("SELECT ISNULL(isMFA, 0) FROM dbo.tbl_User WHERE User_ID = @u", new { u = admin.UserId });

        using (var client = await _app.SignedInAs(admin))
        {
            var page = await (await client.PageAsync("/Settings?tab=security")).Content.ReadAsStringAsync();
            Assert.Contains("id=\"twoStepSwitch\"", page);
            Assert.Contains("not set up on this server yet", page);
            var on = await client.PostFormAsync("/Settings/ToggleMFA", "/Settings", new Dictionary<string, string> { ["enable"] = "true" });
            Assert.Equal(HttpStatusCode.Redirect, on.StatusCode);
            Assert.Equal(1, Flag());
        }

        // With Duo set up, the next sign-in goes through it; from there the user turns it off again.
        _app.Duo.Configured = true;
        using (var next = _app.NewClient())
        {
            Assert.True(WentToDuo(await next.SignInAsync(admin.Email, admin.Password)));
            Assert.True(SignedIn(await next.GetAsync($"/Account/DuoCallback?state=state-{Uri.EscapeDataString(admin.Email)}&duo_code=approved")));
            var off = await next.PostFormAsync("/Settings/ToggleMFA", "/Settings", new Dictionary<string, string> { ["enable"] = "false" });
            Assert.Equal(HttpStatusCode.Redirect, off.StatusCode);
            Assert.Equal(0, Flag());
        }
        using var after = _app.NewClient();
        Assert.True(SignedIn(await after.SignInAsync(admin.Email, admin.Password)));
    }

    /// <summary>Without Duo keys and with the switch off, sign-in is the password alone, as before.</summary>
    [LocalDbFact]
    public async Task WithoutDuoKeysSignInIsUnchanged()
    {
        _app.Duo.Configured = false;
        var admin = OptedIn(RoleIds.SuperAdmin, "duo-off");
        using var client = _app.NewClient();
        Assert.True(SignedIn(await client.SignInAsync(admin.Email, admin.Password)));
    }
}
