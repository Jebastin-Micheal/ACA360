using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ACA360.Web.Filters
{
    /// <summary>
    /// Refuses a request whose employer-id parameter is outside the caller's
    /// permitted set.
    ///
    /// Employer scope is otherwise carried in session, written only by
    /// SelectEmployerController after it verifies entitlement. An action that
    /// accepts an employer id as a parameter sidesteps that entirely, so it has
    /// to re-check — this attribute is that check in one place.
    ///
    /// Entitlement is resolved through IViewAsService.GetPermittedEmployersAsync,
    /// the same oracle SelectEmployer uses, so impersonation is handled: an admin
    /// viewing as one employer is scoped to that employer rather than to their own
    /// unrestricted set.
    ///
    /// <para>
    /// Runs as an action filter rather than an authorization filter because it
    /// needs the bound parameter values, which do not exist until model binding
    /// has run.
    /// </para>
    ///
    /// <para>
    /// Fails closed. A named parameter that is not present on the action is a
    /// wiring mistake, not a pass — most likely the parameter was renamed and the
    /// attribute was not. That returns 403 and logs, so it surfaces the first time
    /// the action is exercised rather than silently ceasing to guard anything.
    /// </para>
    ///
    /// <para>
    /// A value of zero or less means "not supplied" and is skipped, letting the
    /// action fall back to session scope. Several actions declare
    /// <c>int employerId = 0</c> for exactly that purpose.
    /// </para>
    /// </summary>
    /// <example>
    /// [RequireEmployerScope]                       // guards "employerId"
    /// [RequireEmployerScope("EmployerId")]         // different casing or name
    /// [RequireEmployerScope("employerId", "ids")]  // several parameters
    /// </example>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
    public sealed class RequireEmployerScopeAttribute : Attribute, IAsyncActionFilter
    {
        private readonly string[] _parameterNames;

        public RequireEmployerScopeAttribute(params string[] parameterNames)
        {
            _parameterNames = parameterNames is { Length: > 0 }
                ? parameterNames
                : new[] { "employerId" };
        }

        public async Task OnActionExecutionAsync(
            ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var http = context.HttpContext;
            var logger = http.RequestServices
                             .GetService<ILogger<RequireEmployerScopeAttribute>>();

            var viewAs = http.RequestServices.GetService<IViewAsService>();
            if (viewAs == null)
            {
                logger?.LogError(
                    "RequireEmployerScope on {Action} could not resolve IViewAsService; denying.",
                    context.ActionDescriptor.DisplayName);
                context.Result = Deny(http, "Employer scope could not be resolved.");
                return;
            }

            // Collect the requested ids first, so a wiring mistake is reported
            // without the cost of loading the permitted set.
            var requested = new List<long>();

            foreach (var name in _parameterNames)
            {
                if (!TryGetArgument(context, name, out var raw))
                {
                    logger?.LogError(
                        "RequireEmployerScope on {Action} names parameter '{Parameter}', which the " +
                        "action does not declare. Denying — check the attribute against the signature.",
                        context.ActionDescriptor.DisplayName, name);

                    context.Result = Deny(http, "Employer scope could not be resolved.");
                    return;
                }

                requested.AddRange(ExtractIds(raw));
            }

            // Nothing supplied: the action falls back to session scope, which is
            // already verified at the point it was written.
            if (requested.Count == 0)
            {
                await next();
                return;
            }

            var permitted = (await viewAs.GetPermittedEmployersAsync(viewAs.Resolve(http)) ?? new List<Employer>())
                            .Select(e => long.TryParse(e.Id, out var id) ? id : 0L)
                            .Where(id => id > 0)
                            .ToHashSet();

            var denied = requested.Where(id => !permitted.Contains(id)).Distinct().ToList();

            if (denied.Count > 0)
            {
                logger?.LogWarning(
                    "Employer scope denied. User {UserId} called {Action} for employer(s) {Denied}, " +
                    "which are not in their permitted set.",
                    http.User?.FindFirst("UserId")?.Value ?? "(unknown)",
                    context.ActionDescriptor.DisplayName,
                    string.Join(",", denied));

                context.Result = Deny(http, "You do not have access to that employer.");
                return;
            }

            await next();
        }

        /// <summary>
        /// Looks the parameter up case-insensitively. Action arguments are keyed on
        /// the declared parameter name, and call sites vary between employerId and
        /// EmployerId.
        /// </summary>
        private static bool TryGetArgument(ActionExecutingContext context, string name, out object? value)
        {
            foreach (var kvp in context.ActionArguments)
            {
                if (string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = kvp.Value;
                    return true;
                }
            }

            // Distinguish "declared but bound to null" from "not declared at all".
            // The former is a legitimate absent value; the latter is a wiring fault.
            bool declared = context.ActionDescriptor.Parameters
                .Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

            value = null;
            return declared;
        }

        /// <summary>
        /// Pulls employer ids out of whatever the parameter bound to: a number, a
        /// string, a comma-separated string, or a collection of either. Ids of zero
        /// or less are dropped as "not supplied".
        /// </summary>
        private static IEnumerable<long> ExtractIds(object? raw)
        {
            switch (raw)
            {
                case null:
                    yield break;

                case string s:
                    foreach (var id in ParseCsv(s)) yield return id;
                    yield break;

                case IEnumerable enumerable and not string:
                    foreach (var item in enumerable)
                        foreach (var id in ExtractIds(item))
                            yield return id;
                    yield break;

                default:
                    if (raw is IConvertible)
                    {
                        long parsed;
                        try
                        {
                            parsed = Convert.ToInt64(raw, CultureInfo.InvariantCulture);
                        }
                        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
                        {
                            yield break;
                        }

                        if (parsed > 0) yield return parsed;
                    }
                    yield break;
            }
        }

        private static IEnumerable<long> ParseCsv(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) yield break;

            foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (long.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0)
                    yield return id;
            }
        }

        /// <summary>
        /// 403 either way, shaped to the caller. Most of these actions are reached
        /// by AJAX and expect JSON; a ForbidResult would hand those a redirect.
        /// Mirrors how ImpersonationReadOnlyFilter distinguishes the two.
        /// </summary>
        private static IActionResult Deny(HttpContext http, string message)
        {
            bool wantsJson =
                http.Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                (http.Request.Headers["Accept"].ToString()?.Contains("application/json") ?? false);

            return wantsJson
                ? new JsonResult(new { success = false, message })
                  { StatusCode = StatusCodes.Status403Forbidden }
                : new ForbidResult();
        }
    }
}
