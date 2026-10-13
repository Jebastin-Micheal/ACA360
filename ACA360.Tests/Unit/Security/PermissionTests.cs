using System.Security.Claims;
using ACA360.Core.Attributes;
using ACA360.Core.Constants;
using ACA360.Core.Extensions;
using ACA360.Security.Authorization;
using ACA360.Tests.Infrastructure;
using ACA360.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ACA360.Tests.Unit.Security;

public class AuthorizePermissionAttributeTests
{
    private static IActionResult? Run(ClaimsPrincipal user, string controller, string permission)
    {
        var context = FilterContexts.Authorization(FilterContexts.Action(FilterContexts.Http(user), controller, "Whatever"));
        new AuthorizePermissionAttribute(permission).OnAuthorization(context);
        return context.Result;
    }

    [Fact]
    public void AnonymousUsersAreSentToSignIn()
    {
        var result = Assert.IsType<RedirectToActionResult>(Run(Principals.Anonymous, "Employer", "Edit"));
        Assert.Equal("Login", result.ActionName);
        Assert.Equal("Account", result.ControllerName);
    }

    [Fact]
    public void AUserWithTheControllersPermissionPasses() =>
        Assert.Null(Run(Principals.User(UserRoles.Admin, permissions: "Employer:Edit"), "Employer", "Edit"));

    [Fact]
    public void ThePermissionIsMatchedWithoutRegardToCase() =>
        Assert.Null(Run(Principals.User(UserRoles.Admin, permissions: "employer:EDIT"), "Employer", "Edit"));

    [Fact]
    public void AUserWithoutThePermissionIsForbidden() =>
        Assert.IsType<ForbidResult>(Run(Principals.User(UserRoles.Admin, permissions: "Employer:Index"), "Employer", "Edit"));

    [Fact]
    public void ThePermissionMustBeForTheCurrentController() =>
        Assert.IsType<ForbidResult>(Run(Principals.User(UserRoles.Admin, permissions: "Employee:Edit"), "Employer", "Edit"));

    [Fact]
    public void ARoleAloneIsNotEnough() =>
        Assert.IsType<ForbidResult>(Run(Principals.User(UserRoles.SuperAdmin), "Employer", "Delete"));
}

public class ClaimsPrincipalExtensionsTests
{
    [Fact]
    public void HasPermissionFindsAGrantedPermission() =>
        Assert.True(Principals.User(UserRoles.Admin, permissions: new[] { "Tracker:Index", "Employee:Delete" })
            .HasPermission("Employee", "Delete"));

    [Fact]
    public void HasPermissionIsFalseWhenNotGranted() =>
        Assert.False(Principals.User(UserRoles.Admin, permissions: "Employee:Edit").HasPermission("Employee", "Delete"));

    [Fact]
    public void HasPermissionIgnoresOtherClaimTypes()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("Other", "Employee:Delete") }, "Test"));
        Assert.False(user.HasPermission("Employee", "Delete"));
    }
}

public class PiiAccessTests
{
    [Theory]
    [InlineData(UserRoles.SuperAdmin)]
    [InlineData(UserRoles.Admin)]
    [InlineData(UserRoles.ACADirector)]
    [InlineData(UserRoles.DASupervisor)]
    [InlineData(UserRoles.AMSupervisor)]
    [InlineData(UserRoles.DataAnalyst)]
    [InlineData(UserRoles.AccountManager)]
    [InlineData(UserRoles.StaffAC)]
    [InlineData(UserRoles.StaffAU)]
    public void InternalStaffSeeFullPii(string role) => Assert.True(Principals.User(role).CanViewFullPii());

    [Theory]
    [InlineData(UserRoles.Broker)]
    [InlineData(UserRoles.Employer)]
    [InlineData(UserRoles.EmployeeSelfService)]
    public void ClientsAndPortalUsersSeeMaskedPii(string role) => Assert.False(Principals.User(role).CanViewFullPii());

    [Fact]
    public void AnonymousUsersSeeMaskedPii() => Assert.False(Principals.Anonymous.CanViewFullPii());
}

public class CustomRequirementHandlerTests
{
    private static async Task<bool> Succeeds(ClaimsPrincipal user)
    {
        var requirement = new CustomRequirement("Role", "Admin");
        var context = new AuthorizationHandlerContext(new[] { requirement }, user, null);
        await new CustomRequirementHandler().HandleAsync(context);
        return context.HasSucceeded;
    }

    [Fact]
    public async Task SucceedsWithTheExactClaim() =>
        Assert.True(await Succeeds(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("Role", "Admin") }, "Test"))));

    [Fact]
    public async Task FailsWithADifferentValue() =>
        Assert.False(await Succeeds(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("Role", "Broker") }, "Test"))));

    [Fact]
    public async Task FailsWithoutTheClaim() => Assert.False(await Succeeds(Principals.Anonymous));
}

public class UserRolesTests
{
    [Fact]
    public void EveryRoleInAllIsARealRole()
    {
        var roles = typeof(UserRoles).GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!).Where(v => !v.Contains(',')).ToHashSet();
        Assert.All(UserRoles.All.Split(','), r => Assert.Contains(r, roles));
        Assert.All(UserRoles.AllEmployerRoles, r => Assert.Contains(r, roles));
    }

    [Fact]
    public void ClientRolesAreNeverUnrestricted()
    {
        Assert.DoesNotContain(UserRoles.Broker, UserRoles.AllEmployerRoles);
        Assert.DoesNotContain(UserRoles.Employer, UserRoles.AllEmployerRoles);
        Assert.DoesNotContain(UserRoles.AccountManager, UserRoles.AllEmployerRoles);
        Assert.DoesNotContain(UserRoles.DataAnalyst, UserRoles.AllEmployerRoles);
    }

    [Fact]
    public void FileHandlersExcludeClientsAndStaffRoles()
    {
        var handlers = UserRoles.FileHandlers.Split(',');
        Assert.DoesNotContain(UserRoles.Broker, handlers);
        Assert.DoesNotContain(UserRoles.Employer, handlers);
        Assert.DoesNotContain(UserRoles.StaffAC, handlers);
        Assert.DoesNotContain(UserRoles.StaffAU, handlers);
    }

    [Fact]
    public void ThePortalRoleIsNotAStaffRole() => Assert.DoesNotContain(UserRoles.EmployeeSelfService, UserRoles.All.Split(','));
}
