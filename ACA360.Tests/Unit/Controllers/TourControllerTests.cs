using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Constants;
using ACA360.Core.Models.Dashboard;
using ACA360.Logging.Interfaces;
using ACA360.Tests.Infrastructure;
using ACA360.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace ACA360.Tests.Unit.Controllers;

public class TourControllerTests
{
    private readonly Mock<IDashboardService> _dashboards = new();
    private DashboardLayoutDto _stored = new();

    private TourController Controller(int userId = 7)
    {
        _dashboards.Setup(d => d.GetDashboardLayoutAsync(userId, "tours")).ReturnsAsync(() => _stored);
        _dashboards.Setup(d => d.SaveDashboardLayoutAsync(userId, It.IsAny<DashboardLayoutDto>(), "tours"))
                   .Callback<int, DashboardLayoutDto, string>((_, layout, _) => _stored = layout)
                   .Returns(Task.CompletedTask);
        var user = userId > 0 ? Principals.User(UserRoles.Admin, userId) : Principals.Anonymous;
        return new TourController(_dashboards.Object, new Mock<ILoggerService>().Object)
        {
            ControllerContext = new ControllerContext { HttpContext = FilterContexts.Http(user) },
        };
    }

    private static (bool Success, bool Seen) Status(IActionResult result)
    {
        var json = ResultOf.Json(result);
        return (json.GetProperty("success").GetBoolean(), json.GetProperty("seen").GetBoolean());
    }

    [Fact]
    public async Task ATourNotSeenYetStartsByItself() =>
        Assert.Equal((true, false), Status(await Controller().Status("home:1")));

    [Fact]
    public async Task ASeenTourDoesNotStartAgain()
    {
        _stored = new DashboardLayoutDto { Order = { "home:1" } };
        Assert.Equal((true, true), Status(await Controller().Status("home:1")));
    }

    [Fact]
    public async Task ANewVersionOfASeenTourStartsAgain()
    {
        _stored = new DashboardLayoutDto { Order = { "home:1" } };
        Assert.Equal((true, false), Status(await Controller().Status("home:2")));
    }

    [Fact]
    public async Task UpperCaseIdsFromTheGlobalBinderAreAccepted() =>
        Assert.Equal((true, false), Status(await Controller().Status("  TRACKER-EMPLOYER:3 ")));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("home")]
    [InlineData("home:")]
    [InlineData("home:1234")]
    [InlineData("<script>:1")]
    [InlineData("a-very-long-tour-name-that-goes-past-32:1")]
    public async Task UnknownIdsNeverStartATour(string? tour)
    {
        Assert.Equal((false, true), Status(await Controller().Status(tour!)));
        _dashboards.Verify(d => d.GetDashboardLayoutAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task WithoutASignedInUserNothingStarts() =>
        Assert.Equal((false, true), Status(await Controller(userId: 0).Status("home:1")));

    [Fact]
    public async Task IfTheStoreFailsTheTourDoesNotStart()
    {
        var controller = Controller();
        _dashboards.Setup(d => d.GetDashboardLayoutAsync(7, "tours")).ThrowsAsync(new InvalidOperationException("db down"));
        Assert.Equal((false, true), Status(await controller.Status("home:1")));
    }

    [Fact]
    public async Task SeenRecordsTheTour()
    {
        var result = await Controller().Seen("files:1");
        Assert.True(ResultOf.Json(result).GetProperty("success").GetBoolean());
        Assert.Equal(new[] { "files:1" }, _stored.Order);
    }

    [Fact]
    public async Task SeenReplacesAnOlderVersionAndKeepsOtherTours()
    {
        _stored = new DashboardLayoutDto { Order = { "home:1", "files:1" } };
        await Controller().Seen("home:2");
        Assert.Equal(new[] { "files:1", "home:2" }, _stored.Order);
    }

    [Fact]
    public async Task SeenTwiceDoesNotWriteAgain()
    {
        _stored = new DashboardLayoutDto { Order = { "home:1" } };
        await Controller().Seen("home:1");
        _dashboards.Verify(d => d.SaveDashboardLayoutAsync(It.IsAny<int>(), It.IsAny<DashboardLayoutDto>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SeenRefusesABadId()
    {
        var result = await Controller().Seen("../../etc:1");
        Assert.False(ResultOf.Json(result).GetProperty("success").GetBoolean());
        _dashboards.Verify(d => d.SaveDashboardLayoutAsync(It.IsAny<int>(), It.IsAny<DashboardLayoutDto>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ATourOnlyNameDoesNotRemoveToursWithTheSamePrefix()
    {
        _stored = new DashboardLayoutDto { Order = { "tracker-employer:1" } };
        await Controller().Seen("tracker:1");
        Assert.Equal(new[] { "tracker-employer:1", "tracker:1" }, _stored.Order);
    }
}
