using System.Data;
using System.Net;
using System.Text.RegularExpressions;
using ACA360.Tests.Integration.Infrastructure;
using Dapper;
using Microsoft.Data.SqlClient;

namespace ACA360.Tests.Integration;

/// <summary>
/// Employer logins are made only from Tracker > Employer > Portal login (044): they point at
/// their employer (tbl_User.Ref_ID = Employer.id), User Accounts cannot create one, and the
/// User Accounts list shows them read-only, named after the employer.
/// </summary>
[Collection(AppCollection.Name)]
public class EmployerLoginTests
{
    private readonly AcaWebFactory _app;

    public EmployerLoginTests(AcaWebFactory app) => _app = app;

    private static long AnyEmployerId() =>
        LocalDb.Scalar<long>("SELECT TOP 1 id FROM dbo.Employer WHERE ISNULL(IsDeleted, 0) = 0 ORDER BY id");

    private static long CreateEmployerLogin(long employerId, string label) =>
        LocalDb.Scalar<long>(
            @"INSERT INTO dbo.tbl_User (User_Name, Role_ID, Flag, Ref_ID, isMFA, CreatedOn)
              OUTPUT inserted.User_ID VALUES (@name, 13, 1, @employerId, 0, GETDATE());",
            new { name = $"{TestUsers.Prefix}{label}.{Guid.NewGuid().ToString("N")[..12]}@example.com", employerId });

    [LocalDbFact]
    public void TheDatabaseRefusesToCreateAnEmployerLoginFromUserAccounts()
    {
        using var db = LocalDb.Open();
        using var tx = db.BeginTransaction();
        var ex = Assert.Throws<SqlException>(() => db.Execute("dbo.sp_CreateUserAccounts", new
        {
            UserAccounts_FName = "Test", UserAccounts_LName = "Employer", UserAccounts_Addr1 = "", UserAccounts_Addr2 = "",
            UserAccounts_City = "", UserAccounts_State = (long?)null, UserAccounts_Zip = "", UserAccounts_Phone = "",
            UserAccounts_Email = TestUsers.Prefix + "refused@example.com", UserAccounts_Status = 1, UserAccounts_Role = RoleIds.Employer,
            UserAccounts_Password = "x", ProfilePicture = "", IsMFA = 0, createdby = 1
        }, tx, commandType: CommandType.StoredProcedure));
        Assert.Equal(50044, ex.Number);
    }

    [LocalDbFact]
    public void AnEmployerLoginIsListedUnderItsEmployer()
    {
        var employerId = AnyEmployerId();
        var userId = CreateEmployerLogin(employerId, "portal");
        var email = LocalDb.Scalar<string>("SELECT User_Name FROM dbo.tbl_User WHERE User_ID = @userId", new { userId });
        var employerName = LocalDb.Scalar<string>("SELECT Name FROM dbo.Employer WHERE id = @employerId", new { employerId });

        using var db = LocalDb.Open();
        var row = Assert.Single(db.Query("dbo.sp_UserAccountsList", new { PageIndex = 1, PageSize = 500, Search = email, RoleId = RoleIds.Employer },
            commandType: CommandType.StoredProcedure).Cast<IDictionary<string, object>>());
        Assert.Equal("EmployerPortal", row["UserType"]);
        Assert.Equal(employerId, Convert.ToInt64(row["UserAccounts_ID"]));
        Assert.Equal(employerName, row["UserAccounts_Full_Name"]);
        Assert.True((bool)row["LoginEnabled"]);

        LocalDb.Execute("UPDATE dbo.tbl_User SET Flag = 0 WHERE User_ID = @userId", new { userId });
        row = Assert.Single(db.Query("dbo.sp_UserAccountsList", new { PageIndex = 1, PageSize = 500, Search = email, RoleId = RoleIds.Employer },
            commandType: CommandType.StoredProcedure).Cast<IDictionary<string, object>>());
        Assert.False((bool)row["LoginEnabled"]);
    }

    [LocalDbFact]
    public async Task TheUserAccountsPageSendsEmployerLoginsToTracker()
    {
        var employerId = AnyEmployerId();
        var userId = CreateEmployerLogin(employerId, "page-portal");
        var email = LocalDb.Scalar<string>("SELECT User_Name FROM dbo.tbl_User WHERE User_ID = @userId", new { userId });
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.SuperAdmin, "portal-admin"));

        var html = await (await client.AjaxAsync(
            "/UserAccounts/UserAccountsListPartial?pageIndex=1&pageSize=10&roleId=13&search=" + Uri.EscapeDataString(email))).Content.ReadAsStringAsync();
        Assert.Contains($"/TrackerEmployer/Dashboard/{employerId}", html);
        Assert.Contains("Open in Tracker", html);
        Assert.DoesNotContain("edit-UserAccounts-btn", html);
        Assert.DoesNotContain("delete-UserAccounts-btn", html);
    }

    [LocalDbFact]
    public async Task TheAddFormDoesNotOfferTheEmployerRole()
    {
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.SuperAdmin, "portal-form"));
        var html = await (await client.AjaxAsync("/UserAccounts/Add_New")).Content.ReadAsStringAsync();
        var roleSelect = Regex.Match(html, "<select[^>]*name=\"UserAccounts_Role\"[\\s\\S]*?</select>").Value;
        Assert.NotEmpty(roleSelect);
        Assert.DoesNotContain("value=\"13\"", roleSelect);
    }

    [LocalDbFact]
    public async Task UserAccountsRefusesToCreateAnEmployerLogin()
    {
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.SuperAdmin, "portal-create"));
        var email = TestUsers.Prefix + "portal-create-refused@example.com";
        var response = await client.PostFormAsync("/UserAccounts/Create", "/UserAccounts/Add_New", new Dictionary<string, string>
        {
            ["UserAccounts_FName"] = "Test",
            ["UserAccounts_LName"] = "Employer",
            ["UserAccounts_Email"] = email,
            ["UserAccounts_Password"] = "T1!" + Guid.NewGuid().ToString("N"),
            ["UserAccounts_Status"] = "1",
            ["UserAccounts_Role"] = "13",
            ["IsMFA"] = "0",
        }, ajax: true);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Tracker", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, LocalDb.Scalar<int>("SELECT COUNT(*) FROM dbo.tbl_User WHERE User_Name = @email", new { email }));
    }
}
