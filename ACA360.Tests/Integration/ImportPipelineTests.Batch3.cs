using System.Net;
using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Helpers;
using ACA360.Tests.Integration.Infrastructure;
using Dapper;
using OfficeOpenXml;

namespace ACA360.Tests.Integration;

/// <summary>
/// The October 2026 third batch where it rides on a real import: duplicate SSNs (066), the rule
/// what-if (067), employee history and the employer timeline (068, 069), file notifications (070)
/// and an employer's own column names (073).
/// </summary>
public partial class ImportPipelineTests
{
    /// <summary>Upload, structure check, validation and merge, as the background jobs do them.</summary>
    private async Task<int> ImportAsync(TestImports.Workbook book, TestEmployer employer, TestUser analyst, string label)
    {
        var fileLogId = await LogFile(book.Save(label), employer, analyst);
        await Service<IFileProcessingService>().ProcessFileAsync(fileLogId, StandardTemplate);
        Assign(fileLogId, analyst);
        await Service<IDataProcessingService>().ExtractAndValidateDataAsync(fileLogId);
        await Service<IDataProcessingService>().ImportToLiveAsync(fileLogId, "Merge", analyst.UserId.ToString());
        Assert.True(Log(fileLogId).WorkflowStatusId == WS.Complete, $"{label}: import ended at {Log(fileLogId).WorkflowStatusId}\n{Errors(fileLogId)}");
        return fileLogId;
    }

    private static string DigitsOf(string s) => new(s.Where(char.IsDigit).ToArray());

    /// <summary>
    /// An SSN another employer already has for the year is a warning (EMP-160), not an error: the
    /// file still imports. Once both are in, Reports > Duplicate SSNs shows them, by the last four digits.
    /// </summary>
    [LocalDbFact]
    public async Task AnSsnAlreadyUnderAnotherEmployerIsAWarningAndOnTheReport()
    {
        var first = TestImports.CreateEmployer(Year);
        var second = TestImports.CreateEmployer(Year);
        var analyst = TestUsers.CreateStaff(RoleIds.DataAnalyst, "dupssn");
        var shared = Ssn();
        await ImportAsync(GoodFile(first, shared, Ssn()), first, analyst, "dup-first");

        var fileTwo = await LogFile(GoodFile(second, shared, Ssn()).Save("dup-second"), second, analyst);
        await Service<IFileProcessingService>().ProcessFileAsync(fileTwo, StandardTemplate);
        Assign(fileTwo, analyst);
        await Service<IDataProcessingService>().ExtractAndValidateDataAsync(fileTwo);

        var flagged = LocalDb.Open().Query<string>(
            @"SELECT S.EmployeeSSN FROM dbo.StagingRowErrors E
              JOIN dbo.ValidationRules R ON R.RuleId = E.RuleId AND R.RuleCode = 'EMP-160'
              JOIN dbo.Staging_Employees S ON S.StagingEmployeeId = E.StagingRowId
              WHERE E.FileLogId = @fileTwo AND E.StagingTableName = 'Staging_Employees'", new { fileTwo }).Select(DigitsOf).ToList();
        Assert.Equal(new[] { DigitsOf(shared) }, flagged);
        Assert.NotEqual(VS.DataError, Log(fileTwo).ValidationStatusId);

        await Service<IDataProcessingService>().ImportToLiveAsync(fileTwo, "Merge", analyst.UserId.ToString());
        using var admin = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.SuperAdmin, "dupssn-admin"));
        var page = await (await admin.PageAsync($"/DuplicateSsn?year={Year}")).Content.ReadAsStringAsync();
        Assert.Contains("XXX-XX-" + DigitsOf(shared)[^4..], page);
        Assert.Contains(first.Name, page);
        Assert.Contains(second.Name, page);
        Assert.DoesNotContain(shared, page);
    }

    /// <summary>
    /// A second file that changes an employee's city is kept on the employee's history (the field,
    /// old and new), nothing else on that employee counts as changed, and the History tab and the
    /// employer's Timeline show it to staff, not to the employer's own login.
    /// </summary>
    [LocalDbFact]
    public async Task ImportsRecordWhatTheyChangeOnTheEmployee()
    {
        var employer = TestImports.CreateEmployer(Year);
        var analyst = TestUsers.CreateStaff(RoleIds.DataAnalyst, "history");
        var (ssnOne, ssnTwo) = (Ssn(), Ssn());
        await ImportAsync(GoodFile(employer, ssnOne, ssnTwo), employer, analyst, "history-1");

        var again = GoodFile(employer, ssnOne, ssnTwo);
        again.Sheets["Employee"][0]["City"] = "NEWTOWN";
        await ImportAsync(again, employer, analyst, "history-2");

        using var db = LocalDb.Open();
        int EmployeeOf(string ssn) => db.ExecuteScalar<int>(
            "SELECT id FROM dbo.Employee WHERE EmployerId = @id AND REPLACE(ssn, '-', '') = @d", new { id = employer.Id, d = DigitsOf(ssn) });
        var one = EmployeeOf(ssnOne);
        var changes = db.Query<(string ChangeKind, string? FieldName, string? OldValue, string? NewValue)>(
            "SELECT ChangeKind, FieldName, OldValue, NewValue FROM dbo.EmployeeChangeHistory WHERE EmployeeId = @one", new { one }).ToList();
        Assert.Contains(changes, c => c.ChangeKind == "Added");
        var changed = changes.Where(c => c.ChangeKind == "Changed").ToList();
        Assert.True(changed.Count == 1, "changed: " + string.Join("; ", changed.Select(c => $"{c.FieldName} {c.OldValue} -> {c.NewValue}")));
        Assert.Equal(("City", "TESTVILLE", "NEWTOWN"), (changed[0].FieldName, changed[0].OldValue?.ToUpperInvariant(), changed[0].NewValue?.ToUpperInvariant()));
        Assert.Equal(0, db.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM dbo.EmployeeChangeHistory WHERE EmployeeId = @two AND ChangeKind = 'Changed'", new { two = EmployeeOf(ssnTwo) }));

        using var admin = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.SuperAdmin, "history-admin"));
        var history = await admin.AjaxAsync($"/Employee/History?id={one}");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var html = await history.Content.ReadAsStringAsync();
        Assert.Contains("Changed by an import", html);
        Assert.Contains("NEWTOWN", html, StringComparison.OrdinalIgnoreCase);

        var timeline = await (await admin.AjaxAsync($"/Employer/Timeline?employerId={employer.Id}")).Content.ReadAsStringAsync();
        Assert.Contains("File uploaded", timeline);
        Assert.Contains("Import Complete", timeline);

        using var own = await _app.SignedInAs(TestUsers.CreateEmployer(employer.Id, "history-employer"));
        Assert.NotEqual(HttpStatusCode.OK, (await own.AjaxAsync($"/Employee/History?id={one}")).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await own.AjaxAsync($"/Employer/Timeline?employerId={employer.Id}")).StatusCode);
    }

    /// <summary>
    /// A file whose SSN column has the employer's own name fails the structure check; mapped once
    /// from the rejected file, it passes and is read, and the employer's next file passes straight away.
    /// </summary>
    [LocalDbFact]
    public async Task AnEmployersOwnColumnNameIsMappedOnceAndThenRead()
    {
        var employer = TestImports.CreateEmployer(Year);
        var analyst = TestUsers.CreateStaff(RoleIds.DataAnalyst, "colmap");
        var ssnOne = Ssn();
        string Renamed(string label)
        {
            var path = GoodFile(employer, ssnOne, Ssn()).Save(label);
            ExcelPackage.LicenseContext = LicenseContext.Commercial;
            using var package = new ExcelPackage(new FileInfo(path));
            var ws = package.Workbook.Worksheets["Employee"];
            for (int c = 1; c <= ws.Dimension.End.Column; c++)
                if (ws.Cells[1, c].Text.Trim() == "Employee SSN") ws.Cells[1, c].Value = "SOCIAL SECURITY NO";
            package.Save();
            return path;
        }

        var fileLogId = await LogFile(Renamed("colmap-1"), employer, analyst);
        await Service<IFileProcessingService>().ProcessFileAsync(fileLogId, StandardTemplate);
        Assert.Equal(VS.StructureError, Log(fileLogId).ValidationStatusId);

        using var client = await _app.SignedInAs(analyst);
        var form = await client.AjaxAsync($"/FileUpload/ColumnMapping?id={fileLogId}");
        Assert.Equal(HttpStatusCode.OK, form.StatusCode);
        var html = await form.Content.ReadAsStringAsync();
        Assert.Contains("SOCIAL SECURITY NO", html);
        Assert.Contains("Employee SSN", html);

        var saved = await client.PostFormAsync($"/FileUpload/SaveColumnMapping?id={fileLogId}", "/Notifications", new Dictionary<string, string>
        {
            ["maps[0].Sheet"] = "Employee", ["maps[0].Target"] = "EmployeeSSN", ["maps[0].Header"] = "SOCIAL SECURITY NO",
        }, ajax: true);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(1, LocalDb.Scalar<int>("SELECT COUNT(*) FROM dbo.EmployerColumnMaps WHERE EmployerEin = @ein AND SourceHeader = 'SOCIAL SECURITY NO'",
            new { ein = employer.Ein.Replace("-", "") }));

        // What the queued job does: the same check, now with the employer's header.
        await Service<IFileProcessingService>().ProcessFileAsync(fileLogId, StandardTemplate);
        Assert.NotEqual(VS.StructureError, Log(fileLogId).ValidationStatusId);
        Assign(fileLogId, analyst);
        await Service<IDataProcessingService>().ExtractAndValidateDataAsync(fileLogId);
        Assert.Equal(1, LocalDb.Open().Query<string>("SELECT EmployeeSSN FROM dbo.Staging_Employees WHERE FileLogId = @fileLogId", new { fileLogId })
            .Count(s => DigitsOf(s ?? "") == DigitsOf(ssnOne)));

        var next = await LogFile(Renamed("colmap-2"), employer, analyst);
        await Service<IFileProcessingService>().ProcessFileAsync(next, StandardTemplate);
        Assert.NotEqual(VS.StructureError, Log(next).ValidationStatusId);

        // Someone who cannot see the file cannot map for it.
        using var outsider = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.DataAnalyst, "colmap-out"));
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.AjaxAsync($"/FileUpload/ColumnMapping?id={fileLogId}")).StatusCode);
    }

    /// <summary>
    /// The uploader hears when their file is ready (the bell lists it, opening it marks it read and
    /// goes to the file), nobody else can mark it, and with that event switched off the next file is silent.
    /// </summary>
    [LocalDbFact]
    public async Task TheUploaderHearsWhenTheirFileIsReadyUnlessTheyTurnedItOff()
    {
        var employer = TestImports.CreateEmployer(Year);
        var analyst = TestUsers.CreateStaff(RoleIds.DataAnalyst, "notify");
        async Task<int> ValidateFile(string label)
        {
            var id = await LogFile(GoodFile(employer, Ssn(), Ssn()).Save(label), employer, analyst);
            await Service<IFileProcessingService>().ProcessFileAsync(id, StandardTemplate);
            Assign(id, analyst);
            await Service<IDataProcessingService>().ExtractAndValidateDataAsync(id);
            return id;
        }
        int Ready() => LocalDb.Scalar<int>("SELECT COUNT(*) FROM dbo.tbl_UserNotifications WHERE UserId = @u AND Title LIKE 'File ready%'", new { u = analyst.UserId });

        var fileLogId = await ValidateFile("notify-1");
        Assert.Equal(1, Ready());

        using var client = await _app.SignedInAs(analyst);
        var bell = await client.JsonAsync("/Notifications/GetNewNotifications");
        Assert.True(bell.GetProperty("unread").GetInt32() >= 1);
        var item = bell.GetProperty("items").EnumerateArray().First(i => i.GetProperty("title").GetString()!.StartsWith("File ready"));
        var id = item.GetProperty("notificationId").GetInt32();

        using var other = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.DataAnalyst, "notify-other"));
        var foreign = await other.PostFormAsync("/Notifications/MarkAsRead", "/Notifications", new Dictionary<string, string> { ["id"] = id.ToString() }, ajax: true);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        var open = await client.GetAsync($"/Notifications/Open/{id}");
        Assert.Equal(HttpStatusCode.Redirect, open.StatusCode);
        Assert.Equal($"/ErrorTriage/ErrorTriageDashboard/{fileLogId}", open.Headers.Location?.OriginalString);
        Assert.True(LocalDb.Scalar<bool>("SELECT IsRead FROM dbo.tbl_UserNotifications WHERE NotificationId = @id", new { id }));

        var off = await client.PostFormAsync("/Notifications/SavePreference", "/Notifications",
            new Dictionary<string, string> { ["eventCode"] = "FileReady", ["isEnabled"] = "false" }, ajax: true);
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        await ValidateFile("notify-2");
        Assert.Equal(1, Ready());
    }

    /// <summary>
    /// Trying a rule counts what it would flag in the files still in staging and changes nothing:
    /// the file's own errors are the same afterwards and the trial leaves no rows behind.
    /// </summary>
    [LocalDbFact]
    public async Task TryingARuleCountsWhatItWouldFlagAndChangesNothing()
    {
        var employer = TestImports.CreateEmployer(Year);
        var analyst = TestUsers.CreateStaff(RoleIds.DataAnalyst, "whatif");
        var fileLogId = await LogFile(GoodFile(employer, Ssn(), Ssn()).Save("whatif"), employer, analyst);
        await Service<IFileProcessingService>().ProcessFileAsync(fileLogId, StandardTemplate);
        Assign(fileLogId, analyst);
        await Service<IDataProcessingService>().ExtractAndValidateDataAsync(fileLogId);
        int Errs() => LocalDb.Scalar<int>("SELECT COUNT(*) FROM dbo.StagingRowErrors WHERE FileLogId = @fileLogId", new { fileLogId });
        var before = Errs();

        using var admin = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.SuperAdmin, "whatif-admin"));
        // Both employees in the file have no middle initial.
        var response = await admin.PostFormAsync("/Rules/WhatIf?maxFiles=1", "/Notifications", new Dictionary<string, string>
        {
            ["RuleId"] = "0", ["TargetTable"] = "Staging_Employees", ["TargetColumn"] = "EmployeeMiddleInitial", ["ValidationType"] = "REQUIRED",
        }, ajax: true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("data-would-flag>2<", html);
        Assert.Contains($"#{fileLogId}", html);

        Assert.Equal(before, Errs());
        Assert.Equal(0, LocalDb.Scalar<int>("SELECT COUNT(*) FROM dbo.RuleWhatIfRuns"));

        // Only the roles that may change rules can try them.
        using var analystClient = await _app.SignedInAs(analyst);
        var refused = await analystClient.PostFormAsync("/Rules/WhatIf", "/Notifications", new Dictionary<string, string>
        {
            ["TargetTable"] = "Staging_Employees", ["TargetColumn"] = "EmployeeMiddleInitial", ["ValidationType"] = "REQUIRED",
        }, ajax: true);
        Assert.NotEqual(HttpStatusCode.OK, refused.StatusCode);
    }
}
