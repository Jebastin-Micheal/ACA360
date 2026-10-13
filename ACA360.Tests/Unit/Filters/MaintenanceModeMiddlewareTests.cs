using System.Text.Json;
using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Constants;
using ACA360.Tests.Infrastructure;
using ACA360.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Moq;

namespace ACA360.Tests.Unit.Filters;

public class MaintenanceModeMiddlewareTests
{
    private static async Task<(HttpContext Http, bool Ran)> Run(bool maintenanceOn, System.Security.Claims.ClaimsPrincipal user,
        string path = "/Employer", string method = "GET", bool ajax = false, string? accept = FilterContexts.PageAccept)
    {
        var settings = new Mock<ISystemSettingsService>();
        settings.Setup(s => s.GetMaintenanceModeAsync()).ReturnsAsync(maintenanceOn);
        var ran = false;
        var middleware = new MaintenanceModeMiddleware(_ => { ran = true; return Task.CompletedTask; });
        var http = FilterContexts.Http(user, method, path, ajax, accept);
        await middleware.InvokeAsync(http, settings.Object);
        return (http, ran);
    }

    [Fact]
    public async Task WhenOffEveryoneGetsThrough() =>
        Assert.True((await Run(false, Principals.User(UserRoles.Broker))).Ran);

    [Theory]
    [InlineData(UserRoles.SuperAdmin)]
    [InlineData(UserRoles.Admin)]
    public async Task WhenOnAdministratorsStillGetThrough(string role) =>
        Assert.True((await Run(true, Principals.User(role))).Ran);

    [Fact]
    public async Task WhenOnOtherUsersAreSentToTheMaintenancePage()
    {
        var (http, ran) = await Run(true, Principals.User(UserRoles.AccountManager));
        Assert.False(ran);
        Assert.Equal(StatusCodes.Status302Found, http.Response.StatusCode);
        Assert.Equal("/System/Maintenance", http.Response.Headers.Location.ToString());
        Assert.Equal("300", http.Response.Headers.RetryAfter.ToString());
    }

    [Fact]
    public async Task WhenOnAjaxCallsGetA503Json()
    {
        var (http, ran) = await Run(true, Principals.User(UserRoles.DataAnalyst), ajax: true);
        Assert.False(ran);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, http.Response.StatusCode);
        http.Response.Body.Position = 0;
        var json = await JsonDocument.ParseAsync(http.Response.Body);
        Assert.True(json.RootElement.GetProperty("maintenance").GetBoolean());
    }

    [Fact]
    public async Task WhenOnPostsGetA503() =>
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, (await Run(true, Principals.User(UserRoles.Employer), method: "POST")).Http.Response.StatusCode);

    [Theory]
    [InlineData("/Account/Login")]
    [InlineData("/account/login")]
    [InlineData("/Account/Logout")]
    [InlineData("/Account/SetNewPassword")]
    [InlineData("/Account/ForgotPassword")]
    [InlineData("/System/Maintenance")]
    public async Task WhenOnTheSignInAndMaintenancePagesStayOpen(string path) =>
        Assert.True((await Run(true, Principals.Anonymous, path)).Ran);

    [Fact]
    public async Task WhenOnThePortalIsClosedToo() =>
        Assert.False((await Run(true, Principals.User(UserRoles.EmployeeSelfService), "/Portal/TaxForms")).Ran);

    [Fact]
    public async Task APathThatOnlyStartsLikeAnOpenOneIsNotOpen() =>
        Assert.False((await Run(true, Principals.Anonymous, "/Account/LoginHistory")).Ran);
}
