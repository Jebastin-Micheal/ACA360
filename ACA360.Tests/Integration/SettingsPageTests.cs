using System.Net;
using ACA360.Tests.Integration.Infrastructure;

namespace ACA360.Tests.Integration;

/// <summary>
/// Account Settings (2026-10-07): where the user is signed in and "Sign out everywhere else" (script 075),
/// the real notification choices, and the placeholders (API key, Connections) and self-deactivation gone.
/// The two-step switch is in DuoSignInTests.
/// </summary>
[Collection(AppCollection.Name)]
public class SettingsPageTests
{
    private readonly AcaWebFactory _app;

    public SettingsPageTests(AcaWebFactory app) => _app = app;

    [LocalDbFact]
    public async Task SigningOutEverywhereElseEndsTheOtherSessionsOnly()
    {
        var user = TestUsers.CreateStaff(RoleIds.DataAnalyst, "sessions");
        using var here = await _app.SignedInAs(user);
        using var there = await _app.SignedInAs(user);

        var page = await (await here.PageAsync("/Settings?tab=security")).Content.ReadAsStringAsync();
        Assert.Contains("This browser", page);
        Assert.Contains("Sign out everywhere else", page);

        var response = await here.PostFormAsync("/Settings/SignOutOtherSessions", "/Settings", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        Assert.True(AcaClient.IsSignInPage((await there.GetAsync("/Notifications")).Headers.Location));
        Assert.Equal(HttpStatusCode.OK, (await here.GetAsync("/Notifications")).StatusCode);
    }

    [LocalDbFact]
    public async Task TheSettingsPageHasOnlyWhatWorks()
    {
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.AccountManager, "settings-page"));
        var page = await (await client.PageAsync("/Settings?tab=notifications")).Content.ReadAsStringAsync();
        Assert.Contains("data-event-code=\"FileReady\"", page);
        Assert.Contains("id=\"twoStepSwitch\"", page);
        Assert.DoesNotContain("Create an API key", page);
        Assert.DoesNotContain("Connected Accounts", page);
        Assert.DoesNotContain("Deactivate Account", page);

        var deactivate = await client.PostFormAsync("/Settings/DeactivateAccount", "/Settings",
            new Dictionary<string, string> { ["accountActivation"] = "true" });
        Assert.NotEqual(HttpStatusCode.Redirect, deactivate.StatusCode);   // the action is gone
    }
}
