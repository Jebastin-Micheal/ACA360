using System.Data;
using System.Net;
using System.Text.RegularExpressions;
using ACA360.Core.Constants;
using ACA360.Tests.Integration.Infrastructure;
using Dapper;
using OfficeOpenXml;

namespace ACA360.Tests.Integration;

/// <summary>
/// Account Managers and Data Analysts see only the employers assigned to them (portfolio /
/// operational assignments); the all-employer roles see every employer.
/// </summary>
[Collection(AppCollection.Name)]
public class EmployerScopeTests
{
    private readonly AcaWebFactory _app;

    public EmployerScopeTests(AcaWebFactory app) => _app = app;

    /// <summary>Two different live main employers (no parent), and one employee of each.</summary>
    private static (int A, int B, int EmployeeOfB) Employers()
    {
        using var db = LocalDb.Open();
        var ids = db.Query<int>(
            @"SELECT TOP 2 e.id FROM dbo.Employer e
              WHERE ISNULL(e.IsDeleted, 0) = 0 AND ISNULL(e.companyId, 0) = 0
                AND EXISTS (SELECT 1 FROM dbo.Employee x WHERE x.EmployerId = e.id AND ISNULL(x.IsDeleted, 0) = 0)
              ORDER BY e.id").ToList();
        if (ids.Count < 2) throw new InvalidOperationException("The local database needs two employers with employees for these tests.");
        var employee = LocalDb.Scalar<int>(
            "SELECT TOP 1 id FROM dbo.Employee WHERE EmployerId = @b AND ISNULL(IsDeleted, 0) = 0 ORDER BY id", new { b = ids[1] });
        return (ids[0], ids[1], employee);
    }

    private static List<int> ListedEmployers(string html) =>
        Regex.Matches(html, "data-emp-id=\"(\\d+)\"").Select(m => int.Parse(m.Groups[1].Value)).Distinct().ToList();

    private static int FamilyOf(int employerId) => LocalDb.Scalar<int>(
        "SELECT CASE WHEN ISNULL(companyId, 0) > 0 THEN companyId ELSE id END FROM dbo.Employer WHERE id = @id", new { id = employerId });

    private static async Task<string> TrackerList(AcaClient client)
    {
        var response = await client.AjaxAsync("/TrackerEmployer/GetList?Page=1&PageSize=100");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    [LocalDbFact]
    public async Task AnAccountManagerWithNoEmployersSeesNone()
    {
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.AccountManager, "am-none"));
        Assert.Empty(ListedEmployers(await TrackerList(client)));
        Assert.Equal(0, (await client.JsonAsync("/TrackerEmployer/GetMainEmployers")).GetArrayLength());
    }

    [LocalDbTheory]
    [InlineData(RoleIds.AccountManager)]
    [InlineData(RoleIds.DataAnalyst)]
    public async Task AnAssignedUserSeesOnlyTheirEmployer(int roleId)
    {
        var (a, b, _) = Employers();
        var user = TestUsers.CreateStaff(roleId, "scoped");
        TestUsers.Assign(user, a);
        using var client = await _app.SignedInAs(user);

        var listed = ListedEmployers(await TrackerList(client));
        Assert.Contains(a, listed);
        Assert.DoesNotContain(b, listed);
        Assert.All(listed, id => Assert.Equal(a, FamilyOf(id)));

        var mains = await client.JsonAsync("/TrackerEmployer/GetMainEmployers");
        Assert.Equal(new[] { a }, mains.EnumerateArray().Select(m => m.GetProperty("value").GetInt32()));
    }

    [LocalDbFact]
    public async Task AnotherEmployersPagesAndDataAreRefused()
    {
        var (a, b, employeeOfB) = Employers();
        var user = TestUsers.CreateStaff(RoleIds.AccountManager, "am-other");
        TestUsers.Assign(user, a);
        using var client = await _app.SignedInAs(user);

        Assert.Equal(0, (await client.JsonAsync($"/TrackerEmployer/GetAffiliatesByMain?mainEmployerId={b}")).GetArrayLength());
        var dashboard = await client.PageAsync($"/TrackerEmployer/Dashboard?id={b}");
        Assert.True(dashboard.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound
                    || (dashboard.StatusCode == HttpStatusCode.Redirect && dashboard.Headers.Location!.OriginalString.Contains("AccessDenied")),
            $"Tracker dashboard of another employer answered {(int)dashboard.StatusCode} {dashboard.Headers.Location}");

        var year = LocalDb.Scalar<int>("SELECT ISNULL(MAX(filingYear), YEAR(GETDATE()) - 1) FROM dbo.FilingYear");
        var employee = await client.AjaxAsync($"/Employee/Details?id={employeeOfB}&year={year}");
        Assert.True(employee.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound
                    || (employee.StatusCode == HttpStatusCode.Redirect && employee.Headers.Location!.OriginalString.Contains("AccessDenied")),
            $"Employee of another employer answered {(int)employee.StatusCode} {employee.Headers.Location}");
    }

    [LocalDbFact]
    public async Task TheMasterExportHasOnlyTheirEmployers()
    {
        ExcelPackage.LicenseContext = LicenseContext.Commercial;
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.AccountManager, "am-export"));
        var response = await client.GetAsync("/TrackerEmployer/ExportMasterExcel");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var package = new ExcelPackage(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        var sheet = package.Workbook.Worksheets[0];
        Assert.True(sheet.Dimension == null || sheet.Dimension.End.Row <= 1, "an Account Manager with no employers got employer rows");
    }

    [LocalDbTheory]
    [InlineData(RoleIds.SuperAdmin)]
    [InlineData(RoleIds.DaSupervisor)]
    [InlineData(RoleIds.AmSupervisor)]
    public async Task AllEmployerRolesSeeEveryEmployer(int roleId)
    {
        var (a, b, _) = Employers();
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(roleId, "all"));
        var mains = (await client.JsonAsync("/TrackerEmployer/GetMainEmployers")).EnumerateArray()
            .Select(m => m.GetProperty("value").GetInt32()).ToList();
        Assert.Contains(a, mains);
        Assert.Contains(b, mains);
    }

    /// <summary>
    /// The Select Employer list (sp_EmployersOnlyList) and a user's employers at sign-in and in
    /// View As (sp_GetAllEmployers) follow the same all-employer roles as the rest of the app:
    /// the supervisors and StaffAC used to get only their assignments there (script 051).
    /// </summary>
    [LocalDbTheory]
    [InlineData(RoleIds.DaSupervisor, UserRoles.DASupervisor, true)]
    [InlineData(RoleIds.AmSupervisor, UserRoles.AMSupervisor, true)]
    [InlineData(RoleIds.StaffAC, UserRoles.StaffAC, true)]
    [InlineData(RoleIds.StaffAU, UserRoles.StaffAU, true)]
    [InlineData(RoleIds.AccountManager, UserRoles.AccountManager, false)]
    [InlineData(RoleIds.DataAnalyst, UserRoles.DataAnalyst, false)]
    public void TheEmployerListsFollowTheAllEmployerRoles(int roleId, string roleName, bool seesAll)
    {
        Assert.Equal(seesAll, UserRoles.AllEmployerRoles.Contains(roleName));
        var user = TestUsers.CreateStaff(roleId, "lists");   // no assignments
        using var db = LocalDb.Open();
        var year = db.ExecuteScalar<int>("SELECT TOP 1 filingYear FROM dbo.Employer GROUP BY filingYear ORDER BY COUNT(*) DESC");

        var inYear = db.ExecuteScalar<int>("SELECT COUNT(*) FROM dbo.Employer WHERE filingYear = @year AND name IS NOT NULL", new { year });
        int listed;
        using (var list = db.QueryMultiple("dbo.sp_EmployersOnlyList",
            new { search = "", PageSize = 100_000, PageIndex = 1, fillingyear = year, SortColumn = 0, SortOrder = "asc", UserId = (int)user.UserId, RoleName = roleName },
            commandType: CommandType.StoredProcedure))
            listed = list.Read<int?>().Count(id => id != null);
        Assert.Equal(seesAll ? inYear : 0, listed);

        var atSignIn = db.Query("dbo.sp_GetAllEmployers", new { UserID = (int)user.UserId }, commandType: CommandType.StoredProcedure).Count();
        Assert.Equal(seesAll ? db.ExecuteScalar<int>("SELECT COUNT(*) FROM dbo.Employer") : 0, atSignIn);
    }
}
