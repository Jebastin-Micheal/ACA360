using ACA360.Core.Constants;
using ACA360.Logging.Interfaces;
using ACA360.Tests.Infrastructure;
using ACA360.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Moq;

namespace ACA360.Tests.Unit.Filters;

public class SlowRequestLogMiddlewareTests
{
    private static async Task<Mock<ILoggerService>> Run(TimeSpan threshold, TimeSpan takes, string path = "/Employee/GetFilteredEmployeeIds")
    {
        var logger = new Mock<ILoggerService>();
        var middleware = new SlowRequestLogMiddleware(_ => Task.Delay(takes), threshold);
        var http = FilterContexts.Http(Principals.User(UserRoles.AccountManager), "GET", path, ajax: true, FilterContexts.PageAccept);
        http.Request.QueryString = new QueryString("?search=123-45");
        await middleware.InvokeAsync(http, logger.Object);
        return logger;
    }

    [Fact]
    public async Task ASlowRequestIsLoggedWithoutItsQueryString()
    {
        var logger = await Run(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(60));
        logger.Verify(l => l.LogInfo(
            It.Is<string>(m => m.StartsWith("Slow request: GET /Employee/GetFilteredEmployeeIds took ") && !m.Contains("123-45")),
            nameof(SlowRequestLogMiddleware),
            It.Is<string>(i => i.Contains(UserRoles.AccountManager) && !i.Contains("123-45")),
            It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task AQuickRequestIsNotLogged()
    {
        var logger = await Run(TimeSpan.FromSeconds(3), TimeSpan.Zero);
        logger.VerifyNoOtherCalls();
    }
}
