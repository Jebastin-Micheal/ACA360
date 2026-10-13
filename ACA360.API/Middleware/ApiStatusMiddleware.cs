using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using ACA360.BusinessLogic.Interfaces;

namespace ACA360.API.Middleware
{
    public class ApiStatusMiddleware
    {
        private readonly RequestDelegate _next;

        public ApiStatusMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, ISystemSettingsService settingsService)
        {
            // check the database (or cache) for the master switch
            // We use GetSystemSettingAsync which should be cached to prevent DB hammering
            bool isApiEnabled = await settingsService.GetBoolSettingAsync("IsApiEnabled");

            if (!isApiEnabled)
            {
                context.Response.StatusCode = 503; // Service Unavailable
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"error\": \"The API is currently disabled for maintenance.\"}");
                return;
            }

            await _next(context);
        }
    }
}