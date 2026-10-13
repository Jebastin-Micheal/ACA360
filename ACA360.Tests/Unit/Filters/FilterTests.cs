using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Constants;
using ACA360.Core.Models;
using ACA360.Tests.Infrastructure;
using ACA360.Web.Filters;
using Hangfire;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ACA360.Tests.Unit.Filters;

public class PortalAreaOnlyFilterTests
{
    private static IActionResult? Run(System.Security.Claims.ClaimsPrincipal user, string? area = null, string method = "GET",
        bool ajax = false, string? accept = FilterContexts.PageAccept, bool allowAnonymous = false)
    {
        var http = FilterContexts.Http(user, method, ajax: ajax, accept: accept);
        var context = FilterContexts.Authorization(FilterContexts.Action(http, "Faq", "Index", area, allowAnonymous));
        new PortalAreaOnlyFilter().OnAuthorization(context);
        return context.Result;
    }

    private static readonly System.Security.Claims.ClaimsPrincipal PortalUser = Principals.User(UserRoles.EmployeeSelfService);

    [Fact]
    public void StaffAreNotAffected() => Assert.Null(Run(Principals.User(UserRoles.Admin)));

    [Fact]
    public void AnonymousRequestsAreLeftToSignIn() => Assert.Null(Run(Principals.Anonymous));

    [Fact]
    public void PortalUsersMayUseThePortalArea() => Assert.Null(Run(PortalUser, area: "Portal"));

    [Fact]
    public void PortalUsersMayOpenPublicPages() => Assert.Null(Run(PortalUser, allowAnonymous: true));

    [Fact]
    public void APortalUserOpeningAStaffPageIsSentBackToThePortal()
    {
        var redirect = Assert.IsType<RedirectResult>(Run(PortalUser));
        Assert.Equal("/Portal/TaxForms", redirect.Url);
    }

    [Fact]
    public void APortalUsersAjaxCallToStaffCodeIsRefused() =>
        Assert.Equal(StatusCodes.Status403Forbidden, ResultOf.StatusCode(Run(PortalUser, ajax: true)));

    [Fact]
    public void APortalUsersPostToStaffCodeIsRefused() =>
        Assert.Equal(StatusCodes.Status403Forbidden, ResultOf.StatusCode(Run(PortalUser, method: "POST")));

    [Fact]
    public void APortalUsersApiCallIsRefused() =>
        Assert.Equal(StatusCodes.Status403Forbidden, ResultOf.StatusCode(Run(PortalUser, accept: "application/json")));
}

public class RequireEmployerScopeAttributeTests
{
    private static (IActionResult? Result, bool Ran) Run(RequireEmployerScopeAttribute attribute,
        Dictionary<string, object?> arguments, IEnumerable<string>? declared = null, bool ajax = false,
        bool withViewAs = true, params string[] permittedIds)
    {
        var services = new ServiceCollection().AddLogging();
        if (withViewAs)
        {
            var viewAs = new Mock<IViewAsService>();
            viewAs.Setup(v => v.Resolve(It.IsAny<HttpContext>())).Returns(new EffectiveContext { ActorUserId = 7, ActorRole = UserRoles.AccountManager });
            viewAs.Setup(v => v.GetPermittedEmployersAsync(It.IsAny<EffectiveContext>()))
                  .ReturnsAsync(permittedIds.Select(id => new Employer { Id = id }).ToList());
            services.AddSingleton(viewAs.Object);
        }

        var http = FilterContexts.Http(Principals.User(UserRoles.AccountManager), ajax: ajax, services: services.BuildServiceProvider());
        var action = FilterContexts.Action(http, "Employer", "Details", parameterNames: declared ?? arguments.Keys);
        var context = FilterContexts.Executing(action, arguments);
        var (next, called) = FilterContexts.Next(context);
        attribute.OnActionExecutionAsync(context, next).GetAwaiter().GetResult();
        return (context.Result, called());
    }

    private static Dictionary<string, object?> Args(string name, object? value) => new() { [name] = value };

    [Fact]
    public void APermittedEmployerPasses()
    {
        var (result, ran) = Run(new RequireEmployerScopeAttribute(), Args("employerId", 10), permittedIds: new[] { "10", "11" });
        Assert.Null(result);
        Assert.True(ran);
    }

    [Fact]
    public void AnotherEmployerIsForbidden()
    {
        var (result, ran) = Run(new RequireEmployerScopeAttribute(), Args("employerId", 99), permittedIds: "10");
        Assert.IsType<ForbidResult>(result);
        Assert.False(ran);
    }

    [Fact]
    public void AjaxCallersGetA403Json()
    {
        var (result, _) = Run(new RequireEmployerScopeAttribute(), Args("employerId", 99), ajax: true, permittedIds: "10");
        Assert.Equal(StatusCodes.Status403Forbidden, ResultOf.StatusCode(result));
        Assert.False(ResultOf.Json(result).GetProperty("success").GetBoolean());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ZeroOrLessMeansNotSuppliedAndFallsBackToSession(int id)
    {
        var (result, ran) = Run(new RequireEmployerScopeAttribute(), Args("employerId", id), permittedIds: "10");
        Assert.Null(result);
        Assert.True(ran);
    }

    [Fact]
    public void ADeclaredButEmptyParameterPasses()
    {
        var (_, ran) = Run(new RequireEmployerScopeAttribute(), new Dictionary<string, object?>(), declared: new[] { "employerId" }, permittedIds: "10");
        Assert.True(ran);
    }

    [Fact]
    public void AParameterTheActionDoesNotDeclareFailsClosed()
    {
        var (result, ran) = Run(new RequireEmployerScopeAttribute("companyId"), new Dictionary<string, object?>(), declared: new[] { "employerId" }, permittedIds: "10");
        Assert.IsType<ForbidResult>(result);
        Assert.False(ran);
    }

    [Fact]
    public void ParameterNamesMatchWithoutRegardToCase()
    {
        var (_, ran) = Run(new RequireEmployerScopeAttribute("EmployerID"), Args("employerId", 10), permittedIds: "10");
        Assert.True(ran);
    }

    [Theory]
    [InlineData("10,11")]
    [InlineData(" 10 , 11 ")]
    public void AListOfPermittedIdsPasses(string csv) =>
        Assert.True(Run(new RequireEmployerScopeAttribute("ids"), Args("ids", csv), permittedIds: new[] { "10", "11" }).Ran);

    [Fact]
    public void OneForeignIdInAListRefusesTheWholeCall() =>
        Assert.False(Run(new RequireEmployerScopeAttribute("ids"), Args("ids", "10,99"), permittedIds: new[] { "10", "11" }).Ran);

    [Fact]
    public void CollectionsAreCheckedItemByItem()
    {
        Assert.True(Run(new RequireEmployerScopeAttribute("ids"), Args("ids", new List<int> { 10, 11 }), permittedIds: new[] { "10", "11" }).Ran);
        Assert.False(Run(new RequireEmployerScopeAttribute("ids"), Args("ids", new long[] { 10, 99 }), permittedIds: new[] { "10", "11" }).Ran);
    }

    [Fact]
    public void SeveralParametersAreAllChecked() =>
        Assert.False(Run(new RequireEmployerScopeAttribute("employerId", "targetId"),
            new Dictionary<string, object?> { ["employerId"] = 10, ["targetId"] = 99 }, permittedIds: "10").Ran);

    [Fact]
    public void WithoutTheViewAsServiceEveryoneIsRefused() =>
        Assert.False(Run(new RequireEmployerScopeAttribute(), Args("employerId", 10), withViewAs: false).Ran);
}

public class ImpersonationReadOnlyFilterTests
{
    private static (IActionResult? Result, bool Ran) Run(string method, bool impersonating, string controller = "Employer",
        string action = "Save", bool ajax = false)
    {
        var viewAs = new Mock<IViewAsService>();
        viewAs.Setup(v => v.Resolve(It.IsAny<HttpContext>())).Returns(new EffectiveContext
        {
            ActorUserId = 1, ActorRole = UserRoles.SuperAdmin,
            Mode = impersonating ? ImpersonationMode.Employer : ImpersonationMode.None, TargetId = 10,
        });
        var http = FilterContexts.Http(Principals.User(UserRoles.SuperAdmin), method, ajax: ajax);
        var context = FilterContexts.Executing(FilterContexts.Action(http, controller, action));
        var (next, called) = FilterContexts.Next(context);
        new ImpersonationReadOnlyFilter(viewAs.Object).OnActionExecutionAsync(context, next).GetAwaiter().GetResult();
        return (context.Result, called());
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public void ReadingIsAllowedWhileViewingAs(string method) => Assert.True(Run(method, impersonating: true).Ran);

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public void ChangesAreRefusedWhileViewingAs(string method)
    {
        var (result, ran) = Run(method, impersonating: true);
        Assert.False(ran);
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public void AjaxChangesGetA403Json()
    {
        var (result, _) = Run("POST", impersonating: true, ajax: true);
        Assert.Equal(StatusCodes.Status403Forbidden, ResultOf.StatusCode(result));
    }

    [Fact]
    public void ChangesAreAllowedInYourOwnView() => Assert.True(Run("POST", impersonating: false).Ran);

    [Fact]
    public void LeavingViewAsIsAlwaysAllowed() => Assert.True(Run("POST", impersonating: true, controller: "ViewAs", action: "End").Ran);

    [Fact]
    public void OtherViewAsActionsAreStillRefused() => Assert.False(Run("POST", impersonating: true, controller: "ViewAs", action: "Begin").Ran);
}

public class HangfireAuthorizationFilterTests
{
    private static bool Authorize(System.Security.Claims.ClaimsPrincipal user)
    {
        var http = FilterContexts.Http(user, services: new ServiceCollection().BuildServiceProvider());
        var context = new AspNetCoreDashboardContext(new Mock<JobStorage>().Object, new DashboardOptions(), http);
        return new HangfireAuthorizationFilter().Authorize(context);
    }

    [Theory]
    [InlineData(UserRoles.SuperAdmin)]
    [InlineData(UserRoles.Admin)]
    public void AdministratorsMayOpenTheJobDashboard(string role) => Assert.True(Authorize(Principals.User(role)));

    [Theory]
    [InlineData(UserRoles.ACADirector)]
    [InlineData(UserRoles.DataAnalyst)]
    [InlineData(UserRoles.Broker)]
    [InlineData(UserRoles.Employer)]
    [InlineData(UserRoles.EmployeeSelfService)]
    public void OtherRolesMayNot(string role) => Assert.False(Authorize(Principals.User(role)));

    [Fact]
    public void AnonymousUsersMayNot() => Assert.False(Authorize(Principals.Anonymous));
}
