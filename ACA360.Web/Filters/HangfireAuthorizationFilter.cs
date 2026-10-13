using ACA360.Core.Constants;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Http;
using System.Linq;

namespace ACA360.Web.Filters
{
    public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
    {
        public bool Authorize(DashboardContext context)
        {
            var httpContext = context.GetHttpContext();

            // 1. Ensure User is Logged In
            if (httpContext.User.Identity == null || !httpContext.User.Identity.IsAuthenticated)
            {
                return false;
            }

            // 2. Ensure User is an Admin (Adjust role name as needed)
            if (httpContext.User.IsInRole(UserRoles.SuperAdmin) || httpContext.User.IsInRole(UserRoles.Admin))
            {
                return true;
            }

            return false;
        }
    }
}