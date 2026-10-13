using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace ACA360.Tests.Infrastructure;

/// <summary>Signed-in users shaped like the ones AccountController.Login creates.</summary>
public static class Principals
{
    public static ClaimsPrincipal Anonymous => new(new ClaimsIdentity());

    public static ClaimsPrincipal User(string role, int userId = 7, params string[] permissions)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, $"user{userId}@example.com"),
            new(ClaimTypes.Role, role),
            new("UserId", userId.ToString()),
        };
        claims.AddRange(permissions.Select(p => new Claim("Permission", p)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestCookie"));
    }
}

/// <summary>Builds the MVC filter contexts a filter sees during a real request.</summary>
public static class FilterContexts
{
    public static DefaultHttpContext Http(ClaimsPrincipal? user = null, string method = "GET", string path = "/",
        bool ajax = false, string? accept = null, IServiceProvider? services = null)
    {
        var http = new DefaultHttpContext { User = user ?? Principals.Anonymous };
        http.Request.Method = method;
        http.Request.Path = path;
        if (ajax) http.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        if (accept != null) http.Request.Headers.Accept = accept;
        if (services != null) http.RequestServices = services;
        http.Response.Body = new MemoryStream();
        return http;
    }

    public const string PageAccept = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";

    public static ActionContext Action(HttpContext http, string controller, string action, string? area = null,
        bool allowAnonymous = false, IEnumerable<string>? parameterNames = null)
    {
        var route = new RouteData();
        route.Values["controller"] = controller;
        route.Values["action"] = action;
        if (area != null) route.Values["area"] = area;
        var descriptor = new ControllerActionDescriptor
        {
            ControllerName = controller,
            ActionName = action,
            DisplayName = $"{controller}.{action}",
            EndpointMetadata = allowAnonymous ? new List<object> { new AllowAnonymousAttribute() } : new List<object>(),
            Parameters = (parameterNames ?? Array.Empty<string>())
                .Select(n => new ParameterDescriptor { Name = n, ParameterType = typeof(object) }).ToList(),
        };
        return new ActionContext(http, route, descriptor);
    }

    public static AuthorizationFilterContext Authorization(ActionContext action) =>
        new(action, new List<IFilterMetadata>());

    public static ActionExecutingContext Executing(ActionContext action, IDictionary<string, object?>? arguments = null) =>
        new(action, new List<IFilterMetadata>(), arguments ?? new Dictionary<string, object?>(), controller: new object());

    /// <summary>An ActionExecutionDelegate that records whether the action was allowed to run.</summary>
    public static (ActionExecutionDelegate Next, Func<bool> Called) Next(ActionExecutingContext context)
    {
        var called = false;
        ActionExecutionDelegate next = () =>
        {
            called = true;
            return Task.FromResult(new ActionExecutedContext(context, new List<IFilterMetadata>(), context.Controller));
        };
        return (next, () => called);
    }
}

public static class ResultOf
{
    /// <summary>The anonymous object inside a JsonResult / ObjectResult, as JSON.</summary>
    public static JsonElement Json(IActionResult? result) => result switch
    {
        JsonResult j => JsonSerializer.SerializeToElement(j.Value),
        ObjectResult o => JsonSerializer.SerializeToElement(o.Value),
        _ => throw new Xunit.Sdk.XunitException($"Expected a JSON result, got {result?.GetType().Name ?? "null"}"),
    };

    public static int? StatusCode(IActionResult? result) => result switch
    {
        JsonResult j => j.StatusCode,
        ObjectResult o => o.StatusCode,
        StatusCodeResult s => s.StatusCode,
        _ => null,
    };
}
