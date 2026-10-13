using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using ACA360.Tests.Integration.Infrastructure;
using OfficeOpenXml;

namespace ACA360.Tests.Integration;

/// <summary>
/// Administration > Dummy Data: SuperAdmin only; a download is the import template filled in
/// (one employer: a workbook, several: a zip of them), described by the X-Dummy-Data-Summary
/// header. Generating writes nothing to the database.
/// </summary>
[Collection(AppCollection.Name)]
public class DummyDataTests : IDisposable
{
    private readonly AcaWebFactory _app;

    public DummyDataTests(AcaWebFactory app) => _app = app;

    public void Dispose() => TestImports.RemoveAll();

    private static Dictionary<string, string> Form(params int[] employerIds)
    {
        var form = new Dictionary<string, string>
        {
            ["FilingYear"] = "2025", ["Affiliates"] = "1", ["Plans"] = "2", ["Employees"] = "30", ["DependentsMin"] = "0",
            ["DependentsMax"] = "2", ["WarningPercent"] = "10", ["ErrorPercent"] = "10", ["Seed"] = "7",
        };
        for (int i = 0; i < employerIds.Length; i++) form[$"EmployerIds[{i}]"] = employerIds[i].ToString();
        return form;
    }

    [LocalDbTheory]
    [InlineData(RoleIds.Admin)]
    [InlineData(RoleIds.AcaDirector)]
    [InlineData(RoleIds.DataAnalyst)]
    public async Task OnlySuperAdminCanUseIt(int roleId)
    {
        var employer = TestImports.CreateEmployer(2025);
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(roleId, "dummy-no"));
        var page = await client.PageAsync("/DummyData");
        Assert.NotEqual(HttpStatusCode.OK, page.StatusCode);
        var post = await client.PostFormAsync("/DummyData/Generate", "/Dashboard", Form(employer.Id), ajax: true);
        Assert.NotEqual(HttpStatusCode.OK, post.StatusCode);
        Assert.Null(post.Content.Headers.ContentDisposition);
    }

    [LocalDbFact]
    public async Task OneEmployerGivesAWorkbookDescribedByTheSummary()
    {
        var employer = TestImports.CreateEmployer(2025);
        var employersBefore = LocalDb.Scalar<int>("SELECT COUNT(*) FROM dbo.Employer");
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.SuperAdmin, "dummy"));
        var page = await (await client.PageAsync("/DummyData")).Content.ReadAsStringAsync();
        Assert.Contains($"value=\"{employer.Id}\"", page);

        var response = await client.PostFormAsync("/DummyData/Generate", "/DummyData", Form(employer.Id), ajax: true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.EndsWith(".xlsx", response.Content.Headers.ContentDisposition!.FileName!.Trim('"'));

        using var package = new ExcelPackage(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        var employees = package.Workbook.Worksheets["Employee"];
        Assert.Equal(30, employees.Dimension.End.Row - 1);
        Assert.Equal(employer.Ein, employees.Cells[2, 1].Text);
        Assert.Equal(2, package.Workbook.Worksheets["Employer Tab"].Dimension.End.Row - 1);

        var summary = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(
            response.Headers.GetValues("X-Dummy-Data-Summary").Single()))).RootElement[0];
        Assert.Equal(30, summary.GetProperty("employees").GetInt32());
        // 3 employees, plus one extra plan for each of the 2 plan kinds.
        Assert.Equal(5, summary.GetProperty("warnings").EnumerateObject().Sum(p => p.Value.GetInt32()));
        Assert.Equal(3, summary.GetProperty("errors").EnumerateObject().Sum(p => p.Value.GetInt32()));
        Assert.Equal(24, summary.GetProperty("cleanEmployees").GetInt32());

        Assert.Equal(employersBefore, LocalDb.Scalar<int>("SELECT COUNT(*) FROM dbo.Employer"));   // nothing written
    }

    [LocalDbFact]
    public async Task SeveralEmployersComeAsAZipOfWorkbooks()
    {
        var a = TestImports.CreateEmployer(2025);
        var b = TestImports.CreateEmployer(2025);
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.SuperAdmin, "dummy-zip"));
        var response = await client.PostFormAsync("/DummyData/Generate", "/DummyData", Form(a.Id, b.Id), ajax: true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType!.MediaType);

        using var zip = new ZipArchive(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        Assert.Equal(2, zip.Entries.Count);
        var eins = new List<string>();
        foreach (var entry in zip.Entries)
        {
            Assert.EndsWith(".xlsx", entry.Name);
            using var s = new MemoryStream();
            using (var e = entry.Open()) e.CopyTo(s);
            using var package = new ExcelPackage(s);
            eins.Add(package.Workbook.Worksheets["Employer Tab"].Cells[2, 1].Text);
        }
        Assert.Equal(new[] { a.Ein, b.Ein }.OrderBy(x => x), eins.OrderBy(x => x));
    }

    [LocalDbFact]
    public async Task ANewNameGetsAFileWithAMadeUpEinAndNothingIsCreated()
    {
        var employersBefore = LocalDb.Scalar<int>("SELECT COUNT(*) FROM dbo.Employer");
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.SuperAdmin, "dummy-new"));
        var form = Form();
        form["NewEmployerNames"] = "ACA360TESTS Brand New Co.";
        var response = await client.PostFormAsync("/DummyData/Generate", "/DummyData", form, ajax: true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var summary = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(
            response.Headers.GetValues("X-Dummy-Data-Summary").Single()))).RootElement[0];
        Assert.True(summary.GetProperty("isNewEmployer").GetBoolean());
        Assert.Equal("ACA360TESTS BRAND NEW CO", summary.GetProperty("employerName").GetString());
        var ein = summary.GetProperty("ein").GetString()!;
        Assert.Matches(@"^\d{2}-\d{7}$", ein);
        Assert.Equal(0, LocalDb.Scalar<int>("SELECT COUNT(*) FROM dbo.Employer WHERE REPLACE(taxid, '-', '') = @digits",
            new { digits = ein.Replace("-", "") }));

        using var package = new ExcelPackage(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        Assert.Equal(ein, package.Workbook.Worksheets["Employer Tab"].Cells[2, 1].Text);
        Assert.Equal(employersBefore, LocalDb.Scalar<int>("SELECT COUNT(*) FROM dbo.Employer"));
    }

    [LocalDbFact]
    public async Task ABadRequestIsRefusedInPlainWords()
    {
        var employer = TestImports.CreateEmployer(2025);
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.SuperAdmin, "dummy-bad"));
        var form = Form(employer.Id);
        form["WarningPercent"] = "60"; form["ErrorPercent"] = "60";
        var response = await client.PostFormAsync("/DummyData/Generate", "/DummyData", form, ajax: true);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("100%", await response.Content.ReadAsStringAsync());
    }
}
