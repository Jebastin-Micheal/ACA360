using System.Net;
using ACA360.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ACA360.Tests.Integration;

[Collection(AppCollection.Name)]
public class SignInTests
{
    private readonly AcaWebFactory _app;

    public SignInTests(AcaWebFactory app) => _app = app;

    private static int OpenSessions(TestUser user) => LocalDb.Scalar<int>(
        "SELECT COUNT(*) FROM dbo.tbl_AuthSessions WHERE Subject = CONCAT(N'user:', @id) AND RevokedAt IS NULL", new { id = user.UserId });

    private static bool SignedIn(HttpResponseMessage response) =>
        response.StatusCode == HttpStatusCode.Redirect && !AcaClient.IsSignInPage(response.Headers.Location);

    [LocalDbFact]
    public async Task TheRightPasswordSignsInAndOpensAServerSession()
    {
        var user = TestUsers.CreateStaff(RoleIds.Admin, "signin");
        using var client = _app.NewClient();
        Assert.True(SignedIn(await client.SignInAsync(user.Email, user.Password)));
        Assert.Equal(1, OpenSessions(user));
        Assert.Equal(HttpStatusCode.OK, (await client.PageAsync("/Faq/Index")).StatusCode);
    }

    [LocalDbFact]
    public async Task AWrongPasswordIsRefused()
    {
        var user = TestUsers.CreateStaff(RoleIds.Admin, "wrongpw");
        using var client = _app.NewClient();
        Assert.False(SignedIn(await client.SignInAsync(user.Email, user.Password + "x")));
        Assert.Equal(0, OpenSessions(user));
    }

    [LocalDbFact]
    public async Task AnUnknownUserIsRefused()
    {
        using var client = _app.NewClient();
        Assert.False(SignedIn(await client.SignInAsync(TestUsers.Prefix + "nobody@example.com", "Whatever1!")));
    }

    [LocalDbFact]
    public async Task ASwitchedOffLoginIsRefused()
    {
        var user = TestUsers.CreateStaff(RoleIds.Admin, "flag0");
        LocalDb.Execute("UPDATE dbo.tbl_User SET Flag = 0 WHERE User_ID = @id", new { id = user.UserId });
        using var client = _app.NewClient();
        Assert.False(SignedIn(await client.SignInAsync(user.Email, user.Password)));
    }

    [LocalDbFact]
    public async Task ADeletedStaffAccountCannotSignIn()
    {
        var user = TestUsers.CreateStaff(RoleIds.AccountManager, "deleted");
        LocalDb.Execute("UPDATE dbo.StaffAccounts SET Staff_Status = 4 WHERE Staff_ID = @id", new { id = user.RefId });
        using var client = _app.NewClient();
        Assert.False(SignedIn(await client.SignInAsync(user.Email, user.Password)));
    }

    [LocalDbFact]
    public async Task ATemporaryPasswordLeadsToSettingANewOne()
    {
        var user = TestUsers.CreateStaff(RoleIds.Admin, "temppw");
        LocalDb.Execute("UPDATE dbo.tbl_User SET User_Password = NULL, Temp_Password = @hash WHERE User_ID = @id",
            new { id = user.UserId, hash = BCrypt.Net.BCrypt.HashPassword(user.Password) });
        using var client = _app.NewClient();
        var response = await client.SignInAsync(user.Email, user.Password);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/SetNewPassword", response.Headers.Location!.OriginalString);
    }

    [LocalDbFact]
    public async Task RepeatedWrongPasswordsLockTheAccount()
    {
        var user = TestUsers.CreateStaff(RoleIds.Admin, "lockout");
        using var client = _app.NewClient();
        for (var i = 0; i < 6; i++) await client.SignInAsync(user.Email, "Wrong-" + i);
        Assert.False(SignedIn(await client.SignInAsync(user.Email, user.Password)), "the right password still worked after six wrong ones");
    }

    [LocalDbFact]
    public async Task SigningOutEndsTheServerSession()
    {
        var user = TestUsers.CreateStaff(RoleIds.Admin, "signout");
        using var client = await _app.SignedInAs(user);
        Assert.Equal(1, OpenSessions(user));
        await client.GetAsync("/Account/Logout");
        Assert.Equal(0, OpenSessions(user));
        var after = await client.PageAsync("/Faq/Index");
        Assert.True(after.StatusCode == HttpStatusCode.Redirect && AcaClient.IsSignInPage(after.Headers.Location));
    }

    [LocalDbFact]
    public async Task ARevokedSessionCookieNoLongerWorks()
    {
        var user = TestUsers.CreateStaff(RoleIds.Admin, "revoked");
        using var client = await _app.SignedInAs(user);
        LocalDb.Execute("UPDATE dbo.tbl_AuthSessions SET RevokedAt = SYSUTCDATETIME() WHERE Subject = CONCAT(N'user:', @id)", new { id = user.UserId });
        var after = await client.PageAsync("/Faq/Index");
        Assert.True(after.StatusCode == HttpStatusCode.Redirect && AcaClient.IsSignInPage(after.Headers.Location),
            $"{(int)after.StatusCode} {after.Headers.Location}");
    }
}

[Collection(AppCollection.Name)]
public class StartupTests
{
    private readonly AcaWebFactory _app;

    public StartupTests(AcaWebFactory app) => _app = app;

    public static IEnumerable<object[]> Controllers() =>
        ACA360.Tests.Infrastructure.ControllerCatalog.Controllers.Select(c => new object[] { c.FullName! });

    [LocalDbTheory]
    [MemberData(nameof(Controllers))]
    public void EveryControllerCanBeBuiltFromTheAppsServices(string controller)
    {
        using var scope = _app.Services.CreateScope();
        var type = ACA360.Tests.Infrastructure.ControllerCatalog.WebAssembly.GetType(controller)!;
        Assert.NotNull(ActivatorUtilities.CreateInstance(scope.ServiceProvider, type));
    }

    [LocalDbFact]
    public async Task TheSignInPageLoads()
    {
        using var client = _app.NewClient();
        var response = await client.PageAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("__RequestVerificationToken", await response.Content.ReadAsStringAsync());
    }
}
