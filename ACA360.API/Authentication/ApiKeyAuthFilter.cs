using ACA360.BusinessLogic;
using ACA360.Repositories.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace ACA360.API.Authentication
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class ApiKeyAuthAttribute : Attribute, IAsyncAuthorizationFilter
    {
        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var request = context.HttpContext.Request;

            // 1. Check Headers
            if (!request.Headers.TryGetValue("X-Client-Id", out var clientId) ||
                !request.Headers.TryGetValue("X-Client-Secret", out var clientSecret))
            {
                context.Result = new UnauthorizedObjectResult(new { error = "Missing Authentication Headers" });
                return;
            }

            // 2. Validate against Database (Via Service)
            var apiService = context.HttpContext.RequestServices.GetRequiredService<IApiClientService>();
            var client = await apiService.ValidateClientAsync(clientId.ToString(), clientSecret.ToString());

            if (client == null || !client.IsActive)
            {
                context.Result = new UnauthorizedObjectResult(new { error = "Invalid Credentials or Inactive Client" });
                return;
            }

            // 3. Store Context (So the controller knows WHICH employer this is)
            context.HttpContext.Items["EmployerId"] = client.EmployerId;
            context.HttpContext.Items["ClientName"] = client.Name;
        }
    }
}