using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Constants;
using System.Security.Claims;

namespace ACA360.Web.Middleware
{
    /// <summary>
    /// Enforces System settings > Maintenance > Maintenance Mode. While it is on, only SuperAdmin
    /// and Admin can use ACA360 (the Employee Portal included). Everyone else gets the maintenance
    /// page for page requests and a 503 JSON answer for AJAX / API calls. The sign-in pages stay
    /// open so an administrator can still log in.
    /// Static files are served earlier in the pipeline and are never blocked.
    /// </summary>
    public class MaintenanceModeMiddleware
    {
        // Always reachable, so an administrator can sign in and users can see why they are blocked.
        private static readonly string[] OpenPaths =
        {
            "/Account/Login", "/Account/Logout", "/Account/AccessDenied", "/Account/ForgotPassword",
            "/Account/SetNewPassword", "/System/Maintenance"
        };

        private readonly RequestDelegate _next;

        public MaintenanceModeMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext context, ISystemSettingsService settings)
        {
            if (IsOpen(context.Request.Path) || IsAdministrator(context.User) || !await settings.GetMaintenanceModeAsync())
            {
                await _next(context);
                return;
            }

            context.Response.Headers["Retry-After"] = "300";
            context.Response.Headers.CacheControl = "no-store";

            if (WantsPage(context.Request))
            {
                context.Response.Redirect(context.Request.PathBase + "/System/Maintenance");
                return;
            }

            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                maintenance = true,
                message = "ACA360 is down for scheduled maintenance. Please try again later."
            });
        }

        private static bool IsOpen(PathString path) =>
            OpenPaths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

        private static bool IsAdministrator(ClaimsPrincipal user) =>
            user?.Identity?.IsAuthenticated == true &&
            (user.IsInRole(UserRoles.SuperAdmin) || user.IsInRole(UserRoles.Admin));

        // A normal page load (address bar, link, form) rather than AJAX / fetch / API.
        private static bool WantsPage(HttpRequest request) =>
            HttpMethods.IsGet(request.Method) &&
            !string.Equals(request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase) &&
            request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);
    }
}
