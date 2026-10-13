using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace ACA360.Tests.Infrastructure;

/// <summary>
/// Every MVC controller and action in ACA360.Web, found by reflection the way MVC itself finds
/// them, so the convention tests cover a new controller the day it is added.
/// </summary>
public static class ControllerCatalog
{
    public static readonly Assembly WebAssembly = typeof(ACA360.Web.Helpers.BaseController).Assembly;

    public static IReadOnlyList<Type> Controllers { get; } = WebAssembly.GetTypes()
        .Where(t => t.IsClass && t.IsPublic && !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t)
                    && t.Name.EndsWith("Controller", StringComparison.Ordinal)
                    && t.GetCustomAttribute<NonControllerAttribute>() == null)
        .OrderBy(t => t.FullName)
        .ToList();

    public static IReadOnlyList<ActionInfo> Actions { get; } = Controllers
        .SelectMany(c => c.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => !m.IsSpecialName && m.DeclaringType != typeof(object)
                        && m.DeclaringType != typeof(Controller) && m.DeclaringType != typeof(ControllerBase)
                        && m.DeclaringType!.Assembly == WebAssembly
                        && m.GetCustomAttribute<NonActionAttribute>() == null)
            .Select(m => new ActionInfo(c, m)))
        .ToList();
}

public sealed record ActionInfo(Type Controller, MethodInfo Method)
{
    public string ControllerName => Controller.Name[..^"Controller".Length];

    public string ActionName => Method.GetCustomAttribute<ActionNameAttribute>()?.Name ?? Method.Name;

    public string? Area => Controller.GetCustomAttribute<AreaAttribute>()?.RouteValue;

    /// <summary>"Area/Controller/Action" (area left out when there is none), stable for allow-lists.</summary>
    public string Key => (Area is null ? "" : Area + "/") + ControllerName + "/" + ActionName;

    public IReadOnlyList<string> HttpMethods
    {
        get
        {
            var verbs = Method.GetCustomAttributes<HttpMethodAttribute>().SelectMany(a => a.HttpMethods).Distinct().ToList();
            return verbs.Count == 0 ? new[] { "ANY" } : verbs;   // no verb attribute: MVC accepts every verb
        }
    }

    public bool AcceptsGet => HttpMethods.Contains("ANY") || HttpMethods.Contains("GET");

    public bool ChangesState => HttpMethods.Any(v => v is "POST" or "PUT" or "DELETE" or "PATCH");

    /// <summary>
    /// The URL a GET for this action uses: the attribute route when there is one (route
    /// parameters filled with 0), otherwise the conventional /{area}/{controller}/{action}.
    /// </summary>
    public string Url
    {
        get
        {
            var controllerTemplate = Controller.GetCustomAttribute<RouteAttribute>()?.Template;
            var actionTemplate = Method.GetCustomAttributes<HttpMethodAttribute>().Select(a => a.Template).FirstOrDefault(t => t != null)
                                 ?? Method.GetCustomAttribute<RouteAttribute>()?.Template;
            string path;
            if (actionTemplate != null && actionTemplate.StartsWith('/'))
                path = actionTemplate;
            else if (controllerTemplate != null || actionTemplate != null)
                path = "/" + string.Join("/", new[] { controllerTemplate, actionTemplate }.Where(t => !string.IsNullOrEmpty(t)));
            else
                path = "/" + (Area is null ? "" : Area + "/") + ControllerName + "/" + ActionName;
            path = path.Replace("[controller]", ControllerName).Replace("[action]", ActionName);
            return System.Text.RegularExpressions.Regex.Replace(path, @"\{[^}]+\}", "0");
        }
    }

    /// <summary>True when MVC can call the action with no query string at all.</summary>
    public bool NeedsNoInput => Method.GetParameters().All(p =>
        p.HasDefaultValue || p.ParameterType.IsValueType || p.ParameterType == typeof(string)
        || Nullable.GetUnderlyingType(p.ParameterType) != null);

    public bool Has<TAttribute>() where TAttribute : Attribute =>
        Method.GetCustomAttributes<TAttribute>(true).Any() || Controller.GetCustomAttributes<TAttribute>(true).Any();

    public override string ToString() => Key;
}
