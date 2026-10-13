using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ACA360.BusinessLogic.Interfaces;
using ACA360.BusinessLogic.Services;
using ACA360.Core.Models;
using ACA360.Tests.Integration.Infrastructure;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ACA360.Tests.Integration;

/// <summary>
/// What the app produces for the IRS and for employees: the 1095-C / 1095-B / 1094-C PDFs,
/// the batch preview, the 1095-C e-mail run, and the IRS AIR XML (form data + manifest).
/// Uses throw-away employees under the first C-form employer for 2025 (it has codes and a 2025
/// template); the IRS tests use a throw-away employer of their own.
/// </summary>
[Collection(AppCollection.Name)]
public class FormOutputTests
{
    private const int Year = 2025;
    private readonly AcaWebFactory _app;

    public FormOutputTests(AcaWebFactory app) => _app = app;

    /// <summary>A C-form main employer with 1095-C codes for <see cref="Year"/>.</summary>
    private static int CFormEmployer() => LocalDb.Scalar<int>(
        @"SELECT TOP 1 e.id FROM dbo.Employer e
          WHERE ISNULL(e.IsDeleted, 0) = 0 AND ISNULL(e.companyId, 0) = 0 AND e.filingYear = @Year
            AND ISNULL(e.FormType, '') NOT LIKE '%B%'
            AND EXISTS (SELECT 1 FROM dbo.EmployeeCode c WHERE c.employerId = e.id AND c.filingYear = @Year AND c.getForm = 1)
          ORDER BY e.id", new { Year });

    private T Service<T>() where T : notnull => _app.Services.CreateScope().ServiceProvider.GetRequiredService<T>();

    private static string Digits(string s) => new(s.Where(char.IsDigit).ToArray());

    // ---- PDFs

    [LocalDbFact]
    public async Task The1095CCarriesTheEmployeeTheEmployerAndTheCodes()
    {
        var employerId = CFormEmployer();
        var employee = TestEmployees.Create(employerId, Year, "Formholder");
        var employerName = LocalDb.Scalar<string>("SELECT name FROM dbo.Employer WHERE id = @employerId", new { employerId });

        var pdf = await Service<IPdfService>().Generate1095CForEmployeeAsync(employee.Id, Year);

        Assert.True(PdfText.IsPdf(pdf));
        var text = PdfText.Extract(pdf);
        Assert.Contains("Formholder", text);
        Assert.Contains(TestEmployees.LastName, text);
        Assert.Contains(employee.Ssn, text);
        Assert.Contains(employerName.Split(' ')[0], text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1E", text);
        Assert.Contains("2C", text);
        Assert.Contains("95.50", text);
    }

    [LocalDbFact]
    public async Task SuppressingTheSsnPrintsOnlyTheLastFour()
    {
        var employee = TestEmployees.Create(CFormEmployer(), Year);
        var text = PdfText.Extract(await Service<IPdfService>().Generate1095CForEmployeeAsync(employee.Id, Year, suppressSSN: true));

        Assert.Contains("XXX-XX-" + employee.Last4, text);
        Assert.DoesNotContain(employee.Ssn, text);
        Assert.DoesNotContain(Digits(employee.Ssn), text);
    }

    [LocalDbFact]
    public async Task The1095BIsProducedForAnEmployee()
    {
        var employee = TestEmployees.Create(CFormEmployer(), Year, "Bformholder");
        var pdf = await Service<IPdfService>().Generate1095BForEmployeeAsync(employee.Id, Year);

        Assert.True(PdfText.IsPdf(pdf));
        Assert.Contains("Bformholder", PdfText.Extract(pdf));
    }

    [LocalDbFact]
    public async Task The1094CCarriesTheEmployer()
    {
        var employerId = CFormEmployer();
        var (name, ein) = LocalDb.Open().QuerySingle<(string, string)>("SELECT name, taxid FROM dbo.Employer WHERE id = @employerId", new { employerId });

        var pdf = await Service<IPdfService>().Generate1094CForEmployerAsync(employerId, Year);

        Assert.True(PdfText.IsPdf(pdf));
        var text = PdfText.Extract(pdf);
        Assert.Contains(name.Split(' ')[0], text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Digits(ein), Digits(text));
    }

    [LocalDbFact]
    public async Task TheBatchPreviewMergesTheTransmittalAndTheForms()
    {
        var employerId = CFormEmployer();
        var first = TestEmployees.Create(employerId, Year, "Previewone");
        var second = TestEmployees.Create(employerId, Year, "Previewtwo");
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.SuperAdmin, "preview"));
        var token = AcaClient.Token(await (await client.PageAsync("/FormGeneration")).Content.ReadAsStringAsync());

        var request = new HttpRequestMessage(HttpMethod.Post, "/FormGeneration/GenerateBatchPreview")
        {
            Content = JsonContent.Create(new { EmployerIds = new[] { employerId }, EmployeeIds = new[] { first.Id, second.Id }, Year, FormType = "1094-C_1095-C" }),
        };
        request.Headers.Add("RequestVerificationToken", token);
        var response = await client.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var pdf = await response.Content.ReadAsByteArrayAsync();
        Assert.True(PdfText.IsPdf(pdf));
        var text = PdfText.Extract(pdf);
        Assert.Contains("Previewone", text);
        Assert.Contains("Previewtwo", text);
        Assert.True(PdfText.Pages(pdf) >= 3, "expected the 1094-C and two 1095-Cs");
    }

    [LocalDbFact]
    public async Task ThePreviewIsRefusedWithoutAFormType()
    {
        var employee = TestEmployees.Create(CFormEmployer(), Year);
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.SuperAdmin, "preview-type"));
        var token = AcaClient.Token(await (await client.PageAsync("/FormGeneration")).Content.ReadAsStringAsync());

        var request = new HttpRequestMessage(HttpMethod.Post, "/FormGeneration/GenerateBatchPreview")
        {
            Content = JsonContent.Create(new { EmployerIds = new[] { employee.EmployerId }, EmployeeIds = new[] { employee.Id }, Year }),
        };
        request.Headers.Add("RequestVerificationToken", token);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.Http.SendAsync(request)).StatusCode);
    }

    // ---- E-mail run: the message, then the link it carries

    [LocalDbFact]
    public async Task TheEmailRunSendsAWorkingLink()
    {
        var employee = TestEmployees.Create(CFormEmployer(), Year, "Mailrecipient");

        await Service<IDistributionService>().ProcessBatchEmailDistributionAsync(employee.EmployerId, Year, new List<int> { employee.Id }, "https://aca360.example.test");

        var message = Assert.Single(_app.Mail.To(employee.Email));
        var link = Regex.Match(message.Text ?? "", @"https://aca360\.example\.test/MyForms/Access\?token=([0-9a-fA-F-]{36})");
        Assert.True(link.Success, "the e-mail carries no portal link: " + message.Text);
        var token = Guid.Parse(link.Groups[1].Value);
        Assert.Equal(employee.Id, LocalDb.Scalar<int>("SELECT EmployeeId FROM dbo.SecureDownloadLinks WHERE LinkId = @token", new { token }));
        Assert.Equal("Sent", LocalDb.Scalar<string>(
            "SELECT TOP 1 Status FROM dbo.CommunicationLog WHERE EntityType = 'Employee' AND RelatedEntityId = @id ORDER BY LogId DESC", new { id = employee.Id }));

        using var client = _app.NewClient();
        var verify = await client.PostFormAsync("/MyForms/Verify", $"/MyForms/Access?token={token}",
            new Dictionary<string, string> { ["token"] = token.ToString(), ["last4SSN"] = employee.Last4 });
        Assert.Equal(HttpStatusCode.Redirect, verify.StatusCode);
        var form = await client.GetAsync($"/MyForms/DownloadForm?year={Year}");
        Assert.Contains("Mailrecipient", PdfText.Extract(await form.Content.ReadAsByteArrayAsync()));
    }

    // ---- IRS AIR XML (TY2025 format; a throw-away employer, so no real submission is touched)

    /// <summary>
    /// Runs <paramref name="test"/> with transmitter settings the IRS accepts: a mailing address (script 048;
    /// the manifest needs one) and a well-formed TCC (the local one is a placeholder). The row's own values
    /// come back afterwards.
    /// </summary>
    private static async Task WithTransmitterReadyAsync(Func<Task> test)
    {
        dynamic saved;
        using (var db = LocalDb.Open())
            saved = db.QuerySingle("SELECT TOP 1 ConfigId, TCC, AddressLine1, City, State, ZipCode FROM dbo.TransmitterConfig WHERE IsActive = 1 ORDER BY ConfigId");
        int configId = saved.ConfigId;
        LocalDb.Execute("UPDATE dbo.TransmitterConfig SET TCC = 'BB000', AddressLine1 = '1 Test Plaza', City = 'Testville', State = 'NJ', ZipCode = '07001' WHERE ConfigId = @configId", new { configId });
        try { await test(); }
        finally
        {
            LocalDb.Execute("UPDATE dbo.TransmitterConfig SET TCC = @t, AddressLine1 = @a, City = @c, State = @s, ZipCode = @z WHERE ConfigId = @configId",
                new { t = (string?)saved.TCC, a = (string?)saved.AddressLine1, c = (string?)saved.City, s = (string?)saved.State, z = (string?)saved.ZipCode, configId });
        }
    }

    private static void RemoveSubmissions(int employerId)
    {
        // Errors first; corrections before the submissions they correct; record lists go with their row.
        LocalDb.Execute(
            @"DELETE FROM dbo.IrsSubmissionErrors WHERE SubmissionId IN (SELECT SubmissionId FROM dbo.IrsSubmissionLog WHERE EmployerId = @employerId);
              DELETE FROM dbo.IrsSubmissionLog WHERE EmployerId = @employerId AND ParentSubmissionId IS NOT NULL;
              DELETE FROM dbo.IrsSubmissionLog WHERE EmployerId = @employerId;", new { employerId });
        var folder = Path.Combine(AcaWebFactory.WebRoot, "IrsSubmissions", employerId.ToString());
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    [LocalDbFact]
    public async Task TheIrsSubmissionMatchesTheIrsSchemaAndIsRecordedWithItsId()
    {
        var employer = TestImports.CreateEmployer(Year);
        var employee = TestEmployees.Create(employer.Id, Year, "Irsrecipient");
        var user = TestUsers.CreateStaff(RoleIds.SuperAdmin, "irs");
        try
        {
            await WithTransmitterReadyAsync(async () =>
            {
                // GeneratedBy is the user id, as IrsController passes it.
                var log = await Service<IIrsXmlService>().GenerateSubmissionFilesAsync(employer.Id, Year, user.UserId.ToString());

                // The new log row's id comes back, and the row points at the files.
                Assert.True(log.SubmissionId > 0, "no SubmissionId");
                Assert.Equal(Path.GetFileName(log.FormFilePath), Path.GetFileName(LocalDb.Scalar<string>(
                    "SELECT FormFilePath FROM dbo.IrsSubmissionLog WHERE SubmissionId = @id AND EmployerId = @employerId",
                    new { id = log.SubmissionId, employerId = employer.Id })));

                // Both files are in the IRS format: the IRS schema accepts them.
                var form = XDocument.Load(log.FormFilePath!);
                var manifest = XDocument.Load(log.ManifestFilePath!);
                Assert.Empty(IrsAirXml.Validate(form));
                Assert.Empty(IrsAirXml.Validate(manifest));

                XNamespace air = IrsAirXml.AirNamespace(Year), irs = IrsAirXml.Irs;
                var mine = form.Descendants(air + "Form1095CUpstreamDetail").Single();
                // Record IDs run 1, 2, 3...; the record list says record 1 is this employee.
                Assert.Equal("1", mine.Element(air + "RecordId")!.Value);
                Assert.Equal(employee.Id, LocalDb.Scalar<int>("SELECT EmployeeId FROM dbo.IrsSubmissionRecord WHERE SubmissionId = @id AND RecordId = 1", new { id = log.SubmissionId }));
                Assert.Equal("Irsrecipient", mine.Descendants(air + "PersonFirstNm").First().Value, ignoreCase: true);
                // Digits are not allowed in IRS person names.
                Assert.Equal(IrsText.PersonName(TestEmployees.LastName), mine.Descendants(air + "PersonLastNm").First().Value);
                Assert.Equal(Digits(employee.Ssn), mine.Element(air + "EmployeeInfoGrp")!.Element(irs + "SSN")!.Value);
                Assert.Equal("1E", mine.Descendants(air + "AnnualOfferOfCoverageCd").Single().Value);
                Assert.Equal("01", mine.Element(air + "StartMonthNumberCd")!.Value);
                Assert.Equal(Digits(employer.Ein), form.Descendants(irs + "EmployerEIN").Single().Value);

                string M(string name) => manifest.Descendants().Single(e => e.Name.LocalName == name).Value;
                Assert.Equal(Year.ToString(), M("PaymentYr"));
                Assert.Equal("1094/1095C", M("FormTypeCd"));
                Assert.Equal("1", M("TotalPayeeRecordCnt"));
                Assert.Equal(new FileInfo(log.FormFilePath!).Length.ToString(), M("AttachmentByteSizeNum"));
                Assert.Equal(Path.GetFileName(log.FormFilePath), M("DocumentSystemFileNm"));
                using (var file = File.OpenRead(log.FormFilePath!))
                    Assert.Equal(Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant(), M("ChecksumAugmentationNum"));
            });
        }
        finally { RemoveSubmissions(employer.Id); }
    }

    [LocalDbFact]
    public async Task DataTheIrsWouldRejectStopsGenerationAndLeavesNothingBehind()
    {
        var employer = TestImports.CreateEmployer(Year);
        var employee = TestEmployees.Create(employer.Id, Year, "Noplanstart");
        LocalDb.Execute("UPDATE dbo.EmployeeCode SET PlanStartMonth = NULL WHERE employeeId = @id", new { id = employee.Id });
        var user = TestUsers.CreateStaff(RoleIds.SuperAdmin, "irs-refused");
        try
        {
            await WithTransmitterReadyAsync(async () =>
            {
                var ex = await Assert.ThrowsAsync<IrsDataException>(() =>
                    Service<IIrsXmlService>().GenerateSubmissionFilesAsync(employer.Id, Year, user.UserId.ToString()));
                // The message says which employee and what to fix (the IRS page shows it).
                Assert.Contains($"Employee {employee.Id}", ex.Message);
                Assert.Contains("plan start month", ex.Message);
            });

            Assert.Equal(0, LocalDb.Scalar<int>("SELECT COUNT(*) FROM dbo.IrsSubmissionLog WHERE EmployerId = @id", new { id = employer.Id }));
            var folder = Path.Combine(AcaWebFactory.WebRoot, "IrsSubmissions", employer.Id.ToString());
            Assert.True(!Directory.Exists(folder) || !Directory.EnumerateFiles(folder).Any(), "files were left behind");
        }
        finally { RemoveSubmissions(employer.Id); }
    }

    private static IFormFile Upload(string xml, string name = "errors.xml")
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(xml);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name);
    }

    /// <summary>An IRS Error Data File (as downloaded from the AIR UI) for one form and the 1094.</summary>
    private static string ErrorDataFile(string receipt, int recordId) => $"""
        <FormBCTransmitterSubmissionDtl xmlns="urn:us:gov:treasury:irs:ext:aca:air:ty25" xmlns:irs="urn:us:gov:treasury:irs:common">
          <ACATransmitterSubmissionDetail>
            <TransmitterErrorDetailGrp>
              <SubmissionLevelStatusCd>Accepted with Errors</SubmissionLevelStatusCd>
              <UniqueSubmissionId>{receipt}|1</UniqueSubmissionId>
              <irs:ErrorMessageDetail><irs:ErrorMessageCd>1094C-011</irs:ErrorMessageCd><irs:ErrorMessageTxt>Test error on the 1094-C.</irs:ErrorMessageTxt></irs:ErrorMessageDetail>
            </TransmitterErrorDetailGrp>
            <TransmitterErrorDetailGrp>
              <UniqueRecordId>{receipt}|1|{recordId}</UniqueRecordId>
              <irs:ErrorMessageDetail><irs:ErrorMessageCd>AIRTN500</irs:ErrorMessageCd><irs:ErrorMessageTxt>The SSN does not match the name.</irs:ErrorMessageTxt></irs:ErrorMessageDetail>
            </TransmitterErrorDetailGrp>
          </ACATransmitterSubmissionDetail>
        </FormBCTransmitterSubmissionDtl>
        """;

    [LocalDbFact]
    public async Task TheIrsErrorFileIsTracedToEmployeesAndCorrectedByTheirOriginalRecord()
    {
        var employer = TestImports.CreateEmployer(Year);
        var first = TestEmployees.Create(employer.Id, Year, "Ackfirst");
        var second = TestEmployees.Create(employer.Id, Year, "Acksecond");
        var user = TestUsers.CreateStaff(RoleIds.SuperAdmin, "irs-ack");
        var receipt = $"1095C-25-{RandomNumberGenerator.GetInt32(10_000_000, 99_999_999)}";
        try
        {
            await WithTransmitterReadyAsync(async () =>
            {
                var irs = Service<IIrsXmlService>();
                var log = await irs.GenerateSubmissionFilesAsync(employer.Id, Year, user.UserId.ToString());
                int RecordOf(int employeeId) => LocalDb.Scalar<int>(
                    "SELECT RecordId FROM dbo.IrsSubmissionRecord WHERE SubmissionId = @s AND EmployeeId = @e", new { s = log.SubmissionId, e = employeeId });
                Assert.Equal(new[] { 1, 2 }, new[] { RecordOf(first.Id), RecordOf(second.Id) }.OrderBy(r => r));
                int secondRecord = RecordOf(second.Id);

                // An acknowledgement naming a record the submission does not have changes nothing.
                await Assert.ThrowsAsync<IrsDataException>(() => irs.ProcessAckFileAsync(log.SubmissionId, Upload(ErrorDataFile(receipt, 7))));
                Assert.Equal(0, LocalDb.Scalar<int>("SELECT COUNT(*) FROM dbo.IrsSubmissionErrors WHERE SubmissionId = @id", new { id = log.SubmissionId }));

                // The Error Data File: status, receipt, and each error on its employee.
                Assert.True(await irs.ProcessAckFileAsync(log.SubmissionId, Upload(ErrorDataFile(receipt, secondRecord))));
                var after = (await irs.GetSubmissionByIdAsync(log.SubmissionId))!;
                Assert.Equal(3, after.StatusId);
                Assert.Equal(receipt, after.ReceiptId);
                Assert.True(File.Exists(after.AckFilePath));
                var errors = await irs.GetSubmissionErrorsAsync(log.SubmissionId);
                Assert.Equal(2, errors.Count);
                var onForm = errors.Single(e => e.ErrorCode == "AIRTN500");
                Assert.Equal(second.Id.ToString(), onForm.RecordId);
                Assert.Equal($"{receipt}|1|{secondRecord}", onForm.UniqueRecordId);
                Assert.Contains("Acksecond", onForm.EmployeeName, StringComparison.OrdinalIgnoreCase);
                Assert.Null(errors.Single(e => e.ErrorCode == "1094C-011").RecordId);

                // Item 7: the employee's first name is fixed after filing; the correction names the
                // record as it was filed and carries the new name in the form itself.
                LocalDb.Execute("UPDATE dbo.Employee SET firstName = 'Ackrenamed' WHERE id = @id", new { id = second.Id });

                // Errors on the 1094-C and on a 1095-C: two correction transmissions (Pub 5165, 7.1.4).
                var made = await irs.GenerateCorrectionFilesAsync(log.SubmissionId, user.UserId.ToString());
                Assert.Equal(2, made.Count);
                Assert.All(made, c =>
                {
                    Assert.True(c.SubmissionId > 0 && c.SubmissionId != log.SubmissionId);
                    Assert.Equal("C", LocalDb.Scalar<string>("SELECT SubmissionType FROM dbo.IrsSubmissionLog WHERE SubmissionId = @id", new { id = c.SubmissionId }));
                    Assert.Equal(log.SubmissionId, LocalDb.Scalar<int>("SELECT ParentSubmissionId FROM dbo.IrsSubmissionLog WHERE SubmissionId = @id", new { id = c.SubmissionId }));
                    Assert.Empty(IrsAirXml.Validate(XDocument.Load(c.FormFilePath!)));
                    Assert.Empty(IrsAirXml.Validate(XDocument.Load(c.ManifestFilePath!)));
                });
                XNamespace air = IrsAirXml.AirNamespace(Year);

                // The corrected 1094-C: complete, authoritative, alone, naming the 1094-C as filed (7.1.2).
                var transmittal = XDocument.Load(made.Single(c => c.RecordCount == 0).FormFilePath!);
                var detail = transmittal.Descendants(air + "Form1094CUpstreamDetail").Single();
                Assert.Equal("1", detail.Element(air + "CorrectedInd")!.Value);
                Assert.Equal($"{receipt}|1", detail.Descendants(air + "CorrectedUniqueSubmissionId").Single().Value);
                Assert.Equal(Digits(employer.Ein), detail.Descendants(air + "CorrectedSubmissionPayerTIN").Single().Value);
                Assert.Equal("1", detail.Element(air + "AuthoritativeTransmittalInd")!.Value);
                Assert.Empty(transmittal.Descendants(air + "Form1095CUpstreamDetail"));

                // The corrected 1095-C: only that employee, numbered from 1, naming the record it corrects.
                var correction = made.Single(c => c.RecordCount > 0);
                var form = XDocument.Load(correction.FormFilePath!);
                var corrected = form.Descendants(air + "Form1095CUpstreamDetail").Single();
                Assert.Equal("1", corrected.Element(air + "RecordId")!.Value);
                Assert.Equal("1", corrected.Element(air + "CorrectedInd")!.Value);
                Assert.Equal($"{receipt}|1|{secondRecord}", corrected.Descendants(air + "CorrectedUniqueRecordId").Single().Value);
                var asFiled = corrected.Element(air + "CorrectedRecordRecipientGrp")!;
                Assert.Equal("Acksecond", asFiled.Descendants(air + "PersonFirstNm").Single().Value, ignoreCase: true);
                Assert.Equal(Digits(second.Ssn), asFiled.Element(air + "CorrectedRecRecipientTIN")!.Value);
                Assert.Equal("Ackrenamed", corrected.Element(air + "EmployeeInfoGrp")!.Descendants(air + "PersonFirstNm").Single().Value, ignoreCase: true);
                Assert.Equal("C", XDocument.Load(correction.ManifestFilePath!).Descendants(air + "TransmissionTypeCd").Single().Value);

                // A record already corrected is corrected again through its accepted correction (7.1.1).
                var correctionReceipt = receipt + "C";
                LocalDb.Execute("UPDATE dbo.IrsSubmissionLog SET StatusId = 2, ReceiptId = @r WHERE SubmissionId = @id", new { r = correctionReceipt, id = correction.SubmissionId });
                LocalDb.Execute("UPDATE dbo.IrsSubmissionLog SET StatusId = 2 WHERE SubmissionId = @id", new { id = made.Single(c => c.RecordCount == 0).SubmissionId });
                var again = await irs.GenerateCorrectionFilesAsync(log.SubmissionId, user.UserId.ToString());
                var againForm = XDocument.Load(again.Single(c => c.RecordCount > 0).FormFilePath!);
                Assert.Equal($"{correctionReceipt}|1|1", againForm.Descendants(air + "CorrectedUniqueRecordId").Single().Value);
                Assert.Equal("Ackrenamed", againForm.Descendants(air + "CorrectedRecordRecipientGrp").Single().Descendants(air + "PersonFirstNm").Single().Value, ignoreCase: true);
            });
        }
        finally { RemoveSubmissions(employer.Id); }
    }

    private static string Manifest(IrsSubmissionLog log, string element) =>
        XDocument.Load(log.ManifestFilePath!).Descendants().Single(e => e.Name.LocalName == element).Value;

    [LocalDbFact]
    public async Task OnlyFilesNotSentAreRegeneratedAndARejectedFilingIsReplaced()
    {
        var employer = TestImports.CreateEmployer(Year);
        TestEmployees.Create(employer.Id, Year, "Replacement");
        var user = TestUsers.CreateStaff(RoleIds.SuperAdmin, "irs-replace");
        var rejected = $"1095C-25-{RandomNumberGenerator.GetInt32(10_000_000, 99_999_999)}";
        try
        {
            await WithTransmitterReadyAsync(async () =>
            {
                var irs = Service<IIrsXmlService>();
                void SetStatus(int id, int status, string? receipt) =>
                    LocalDb.Execute("UPDATE dbo.IrsSubmissionLog SET StatusId = @status, ReceiptId = @receipt WHERE SubmissionId = @id", new { id, status, receipt });

                // Not sent yet: regenerated as an original.
                var log = await irs.GenerateSubmissionFilesAsync(employer.Id, Year, user.UserId.ToString());
                var again = await irs.GenerateSubmissionFilesAsync(employer.Id, Year, user.UserId.ToString());
                Assert.Equal(log.SubmissionId, again.SubmissionId);
                Assert.Equal("O", Manifest(again, "TransmissionTypeCd"));

                // Sent and waiting, or accepted: refused, nothing changes.
                foreach (var (status, says) in new[] { (1, "waiting for the IRS acknowledgement"), (2, "corrections"), (3, "corrections") })
                {
                    SetStatus(log.SubmissionId, status, status == 1 ? null : "1095C-25-00000001");
                    var ex = await Assert.ThrowsAsync<IrsDataException>(() => irs.GenerateSubmissionFilesAsync(employer.Id, Year, user.UserId.ToString()));
                    Assert.Contains(says, ex.Message);
                    Assert.Equal(again.TransmissionId, LocalDb.Scalar<Guid>("SELECT TransmissionId FROM dbo.IrsSubmissionLog WHERE SubmissionId = @id", new { id = log.SubmissionId }));
                }

                // Rejected: a replacement naming the rejected receipt; not a correction.
                SetStatus(log.SubmissionId, 4, rejected);
                var correctionEx = await Assert.ThrowsAsync<IrsDataException>(() => irs.GenerateCorrectionFilesAsync(log.SubmissionId, user.UserId.ToString()));
                Assert.Contains("replaced", correctionEx.Message);
                var replacement = await irs.GenerateSubmissionFilesAsync(employer.Id, Year, user.UserId.ToString());
                Assert.Equal("R", Manifest(replacement, "TransmissionTypeCd"));
                Assert.Equal(rejected, Manifest(replacement, "OriginalReceiptId"));
                Assert.Empty(IrsAirXml.Validate(XDocument.Load(replacement.ManifestFilePath!)));
                Assert.Equal(rejected, LocalDb.Scalar<string>("SELECT ReplacesReceiptId FROM dbo.IrsSubmissionLog WHERE SubmissionId = @id", new { id = log.SubmissionId }));

                // Regenerated again before it is sent: still the replacement of the same receipt.
                var stillReplacement = await irs.GenerateSubmissionFilesAsync(employer.Id, Year, user.UserId.ToString());
                Assert.Equal("R", Manifest(stillReplacement, "TransmissionTypeCd"));
                Assert.Equal(rejected, Manifest(stillReplacement, "OriginalReceiptId"));
            });
        }
        finally { RemoveSubmissions(employer.Id); }
    }
}
