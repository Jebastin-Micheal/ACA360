using System.Data;
using System.Reflection;
using System.Text.RegularExpressions;
using ACA360.Core.Attributes;
using ACA360.Tests.Infrastructure;
using ACA360.Tests.Integration.Infrastructure;
using Dapper;

namespace ACA360.Tests.Integration;

/// <summary>The local database matches what the code expects of it.</summary>
[Collection(AppCollection.Name)]
public class DatabaseTests
{
    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ACA360.sln"))) dir = dir.Parent;
        return Path.Combine(new[] { dir?.FullName ?? throw new InvalidOperationException("ACA360.sln not found") }.Concat(parts).ToArray());
    }

    [LocalDbFact]
    public void EveryFixScriptIsApplied()
    {
        // The read-only deploy check (Database Script/deploy) reports script by script.
        var script = File.ReadAllText(RepoFile("Database Script", "deploy", "deploy_check.sql"));
        using var db = LocalDb.Open();
        using var results = db.QueryMultiple(script);
        var rows = results.Read<(string Database, string Script, string Applied, string Detail)>().ToList();
        var summary = results.Read<string>().Single();

        Assert.NotEmpty(rows);
        var missing = rows.Where(r => r.Applied == "No").Select(r => $"{r.Script}: {r.Detail}").ToList();
        Assert.True(missing.Count == 0, "Scripts not applied:\n" + string.Join("\n", missing));
        Assert.StartsWith("All of", summary);
    }

    [LocalDbFact]
    public void Script041IsApplied() =>
        Assert.True(LocalDb.Scalar<bool>("SELECT CASE WHEN OBJECT_ID('dbo.fn_FileVisibleToUser') IS NOT NULL THEN 1 ELSE 0 END"),
            "dbo.fn_FileVisibleToUser is missing: run Database Script/fixes/041_file_visible_to_user.sql");

    [LocalDbFact]
    public void Script042IsApplied() =>
        Assert.True(LocalDb.Scalar<bool>("SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.sql_modules WHERE object_id = OBJECT_ID('dbo.sp_UserAccountsList') AND definition LIKE '%LoginEnabled%') THEN 1 ELSE 0 END"),
            "sp_UserAccountsList has no LoginEnabled: run Database Script/fixes/042_user_accounts_list_login_state.sql");

    [LocalDbFact]
    public void Script040LeftNoActiveLoginWithoutAnAccount()
    {
        var orphans = LocalDb.Scalar<int>(@"
            SELECT COUNT(*) FROM dbo.tbl_User u
            WHERE ISNULL(u.Flag, 1) <> 0
              AND ((u.Role_ID = 10 AND NOT EXISTS (SELECT 1 FROM dbo.BrokerAccounts b WHERE b.Broker_ID = u.Ref_ID))
                OR (u.Role_ID = 13 AND NOT EXISTS (SELECT 1 FROM dbo.Employer e WHERE e.id = u.Ref_ID)))");
        Assert.Equal(0, orphans);
    }

    [LocalDbFact]
    public void TheLoginCheckFollowsFlagAndAccountStatus()
    {
        var user = TestUsers.CreateStaff(RoleIds.Admin, "fn-login");
        bool Allowed() => LocalDb.Scalar<bool>("SELECT IsAllowed FROM dbo.fn_UserLoginAllowed(@id)", new { id = user.UserId });
        Assert.True(Allowed());
        LocalDb.Execute("UPDATE dbo.StaffAccounts SET Staff_Status = 4 WHERE Staff_ID = @id", new { id = user.RefId });
        Assert.False(Allowed());
        LocalDb.Execute("UPDATE dbo.StaffAccounts SET Staff_Status = 1 WHERE Staff_ID = @id; UPDATE dbo.tbl_User SET Flag = 0 WHERE User_ID = @u",
            new { id = user.RefId, u = user.UserId });
        Assert.False(Allowed());
    }

    private static List<int> TrackerList(string? employerIds)
    {
        using var db = LocalDb.Open();
        var args = new DynamicParameters(new { PageSize = 500 });
        if (employerIds != null) args.Add("EmployerIDs", employerIds);
        return db.Query("dbo.sp_Tracker_GetEmployerList", args, commandType: CommandType.StoredProcedure)
                 .Select(r => (int)((IDictionary<string, object>)r)["EmployerId"]).ToList();
    }

    [LocalDbFact]
    public void TheTrackerListProcedureFiltersByEmployerIds()
    {
        var all = TrackerList(null);
        Assert.True(all.Count > 1, "the local database needs more than one employer");
        Assert.Equal(new[] { all[0] }, TrackerList(all[0].ToString()).Where(id => id == all[0]).Distinct());
        Assert.Empty(TrackerList("-1"));
        Assert.Equal(new[] { all[0], all[1] }.OrderBy(i => i), TrackerList($"{all[0]},{all[1]}").Where(id => id == all[0] || id == all[1]).Distinct().OrderBy(i => i));
    }

    // Permission checks in code that no role can be granted, because tbl_Menus has no active
    // menu for the controller or tbl_ActionTypes has no such action. Such an action is refused
    // to everyone. May only shrink.
    private static readonly Dictionary<string, string> KnownUngrantable = new(StringComparer.OrdinalIgnoreCase)
    {
    };

    [LocalDbFact]
    public void EveryPermissionTheCodeChecksCanBeGranted()
    {
        using var db = LocalDb.Open();
        var grantable = db.Query<(string Controller, string Action)>(@"
                SELECT LTRIM(RTRIM(m.Controller)), at.ActionName
                FROM dbo.tbl_Menus m CROSS JOIN dbo.tbl_ActionTypes at
                WHERE m.IsActive = 1 AND m.Flag = 0 AND m.Controller IS NOT NULL")
            .Select(x => $"{x.Controller}:{x.Action}").ToHashSet(StringComparer.OrdinalIgnoreCase);

        var checkedInCode = ControllerCatalog.Actions
            .SelectMany(a => a.Method.GetCustomAttributes<AuthorizePermissionAttribute>()
                .Concat(a.Controller.GetCustomAttributes<AuthorizePermissionAttribute>())
                .Select(p => $"{a.ControllerName}:{ACA360.Tests.Unit.Conventions.ControllerSecurityConventionTests.PermissionName(p)}"))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p).ToList();

        var ungrantable = checkedInCode.Where(p => !grantable.Contains(p)).ToList();
        var added = ungrantable.Where(p => !KnownUngrantable.ContainsKey(p)).ToList();
        var fixedNow = KnownUngrantable.Keys.Where(k => !ungrantable.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList();
        Assert.True(added.Count == 0 && fixedNow.Count == 0,
            (added.Count > 0 ? "Permissions the code checks but no role can be granted:\n  " + string.Join("\n  ", added) + "\n" : "")
            + (fixedNow.Count > 0 ? "Listed as ungrantable but now grantable (remove from the list):\n  " + string.Join("\n  ", fixedNow) : ""));
    }

    // Active menu rows whose Controller is not a page, and why.
    private static readonly Dictionary<string, string> MenuNamesThatAreNotControllers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Administration"] = "a menu group heading",
        ["Tracker"] = "a permission group: TrackerEmployerController checks Tracker:<action>",
    };

    [LocalDbFact]
    public void EveryActiveMenuPointsAtAController()
    {
        using var db = LocalDb.Open();
        var menuControllers = db.Query<string>(
                "SELECT DISTINCT LTRIM(RTRIM(Controller)) FROM dbo.tbl_Menus WHERE IsActive = 1 AND Flag = 0 AND ISNULL(Controller, '') <> ''")
            .ToList();
        var codeControllers = ControllerCatalog.Controllers.Select(c => c.Name[..^"Controller".Length])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = menuControllers.Where(m => !codeControllers.Contains(m) && !MenuNamesThatAreNotControllers.ContainsKey(m) && !Regex.IsMatch(m, "^#|^javascript", RegexOptions.IgnoreCase)).ToList();
        Assert.True(missing.Count == 0, "Active menus whose controller does not exist:\n" + string.Join("\n", missing));
    }
}
