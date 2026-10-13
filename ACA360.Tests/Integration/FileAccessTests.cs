using System.Data;
using System.Net;
using ACA360.Tests.Integration.Infrastructure;
using Dapper;

namespace ACA360.Tests.Integration;

/// <summary>
/// A file can be opened exactly when the File Upload list shows it: dbo.fn_FileVisibleToUser
/// (script 041, used by every per-file action) agrees with sp_SearchFileLogsPaginated.
/// </summary>
[Collection(AppCollection.Name)]
public class FileAccessTests
{
    private readonly AcaWebFactory _app;

    public FileAccessTests(AcaWebFactory app) => _app = app;

    private static readonly string[] ViewModes = { "Active", "Archived", "RecycleBin" };

    /// <summary>Every file the list shows this user, in any view.</summary>
    private static HashSet<int> Listed(long userId)
    {
        using var db = LocalDb.Open();
        var ids = new HashSet<int>();
        foreach (var mode in ViewModes)
        {
            using var results = db.QueryMultiple("dbo.sp_SearchFileLogsPaginated",
                new { PageSize = 100000, PageNumber = 1, UserId = (int)userId, FilterType = "All", ViewMode = mode },
                commandType: CommandType.StoredProcedure);
            ids.UnionWith(results.Read().Select(r => (int)((IDictionary<string, object>)r)["FileLogId"]));
        }
        return ids;
    }

    private static HashSet<int> Visible(IEnumerable<int> fileIds, long userId)
    {
        using var db = LocalDb.Open();
        return db.Query<int>(
            @"SELECT f.FileLogId FROM (SELECT CAST(value AS INT) AS FileLogId FROM STRING_SPLIT(@ids, ',')) f
              CROSS APPLY dbo.fn_FileVisibleToUser(f.FileLogId, @userId) v WHERE v.IsVisible = 1",
            new { ids = string.Join(",", fileIds), userId = (int)userId }).ToHashSet();
    }

    /// <summary>An employer that has uploaded files (matched as the list matches them), if any.</summary>
    private static int? EmployerWithFiles() => LocalDb.Scalar<int?>(
        @"SELECT TOP 1 emp.id FROM dbo.Employer emp
          WHERE EXISTS (SELECT 1 FROM dbo.UploadedFileLog l WHERE l.TaxID = emp.taxid AND l.PlanYear = emp.filingYear)
          ORDER BY emp.id");

    public static IEnumerable<object[]> Users() => new[]
    {
        new object[] { RoleIds.AccountManager, false },
        new object[] { RoleIds.AccountManager, true },
        new object[] { RoleIds.DataAnalyst, true },
        new object[] { RoleIds.StaffAC, false },
        new object[] { RoleIds.AmSupervisor, false },
        new object[] { RoleIds.SuperAdmin, false },
    };

    [LocalDbTheory]
    [MemberData(nameof(Users))]
    public void TheFileRuleMatchesTheFileList(int roleId, bool assignAnEmployerWithFiles)
    {
        var allFiles = Listed(TestUsers.CreateStaff(RoleIds.SuperAdmin, "files-all").UserId);
        var user = TestUsers.CreateStaff(roleId, "files");
        if (assignAnEmployerWithFiles && EmployerWithFiles() is int employer) TestUsers.Assign(user, employer);

        var listed = Listed(user.UserId);
        var visible = Visible(allFiles, user.UserId);
        Assert.Equal(listed.OrderBy(i => i), visible.OrderBy(i => i));
    }

    [LocalDbFact]
    public void AnAccountManagerSeesTheirEmployersFilesOnly()
    {
        if (EmployerWithFiles() is not int employer) return;   // no uploaded files locally: nothing to compare
        var allFiles = Listed(TestUsers.CreateStaff(RoleIds.SuperAdmin, "files-all").UserId);
        var none = TestUsers.CreateStaff(RoleIds.AccountManager, "files-none");
        var one = TestUsers.CreateStaff(RoleIds.AccountManager, "files-one");
        TestUsers.Assign(one, employer);

        Assert.Empty(Visible(allFiles, none.UserId));
        var theirs = Visible(allFiles, one.UserId);
        Assert.NotEmpty(theirs);
        Assert.True(theirs.Count < allFiles.Count || allFiles.Count == 1, "an Account Manager with one employer sees every file");
    }

    [LocalDbFact]
    public async Task AnAccountManagerCannotReachAnotherEmployersFileById()
    {
        var someFile = LocalDb.Scalar<int?>("SELECT TOP 1 FileLogId FROM dbo.UploadedFileLog WHERE ISNULL(IsDeleted, 0) = 0 ORDER BY FileLogId DESC");
        if (someFile is not int fileId) return;   // no uploaded files locally
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.AccountManager, "files-http"));

        var download = await client.GetAsync($"/FileUpload/DownloadFile?id={fileId}");
        Assert.True(download.StatusCode is HttpStatusCode.Forbidden
                    || (download.StatusCode == HttpStatusCode.Redirect && download.Headers.Location!.OriginalString.Contains("AccessDenied")),
            $"download answered {(int)download.StatusCode} {download.Headers.Location}");

        var history = await client.AjaxAsync($"/FileUpload/GetFileHistory?id={fileId}");
        Assert.Equal(HttpStatusCode.Unauthorized, history.StatusCode);

        var archive = await client.PostFormAsync("/FileUpload/ToggleArchive", "/Faq/Index",
            new Dictionary<string, string> { ["fileLogId"] = fileId.ToString() }, ajax: true);
        Assert.NotEqual(HttpStatusCode.OK, archive.StatusCode);
    }

    [LocalDbFact]
    public async Task ASupervisorCanOpenAnyFilesHistory()
    {
        var someFile = LocalDb.Scalar<int?>("SELECT TOP 1 FileLogId FROM dbo.UploadedFileLog WHERE ISNULL(IsDeleted, 0) = 0 ORDER BY FileLogId DESC");
        if (someFile is not int fileId) return;
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.DaSupervisor, "files-sup"));
        Assert.Equal(HttpStatusCode.OK, (await client.AjaxAsync($"/FileUpload/GetFileHistory?id={fileId}")).StatusCode);
    }
}
