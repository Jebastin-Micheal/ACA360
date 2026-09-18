using ACA360.BusinessLogic.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Threading.Tasks;

namespace ACA360.Web.Filters
{
    /// <summary>
    /// Refuses every state-changing request while a View As session is active.
    /// Registered globally, so a new controller is covered the day it is written
    /// rather than the day someone remembers to annotate it.
    /// </summary>
    public class ImpersonationReadOnlyFilter : IAsyncActionFilter
    {
        private readonly IViewAsService _viewAs;

        public ImpersonationReadOnlyFilter(IViewAsService viewAs) => _viewAs = viewAs;

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var http = context.HttpContext;
            var method = http.Request.Method;

            var isSafe = HttpMethods.IsGet(method)
                      || HttpMethods.IsHead(method)
                      || HttpMethods.IsOptions(method);

            if (!isSafe)
            {
                var ctx = _viewAs.Resolve(http);
                if (ctx.IsReadOnly && !IsExempt(context))
                {
                    var msg = "This action is not available while viewing as another user. "
                            + "Return to your own view first.";

                    if (IsAjax(http))
                        context.Result = new JsonResult(new { success = false, message = msg })
                        { StatusCode = StatusCodes.Status403Forbidden };
                    else
                        context.Result = new ForbidResult();

                    return;
                }
            }

            await next();
        }

        // Only the exit route may be POSTed while impersonating — otherwise a user
        // could get stuck in a mode they cannot leave.
        private static bool IsExempt(ActionExecutingContext ctx)
        {
            var controller = ctx.RouteData.Values["controller"]?.ToString();
            var action = ctx.RouteData.Values["action"]?.ToString();

            return string.Equals(controller, "ViewAs", System.StringComparison.OrdinalIgnoreCase)
                && string.Equals(action, "End", System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAjax(HttpContext http) =>
            http.Request.Headers["X-Requested-With"] == "XMLHttpRequest"
            || (http.Request.Headers["Accept"].ToString()?.Contains("application/json") ?? false);
    }
}