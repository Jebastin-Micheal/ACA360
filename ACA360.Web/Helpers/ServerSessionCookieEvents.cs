using ACA360.Repositories.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ACA360.Web.Helpers
{
    /// <summary>
    /// Backs every login cookie with a row in tbl_AuthSessions, so a session can be
    /// ended on the server (logout, idle timeout) rather than only by the browser
    /// deleting its cookie. Hooked into the cookie scheme itself, so every sign-in
    /// and sign-out path is covered without each controller having to remember.
    /// </summary>
    public class ServerSessionCookieEvents : CookieAuthenticationEvents
    {
        public const string SessionIdClaim = "SessionId";
        public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(15);

        // These run on a timer while a page is open. If they counted as activity,
        // an idle tab would never time out.
        private static readonly PathString[] BackgroundPaths =
        {
            new("/Notifications/GetNewNotifications"),
            new("/FileUpload/PollStatus")
        };

        private readonly IAuthSessionRepository _sessions;

        public ServerSessionCookieEvents(IAuthSessionRepository sessions)
        {
            _sessions = sessions;
        }

        public override async Task SigningIn(CookieSigningInContext context)
        {
            // No "remember me": the cookie always dies with the browser session.
            context.Properties.IsPersistent = false;
            context.Properties.ExpiresUtc = null;

            // Signing in over a live cookie gets a fresh session; the old one must
            // not stay usable.
            await RevokeAsync(context.HttpContext.User);

            var identity = (ClaimsIdentity)context.Principal!.Identity!;
            foreach (var stale in identity.FindAll(SessionIdClaim).ToList())
                identity.RemoveClaim(stale);

            var sessionId = Guid.NewGuid();
            await _sessions.CreateAsync(
                sessionId,
                SubjectOf(context.Principal),
                context.HttpContext.Connection.RemoteIpAddress?.ToString(),
                context.Request.Headers.UserAgent.ToString());

            identity.AddClaim(new Claim(SessionIdClaim, sessionId.ToString()));
        }

        public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
        {
            var touch = !BackgroundPaths.Any(p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

            if (TryGetSessionId(context.Principal, out var sessionId) &&
                await _sessions.ValidateAsync(sessionId, (int)IdleTimeout.TotalMinutes, touch))
            {
                return;
            }

            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }

        public override Task SigningOut(CookieSigningOutContext context)
        {
            return RevokeAsync(context.HttpContext.User);
        }

        public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
        {
            // An AJAX call would otherwise follow the redirect and receive the login
            // page's HTML as if it were data. A 401 lets _Layout send the whole page
            // to the login screen instead.
            if (string.Equals(context.Request.Headers.XRequestedWith, "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }
            return base.RedirectToLogin(context);
        }

        private async Task RevokeAsync(ClaimsPrincipal? principal)
        {
            if (TryGetSessionId(principal, out var sessionId))
                await _sessions.RevokeAsync(sessionId);
        }

        private static bool TryGetSessionId(ClaimsPrincipal? principal, out Guid sessionId)
        {
            sessionId = Guid.Empty;
            return Guid.TryParse(principal?.FindFirst(SessionIdClaim)?.Value, out sessionId);
        }

        private static string SubjectOf(ClaimsPrincipal? principal)
        {
            var userId = principal?.FindFirst("UserId")?.Value;
            if (!string.IsNullOrEmpty(userId)) return $"user:{userId}";

            var employeeId = principal?.FindFirst("EmployeeId")?.Value;
            if (!string.IsNullOrEmpty(employeeId)) return $"portal:{employeeId}";

            return $"name:{principal?.Identity?.Name}";
        }
    }
}
