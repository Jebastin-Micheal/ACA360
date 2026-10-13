using System.Reflection;
using ACA360.Core.Attributes;
using ACA360.Tests.Infrastructure;
using ACA360.Web.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ACA360.Tests.Unit.Conventions;

/// <summary>
/// Security rules every controller action must follow. They run over every action in
/// ACA360.Web (see ControllerCatalog), so a new controller is checked the day it is written.
///
/// Where today's code already breaks a rule, the offending actions are listed in a "known"
/// set below. Those lists may only shrink: a new action that breaks the rule fails the test,
/// and so does a listed action that has since been fixed (remove it from the list).
/// </summary>
public class ControllerSecurityConventionTests
{
    [Fact]
    public void TheCatalogFindsTheControllers()
    {
        Assert.True(ControllerCatalog.Controllers.Count >= 50, $"only {ControllerCatalog.Controllers.Count} controllers found");
        Assert.Contains(ControllerCatalog.Actions, a => a.Key == "TrackerEmployer/Index");
        Assert.Contains(ControllerCatalog.Actions, a => a.Key == "Portal/TaxForms/Index");
    }

    // Pages anyone may open without signing in. Everything else needs a signed-in user
    // (Program.cs sets a fallback policy), so this list is the whole public surface.
    private static readonly Dictionary<string, string> PublicActions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Account/Login"] = "sign-in",
        ["Account/ForgotPassword"] = "sign-in help",
        ["Account/DuoCallback"] = "Duo sends the user back here before they are signed in; it checks the sign-in it started",
        ["Account/SetNewPassword"] = "first sign-in with a temporary password",
        ["Account/AccessDenied"] = "error page",
        ["Home/Error"] = "error page",
        ["System/Maintenance"] = "shown to everyone while maintenance mode is on",
        ["Portal/Account/Login"] = "Employee Portal sign-in",
        ["Portal/Account/ForgotPassword"] = "Employee Portal sign-in help",
        ["Portal/Account/SetPassword"] = "Employee Portal activation link",
        ["Portal/Account/Logout"] = "Employee Portal sign-out",
        ["MyForms/Access"] = "emailed form link; checks its own token",
        ["MyForms/Verify"] = "token + last 4 of the SSN, then a portal session",
        ["MyForms/Dashboard"] = "needs the session Verify sets",
        ["MyForms/DownloadForm"] = "needs the session Verify sets",
        ["MyForms/Logout"] = "clears that session",
    };

    [Fact]
    public void OnlyTheKnownActionsAreOpenWithoutSigningIn()
    {
        var open = ControllerCatalog.Actions
            .Where(a => a.Method.GetCustomAttribute<AllowAnonymousAttribute>() != null
                        || (a.Controller.GetCustomAttribute<AllowAnonymousAttribute>() != null
                            && a.Method.GetCustomAttribute<AuthorizeAttribute>() == null))
            .Select(a => a.Key).Distinct().OrderBy(k => k).ToList();

        AssertSameSet(PublicActions, open, "open without signing in");
    }

    // State-changing actions that do not check an antiforgery token today.
    private static readonly Dictionary<string, string> KnownWithoutAntiforgery = new(StringComparer.OrdinalIgnoreCase)
    {
        ["System/GetErrorLogsDataTables"] = "read-only: DataTables posts its paging request",
    };

    [Fact]
    public void EveryStateChangingActionValidatesTheAntiforgeryToken()
    {
        var missing = ControllerCatalog.Actions
            .Where(a => a.ChangesState && !a.Has<ValidateAntiForgeryTokenAttribute>() && !a.Has<AutoValidateAntiforgeryTokenAttribute>())
            .Select(a => a.Key).Distinct().OrderBy(k => k).ToList();

        AssertSameSet(KnownWithoutAntiforgery, missing, "change data without an antiforgery check");
    }

    // GET-reachable actions whose name says they change data. A GET can be triggered by a link,
    // an <img> tag or a prefetch, so these should be POST-only.
    private static readonly Dictionary<string, string> KnownStateChangingGets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Assignment/AssignmentHistory"] = "read-only: a history list (name starts with \"Assign\")",
        ["PortalAdmin/Activate"] = "GET only shows the confirm page; the POST of the same name activates",
    };

    private static readonly string[] ChangingVerbs =
        { "Delete", "Remove", "Save", "Update", "Create", "Insert", "Activate", "Deactivate", "Reset", "Approve",
          "Reject", "Assign", "Unassign", "Toggle", "Clear", "Purge", "Revoke", "Import", "Send", "Submit" };

    [Fact]
    public void ActionsThatChangeDataAreNotReachableByGet()
    {
        var gets = ControllerCatalog.Actions
            .Where(a => a.AcceptsGet && ChangingVerbs.Any(v => a.ActionName.StartsWith(v, StringComparison.OrdinalIgnoreCase)))
            .Select(a => a.Key).Distinct().OrderBy(k => k).ToList();

        AssertSameSet(KnownStateChangingGets, gets, "change data but answer GET requests");
    }

    [Fact]
    public void RequireEmployerScopeNamesParametersTheActionDeclares()
    {
        // A misnamed parameter makes the filter refuse every call (it fails closed), so the
        // action would be unusable rather than unguarded. Either way it is a wiring mistake.
        var broken = new List<string>();
        foreach (var action in ControllerCatalog.Actions)
        {
            var attribute = action.Method.GetCustomAttribute<RequireEmployerScopeAttribute>()
                            ?? action.Controller.GetCustomAttribute<RequireEmployerScopeAttribute>();
            if (attribute == null) continue;

            var names = (string[])typeof(RequireEmployerScopeAttribute)
                .GetField("_parameterNames", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(attribute)!;
            var declared = action.Method.GetParameters().Select(p => p.Name!).ToList();
            broken.AddRange(names.Where(n => !declared.Contains(n, StringComparer.OrdinalIgnoreCase))
                                 .Select(n => $"{action.Key} ({n})"));
        }

        Assert.True(broken.Count == 0, "RequireEmployerScope names a parameter the action does not have:\n" + string.Join("\n", broken));
    }

    [Fact]
    public void PermissionChecksUseKnownActionTypes()
    {
        // tbl_ActionTypes holds the action types a role can be granted; a misspelt one can never be granted.
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "View", "Add", "Edit", "Delete", "Download", "Upload", "Index" };
        var unknown = ControllerCatalog.Actions
            .SelectMany(a => a.Method.GetCustomAttributes<AuthorizePermissionAttribute>()
                .Select(p => (a.Key, Permission: PermissionName(p))))
            .Where(x => !known.Contains(x.Permission))
            .Select(x => $"{x.Key} -> {x.Permission}").Distinct().ToList();

        Assert.True(unknown.Count == 0, "AuthorizePermission with an action type that is not in the list:\n" + string.Join("\n", unknown));
    }

    [Fact]
    public void AreaControllersAreOnlyThePortal()
    {
        var areas = ControllerCatalog.Controllers
            .Select(c => c.GetCustomAttribute<AreaAttribute>()?.RouteValue)
            .Where(a => a != null).Distinct().ToList();
        Assert.Equal(new[] { "Portal" }, areas);
    }

    internal static string PermissionName(AuthorizePermissionAttribute attribute) =>
        (string)typeof(AuthorizePermissionAttribute)
            .GetField("_actionName", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(attribute)!;

    private static void AssertSameSet(Dictionary<string, string> expected, List<string> actual, string what)
    {
        var added = actual.Where(a => !expected.ContainsKey(a)).ToList();
        var fixedNow = expected.Keys.Where(e => !actual.Contains(e, StringComparer.OrdinalIgnoreCase)).OrderBy(e => e).ToList();
        Assert.True(added.Count == 0 && fixedNow.Count == 0,
            (added.Count > 0 ? $"New actions that {what}:\n  " + string.Join("\n  ", added) + "\n" : "")
            + (fixedNow.Count > 0 ? $"Listed as known but no longer {what} (remove from the list):\n  " + string.Join("\n  ", fixedNow) : ""));
    }
}
