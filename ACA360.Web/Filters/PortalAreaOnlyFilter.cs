using ACA360.Core.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Linq;

namespace ACA360.Web.Filters
{
    /// <summary>
    /// Keeps Employee Portal sign-ins inside the Portal area.
    ///
    /// Portal employees sign in with the same cookie scheme as staff (role EmployeeSelfService),
    /// so without this every staff page that only asks for "signed in" (FAQ, calendar,
    /// shortcuts, ...) would accept them. Here a portal principal may use the Portal area and
    /// pages open to everyone ([AllowAnonymous]: sign-in, maintenance); anything else sends a
    /// page load back to the Portal and refuses AJAX / API calls with 403.
    /// </summary>
    public class PortalAreaOnlyFilter : IAuthorizationFilter
    {
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var user = context.HttpContext.User;
            if (user?.Identity?.IsAuthenticated != true || !user.IsInRole(UserRoles.EmployeeSelfService))
                return;

            var area = context.RouteData.Values["area"] as string;
            if (string.Equals(area, "Portal", StringComparison.OrdinalIgnoreCase))
                return;

            if (context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
                return;

            var request = context.HttpContext.Request;
            var isPage = HttpMethods.IsGet(request.Method)
                && !string.Equals(request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
                && request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);

            context.Result = isPage
                ? new RedirectResult(request.PathBase + "/Portal/TaxForms")
                : new ObjectResult(new { success = false, message = "This page is not available to Employee Portal accounts." })
                  { StatusCode = StatusCodes.Status403Forbidden };
        }
    }
}
