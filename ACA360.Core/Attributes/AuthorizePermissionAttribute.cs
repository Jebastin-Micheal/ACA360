using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Linq;
namespace ACA360.Core.Attributes
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
    public class AuthorizePermissionAttribute : Attribute, IAuthorizationFilter
    {
        private readonly string _actionName;

        public AuthorizePermissionAttribute(string actionName)
        {
            _actionName = actionName;
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var user = context.HttpContext.User;

            if (!user.Identity.IsAuthenticated)
            {
                context.Result = new RedirectToActionResult("Login", "Account", null);
                return;
            }
            // Get controller name
            var controllerName = context.RouteData.Values["controller"]?.ToString();


            // Get user claims of type "Permission"
            var permissions = user.Claims
                .Where(c => c.Type == "Permission")
                .Select(c => c.Value) // Format: "Controller:Action"
                .ToList();
            // Check if any of the required permissions are present
            var currentPermission = $"{controllerName}:{_actionName}";

            if (!permissions.Contains(currentPermission, System.StringComparer.OrdinalIgnoreCase))
            {
                context.Result = new ForbidResult(); // 403 Forbidden
            }
        }
    }
}
