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
    }
}
