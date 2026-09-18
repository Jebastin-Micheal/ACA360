using ACA360.Core.Constants;
using ACA360.Core.Extensions;
using ACA360.Core.Models;
using Microsoft.AspNetCore.Http;
using ACA360.BusinessLogic.Interfaces;

namespace ACA360.BusinessLogic.Middleware
{
    public class SessionValidationMiddleware
    {
        private readonly RequestDelegate _next;

        public SessionValidationMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        // ISystemSettingsService is injected here to support Scoped services (like DB contexts)
        public async Task InvokeAsync(HttpContext context, ISystemSettingsService settings)
        {
            var path = context.Request.Path.Value?.ToLower();

            // --- 1. WHITELIST CHECK ---
            // Critical: Ensures Login, Logout, Maintenance page, and Employee Portal are always accessible.
            if (path.Contains("/account/login") ||
                path.Contains("/account/logout") ||
                path.Contains("/system/maintenance") || // Prevents redirect loops when in maintenance
                path.Contains("/account/accessdenied") ||
                path.Contains("/myforms") || // Correctly allows Employee Portal access without full session checks
                path.Contains(".") ||        // Skips static files (css, js, images)
                path.Contains("/api/") ||
                path.Contains("/session/") ||
                path.Contains("/favicon"))
            {
                await _next(context);
                return;
            }

            // --- 2. AUTHENTICATION CHECK ---
            var isAuthenticated = context.User?.Identity?.IsAuthenticated ?? false;

            if (!isAuthenticated)
            {
                context.Response.Redirect("/Account/Logout");
                return;
            }

            // --- 3. SESSION INTEGRITY CHECK ---
            // Ensures that if the server restarted (clearing RAM session) but the user has a cookie,
            // they are forced to re-login to populate session variables.
            var years = context.Session.GetObject<List<FilingYearModel>>("FilingYear");
            if (years == null)
            {
                // Optional: Add logging here if needed
                context.Response.Redirect("/Account/Logout");
                return;
            }

            // --- 4. MAINTENANCE MODE CHECK ---
            // This relies on the optimized (cached) GetMaintenanceModeAsync we fixed earlier.
            var isMaintenance = await settings.GetMaintenanceModeAsync();

            if (isMaintenance &&
                !context.User.IsInRole(UserRoles.SuperAdmin) &&          // SuperAdmin bypasses
                !context.User.IsInRole(UserRoles.Admin) &&               // Admin bypasses
                !context.Request.Path.StartsWithSegments("/Account"))    // Allow Account actions (like Logout)
            {
                context.Response.Redirect("/System/Maintenance");
                return;
            }

            await _next(context);
        }
    }
}