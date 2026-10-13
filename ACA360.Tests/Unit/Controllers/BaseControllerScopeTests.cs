using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Constants;
using ACA360.Core.Models;
using ACA360.Tests.Infrastructure;
using ACA360.Web.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ACA360.Tests.Unit.Controllers;

/// <summary>The employer / employee scope checks every controller inherits from BaseController.</summary>
public class BaseControllerScopeTests
{
    /// <summary>Exposes BaseController's protected helpers.</summary>
    private sealed class Probe : BaseController
    {
        public Task<bool> EmployerPermitted(long id) => IsEmployerPermittedAsync(id);
        public Task<IActionResult?> EmployeeAccess(int id, bool asJson = true) => CheckEmployeeAccessAsync(id, asJson);
        public Task<bool> EmployeesPermitted(IEnumerable<int>? ids) => AreEmployeesPermittedAsync(ids);
        public Task<List<long>> Filter(IEnumerable<long>? ids) => FilterPermittedEmployerIdsAsync(ids);
        public Task<HashSet<long>> Permitted() => GetPermittedEmployerIdsAsync();
        public bool Unrestricted() => IsUnrestrictedEmployerRole();
        public string UserId() => GetCurrentUserId();
        public string Role() => GetEffectiveRole();
    }

    private readonly Mock<IViewAsService> _viewAs = new();
    private readonly Mock<IEmployeeService> _employees = new();
    private EffectiveContext _context = new() { ActorUserId = 7, ActorRole = UserRoles.AccountManager };

    private Probe Controller(string[] permitted, bool withServices = true, string role = UserRoles.AccountManager)
    {
        _viewAs.Setup(v => v.Resolve(It.IsAny<HttpContext>())).Returns(() => _context);
        _viewAs.Setup(v => v.GetPermittedEmployersAsync(It.IsAny<EffectiveContext>()))
               .ReturnsAsync(permitted.Select(id => new Employer { Id = id }).ToList());
        var services = new ServiceCollection();
        if (withServices)
        {
            services.AddSingleton(_viewAs.Object);
            services.AddSingleton(_employees.Object);
        }
        return new Probe
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = FilterContexts.Http(Principals.User(role), services: services.BuildServiceProvider()),
            },
        };
    }

    private void Employee(int id, long? employerId) =>
        _employees.Setup(e => e.GetEmployeeByIdAsync(id)).ReturnsAsync(new Employee { EmployerId = employerId });

    [Fact]
    public async Task AnEmployerInTheSetIsPermitted() => Assert.True(await Controller(new[] { "10", "11" }).EmployerPermitted(11));

    [Fact]
    public async Task AnEmployerOutsideTheSetIsNot() => Assert.False(await Controller(new[] { "10" }).EmployerPermitted(12));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task NoEmployerIsNeverPermitted(long id) => Assert.False(await Controller(new[] { "10" }).EmployerPermitted(id));

    [Fact]
    public async Task UnparseableEmployerIdsAreDroppedNotTreatedAsZero()
    {
        var permitted = await Controller(new[] { "10", "abc", "", "0" }).Permitted();
        Assert.Equal(new long[] { 10 }, permitted);
    }

    [Fact]
    public async Task WithoutTheViewAsServiceNothingIsPermitted() =>
        Assert.False(await Controller(new[] { "10" }, withServices: false).EmployerPermitted(10));

    [Fact]
    public async Task FilterKeepsOnlyPermittedIds() =>
        Assert.Equal(new long[] { 10 }, await Controller(new[] { "10", "11" }).Filter(new long[] { 10, 99, 10, 0, -3 }));

    [Fact]
    public async Task FilterOfNothingIsNothing() => Assert.Empty(await Controller(new[] { "10" }).Filter(null));

    [Fact]
    public async Task AnEmployeeOfAPermittedEmployerMayBeOpened()
    {
        Employee(5, 10);
        Assert.Null(await Controller(new[] { "10" }).EmployeeAccess(5));
    }

    [Fact]
    public async Task AnEmployeeOfAnotherEmployerIsForbidden()
    {
        Employee(5, 99);
        var result = await Controller(new[] { "10" }).EmployeeAccess(5);
        Assert.Equal(StatusCodes.Status403Forbidden, ResultOf.StatusCode(result));
        Assert.IsType<ForbidResult>(await Controller(new[] { "10" }).EmployeeAccess(5, asJson: false));
    }

    [Fact]
    public async Task AnEmployeeWithoutAnEmployerIsForbiddenToEveryone()
    {
        Employee(5, null);
        Assert.Equal(StatusCodes.Status403Forbidden, ResultOf.StatusCode(await Controller(new[] { "10" }).EmployeeAccess(5)));
    }

    [Fact]
    public async Task AnUnknownEmployeeIsNotFound()
    {
        Assert.IsType<NotFoundObjectResult>(await Controller(new[] { "10" }).EmployeeAccess(404));
        Assert.IsType<NotFoundResult>(await Controller(new[] { "10" }).EmployeeAccess(0, asJson: false));
    }

    [Fact]
    public async Task AListOfEmployeesPassesOnlyWhenEveryOneIsInScope()
    {
        _employees.Setup(e => e.GetEmployerIdsForEmployeesAsync(It.IsAny<IEnumerable<int>>()))
                  .ReturnsAsync(new Dictionary<int, long?> { [1] = 10, [2] = 10, [3] = 99, [4] = null });
        var controller = Controller(new[] { "10" });
        Assert.True(await controller.EmployeesPermitted(new[] { 1, 2, 1 }));
        Assert.False(await controller.EmployeesPermitted(new[] { 1, 3 }));
        Assert.False(await controller.EmployeesPermitted(new[] { 1, 4 }));
        Assert.False(await controller.EmployeesPermitted(new[] { 1, 5 }));   // 5 does not exist
        Assert.False(await controller.EmployeesPermitted(new[] { 1, 0 }));
    }

    [Fact]
    public async Task AnEmptyListOfEmployeesAsksForNothing() => Assert.True(await Controller(new[] { "10" }).EmployeesPermitted(null));

    [Theory]
    [InlineData(UserRoles.SuperAdmin, true)]
    [InlineData(UserRoles.DASupervisor, true)]
    [InlineData(UserRoles.StaffAU, true)]
    [InlineData(UserRoles.AccountManager, false)]
    [InlineData(UserRoles.DataAnalyst, false)]
    [InlineData(UserRoles.Broker, false)]
    public void OnlyTheAllEmployerRolesAreUnrestricted(string role, bool expected)
    {
        _context = new EffectiveContext { ActorUserId = 7, ActorRole = role };
        Assert.Equal(expected, Controller(new[] { "10" }, role: role).Unrestricted());
    }

    [Fact]
    public void AnAdminViewingAsAnEmployerIsRestricted()
    {
        _context = new EffectiveContext { ActorUserId = 1, ActorRole = UserRoles.SuperAdmin, Mode = ImpersonationMode.Employer, TargetId = 10 };
        var controller = Controller(new[] { "10" }, role: UserRoles.SuperAdmin);
        Assert.Equal(UserRoles.Employer, controller.Role());
        Assert.False(controller.Unrestricted());
    }

    [Fact]
    public void TheUserIdComesFromTheSignInClaim() => Assert.Equal("7", Controller(new[] { "10" }).UserId());
}

public class EffectiveContextTests
{
    [Fact]
    public void WithoutViewAsTheActorIsInCharge()
    {
        var context = new EffectiveContext { ActorUserId = 3, ActorRole = UserRoles.Admin };
        Assert.False(context.IsImpersonating);
        Assert.False(context.IsReadOnly);
        Assert.Equal(UserRoles.Admin, context.EffectiveRole);
        Assert.Equal(3, context.ScopeUserId);
        Assert.Equal("", context.BannerText);
    }

    [Fact]
    public void ViewingAsABrokerScopesToTheBroker()
    {
        var context = new EffectiveContext { ActorUserId = 3, ActorRole = UserRoles.Admin, Mode = ImpersonationMode.Broker, TargetId = 44, TargetName = "Acme Brokers" };
        Assert.True(context.IsReadOnly);
        Assert.Equal(UserRoles.Broker, context.EffectiveRole);
        Assert.Equal(44, context.ScopeUserId);
        Assert.Contains("Acme Brokers", context.BannerText);
    }

    [Fact]
    public void ViewingAsAnEmployerIsReadOnly()
    {
        var context = new EffectiveContext { ActorUserId = 3, ActorRole = UserRoles.Admin, Mode = ImpersonationMode.Employer, TargetId = 10 };
        Assert.True(context.IsReadOnly);
        Assert.Equal(UserRoles.Employer, context.EffectiveRole);
    }
}
