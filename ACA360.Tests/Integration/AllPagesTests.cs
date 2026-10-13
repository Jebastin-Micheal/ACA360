using System.Net;
using System.Reflection;
using ACA360.Tests.Infrastructure;
using ACA360.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Authorization;

namespace ACA360.Tests.Integration;

/// <summary>
/// Every page in the app, one test per action: closed to anonymous visitors, and open for a
/// SuperAdmin without a server error. Pages are found by reflection, so a new page is tested
/// the day it is added.
/// </summary>
[Collection(AppCollection.Name)]
public class AllPagesTests
{
    private readonly AcaWebFactory _app;

    public AllPagesTests(AcaWebFactory app) => _app = app;

    private static bool IsPublic(ActionInfo a) =>
        a.Method.GetCustomAttribute<AllowAnonymousAttribute>() != null
        || (a.Controller.GetCustomAttribute<AllowAnonymousAttribute>() != null && a.Method.GetCustomAttribute<AuthorizeAttribute>() == null);

    public static IEnumerable<object[]> ClosedGetActions() => ControllerCatalog.Actions
        .Where(a => a.AcceptsGet && !IsPublic(a))
        .Select(a => a.Key).Distinct().OrderBy(k => k).Select(k => new object[] { k });

    // Pages a SuperAdmin is not sent to by this test, and why.
    private static readonly Dictionary<string, string> NotOpened = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Account/Logout"] = "would end the shared session",
        ["Employer/Employer1094Tab"] = "needs an employer id (a tab inside an employer page)",
        ["Employer/EmployerOtherDetailsPartial"] = "needs an employer id (a tab inside an employer page)",
        ["ErrorTriage/GetStagingRow"] = "needs a staging row id",
    };

    // Names of actions that do work rather than show a page; even as GET they are not opened here.
    private static readonly string[] WorkVerbs =
        { "Delete", "Remove", "Save", "Update", "Create", "Insert", "Activate", "Deactivate", "Reset", "Approve", "Reject",
          "Assign", "Unassign", "Toggle", "Clear", "Purge", "Revoke", "Import", "Send", "Submit", "Generate", "Run",
          "Process", "Recalculate", "Recover", "Seed", "Begin", "End", "Start", "Stop", "Retry", "Restore", "Sync",
          "Upload", "Merge", "Apply", "Lock", "Unlock", "Mark", "Set", "Switch", "Claim", "Release", "Rebuild", "Fix",
          "Auto", "Bulk", "Enqueue", "Trigger", "Queue", "Cancel", "Close", "Reopen", "Archive", "Publish", "Change",
          "Copy", "Clone", "Move", "Export", "Download", "Print", "Email", "Notify", "Test", "Validate", "Verify" };

    public static IEnumerable<object[]> PagesForSuperAdmin() => ControllerCatalog.Actions
        .Where(a => a.AcceptsGet && !IsPublic(a) && a.NeedsNoInput && a.Area == null
                    && !NotOpened.ContainsKey(a.Key)
                    && !WorkVerbs.Any(v => a.ActionName.StartsWith(v, StringComparison.OrdinalIgnoreCase)))
        .Select(a => a.Key).Distinct().OrderBy(k => k).Select(k => new object[] { k });

    private static ActionInfo Find(string key) => ControllerCatalog.Actions.First(a => a.Key == key);

    [LocalDbTheory]
    [MemberData(nameof(ClosedGetActions))]
    public async Task AnonymousVisitorsAreSentToSignIn(string action)
    {
        using var client = _app.NewClient();
        var response = await client.PageAsync(Find(action).Url);
        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized
            || response.StatusCode == HttpStatusCode.NotFound          // route needs a value the test does not give
            || (response.StatusCode == HttpStatusCode.Redirect && (AcaClient.IsSignInPage(response.Headers.Location)
                || response.Headers.Location!.OriginalString.Contains("/Portal/Account/Login", StringComparison.OrdinalIgnoreCase))),
            $"{Find(action).Url} answered {(int)response.StatusCode} {response.Headers.Location}");
    }

    private static TestUser? _superAdmin;
    private static AcaClient? _superAdminClient;

    private async Task<AcaClient> SuperAdmin()
    {
        if (_superAdminClient == null)
        {
            _superAdmin = TestUsers.CreateStaff(RoleIds.SuperAdmin, "pages");
            _superAdminClient = await _app.SignedInAs(_superAdmin);
        }
        return _superAdminClient;
    }

    // What the grids and year pickers send with every request (unknown names are ignored by MVC).
    private static string CommonQuery()
    {
        var year = LocalDb.Scalar<int>("SELECT ISNULL(MAX(FilingYear), YEAR(GETDATE()) - 1) FROM dbo.FilingYear");
        return $"?page=1&pageIndex=1&pageNumber=1&pageSize=10&sortColumn=0&sortOrder=asc&length=10&start=0&draw=1&year={year}&planYear={year}&filingYear={year}";
    }

    [LocalDbTheory]
    [MemberData(nameof(PagesForSuperAdmin))]
    public async Task ASuperAdminCanOpenThePageWithoutAServerError(string action)
    {
        var client = await SuperAdmin();
        var response = await client.PageAsync(Find(action).Url + CommonQuery());
        var body = (int)response.StatusCode >= 500 ? await response.Content.ReadAsStringAsync() : "";
        Assert.True((int)response.StatusCode < 500,
            $"{Find(action).Url} answered {(int)response.StatusCode}: {body[..Math.Min(400, body.Length)]}");
        Assert.False(AcaClient.IsSignInPage(response.Headers.Location), $"{Find(action).Url} sent a signed-in SuperAdmin to sign in");
    }

    [LocalDbTheory]
    [InlineData("/Distribution/DistributionListPartial")]
    [InlineData("/Filing/FilingListPartial")]
    [InlineData("/Menu/MenuListPartial")]
    [InlineData("/Plan/PlanListPartial")]
    [InlineData("/Role/RoleListPartial")]
    [InlineData("/Rules/ValidationRuleListPartial")]
    [InlineData("/System/FilingYearListPartial")]
    [InlineData("/UserAccounts/UserAccountsListPartial")]
    public async Task ListsWorkWithoutPagingValues(string url)
    {
        var client = await SuperAdmin();
        var response = await client.AjaxAsync(url);
        var body = (int)response.StatusCode >= 500 ? await response.Content.ReadAsStringAsync() : "";
        Assert.True((int)response.StatusCode < 500, $"{url} answered {(int)response.StatusCode}: {body[..Math.Min(600, body.Length)]}");
    }
}
