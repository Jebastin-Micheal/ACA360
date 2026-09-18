using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Extensions
{
    public static class ClaimsPrincipalExtensions
    {
        public static bool HasPermission(this ClaimsPrincipal user, string controller, string action)
        {
            var permissionValue = $"{controller}:{action}";
            return user.Claims
                       .Where(c => c.Type == "Permission")
                       .Any(c => c.Value == permissionValue);
        }

        public static string GetRole(this ClaimsPrincipal user)
        {
            return user?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;
        }
        public static string GetProfileName(this ClaimsPrincipal user)
        {
            // Use the exact string key you used when adding the claim (e.g., "ProfileName")
            return user?.Claims.FirstOrDefault(c => c.Type == "ProfileName")?.Value;
        }
        public static string GetUserName(this ClaimsPrincipal user)
        {
            return user?.Identity?.Name;
        }
        public static bool HasAnyPermissionForController(this ClaimsPrincipal user, string controller)
        {
            return user.Claims
                       .Where(c => c.Type == "Permission")
                       .Any(c => c.Value.StartsWith($"{controller}:"));
        }
    }
}
