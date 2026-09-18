using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ACA360.Logging.Interfaces;

namespace ACA360.Core.Filters
{
    public class GlobalExceptionFilter : IExceptionFilter
    {
        private readonly ILoggerService _logger;

        public GlobalExceptionFilter(ILoggerService logger)
        {
            _logger = logger;
        }

        public void OnException(ExceptionContext context)
        {
            var exception = context.Exception;
            var controllerName = context.RouteData.Values["controller"]?.ToString() ?? "UnknownController";
            var actionName = context.RouteData.Values["action"]?.ToString() ?? "UnknownAction";
            var callSite = $"{controllerName}.{actionName}";
            var userIp = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown IP";

            _logger.LogError(
                exception,
                callSite,
                "UnhandledException",
                "Exception captured by global filter",
                userIp
            );

            context.Result = new RedirectToActionResult("Error", "Home", null);
            context.ExceptionHandled = true;
        }
    }
}
