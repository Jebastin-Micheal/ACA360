using System.Diagnostics;
using System.Security.Claims;
using ACA360.Logging.Interfaces;

namespace ACA360.Web.Middleware
{
    /// <summary>
    /// Writes an Info entry to System > Logs for every request that takes longer than the
    /// threshold (3 seconds in Program.cs), so a slow page on someone's copy of ACA360 shows
    /// which call was slow: method, path, time, status code, user id and role. The query string
    /// is left out, as a search there can be part of an SSN. Static files are served earlier in
    /// the pipeline and are not timed.
    /// </summary>
    public class SlowRequestLogMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly TimeSpan _threshold;

        public SlowRequestLogMiddleware(RequestDelegate next, TimeSpan threshold)
        {
            _next = next;
            _threshold = threshold;
        }

        public async Task InvokeAsync(HttpContext context, ILoggerService logger)
        {
            var timer = Stopwatch.StartNew();
            try
            {
                await _next(context);
            }
            finally
            {
                timer.Stop();
                if (timer.Elapsed >= _threshold)
                {
                    var user = context.User;
                    logger.LogInfo(
                        $"Slow request: {context.Request.Method} {context.Request.Path} took {timer.ElapsedMilliseconds} ms",
                        nameof(SlowRequestLogMiddleware),
                        $"Status {context.Response.StatusCode}; user {user.FindFirstValue("UserId") ?? "-"}; role {user.FindFirstValue(ClaimTypes.Role) ?? "-"}",
                        context.Connection.RemoteIpAddress?.ToString() ?? "");
                }
            }
        }
    }
}
