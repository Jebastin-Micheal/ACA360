using System.Net;
using ACA360.BusinessLogic.Interfaces;
using ACA360.Tests.Integration.Infrastructure;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace ACA360.Tests.Integration;

/// <summary>
/// The two ways an employee reaches their 1095:
/// <list type="bullet">
/// <item>MyForms: the e-mailed link (SecureDownloadLinks) plus the last four digits of the SSN.</item>
/// <item>The Portal area: a login activated by staff, a forced password change on first use,
/// the electronic-delivery consent, and the form download.</item>
/// </list>
/// Each test uses its own throw-away employee (see <see cref="TestEmployees"/>).
/// </summary>
[Collection(AppCollection.Name)]
public class EmployeePortalTests
{
    private readonly AcaWebFactory _app;

    public EmployeePortalTests(AcaWebFactory app) => _app = app;

    private static int LatestYear() => LocalDb.Scalar<int>("SELECT MAX(filingYear) FROM dbo.FilingYear");

    private static int AnEmployer() => LocalDb.Scalar<int>(
        "SELECT TOP 1 id FROM dbo.Employer WHERE ISNULL(IsDeleted, 0) = 0 AND ISNULL(companyId, 0) = 0 AND ISNULL(FormType, '') NOT LIKE '%B%' ORDER BY id");

    private T Service<T>() where T : notnull => _app.Services.CreateScope().ServiceProvider.GetRequiredService<T>();

    private static async Task<string> Text(HttpResponseMessage response) =>
        WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

    // ---- MyForms: secure link + last four SSN digits

    private async Task<Guid> NewLink(TestEmployee employee) =>
        Guid.Parse(await Service<IDistributionService>().GenerateSecureLinkAsync(employee.Id, employee.Year));

    private static Task<HttpResponseMessage> Verify(AcaClient client, Guid token, string last4) =>
        client.PostFormAsync("/MyForms/Verify", $"/MyForms/Access?token={token}",
            new Dictionary<string, string> { ["token"] = token.ToString(), ["last4SSN"] = last4 });

    [LocalDbFact]
    public async Task TheRightDigitsOpenTheFormAndTheFormIsTheEmployees()
    {
        var employee = TestEmployees.Create(AnEmployer(), LatestYear(), "Linkholder");
        var token = await NewLink(employee);
        using var client = _app.NewClient();

        var access = await client.PageAsync($"/MyForms/Access?token={token}");
        Assert.Equal(HttpStatusCode.OK, access.StatusCode);

        var verify = await Verify(client, token, employee.Last4);
        Assert.Equal(HttpStatusCode.Redirect, verify.StatusCode);
        Assert.Contains("/MyForms/Dashboard", verify.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await client.PageAsync("/MyForms/Dashboard")).StatusCode);

        var form = await client.GetAsync($"/MyForms/DownloadForm?year={employee.Year}");
        Assert.Equal(HttpStatusCode.OK, form.StatusCode);
        Assert.Equal("application/pdf", form.Content.Headers.ContentType?.MediaType);
        var pdf = await form.Content.ReadAsByteArrayAsync();
        Assert.True(PdfText.IsPdf(pdf));
        var text = PdfText.Extract(pdf);
        Assert.Contains("Linkholder", text);
        Assert.Contains(TestEmployees.LastName, text);
        Assert.Contains("1E", text);

        Assert.Equal(1, LocalDb.Scalar<int>("SELECT UseCount FROM dbo.SecureDownloadLinks WHERE LinkId = @token", new { token }));
    }

    [LocalDbFact]
    public async Task WrongDigitsAreRefusedAndCounted()
    {
        var employee = TestEmployees.Create(AnEmployer(), LatestYear());
        var token = await NewLink(employee);
        using var client = _app.NewClient();

        var wrong = employee.Last4 == "0000" ? "1111" : "0000";
        var response = await Verify(client, token, wrong);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);   // back on the access page
        Assert.Contains("Verification Failed", await Text(response));
        Assert.Equal(1, LocalDb.Scalar<int>("SELECT FailedAttempts FROM dbo.SecureDownloadLinks WHERE LinkId = @token", new { token }));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/MyForms/DownloadForm?year={employee.Year}")).StatusCode);
    }

    [LocalDbFact]
    public async Task FiveWrongTriesLockTheLinkEvenForTheRightDigits()
    {
        var employee = TestEmployees.Create(AnEmployer(), LatestYear());
        var token = await NewLink(employee);
        using var client = _app.NewClient();
        var wrong = employee.Last4 == "0000" ? "1111" : "0000";

        for (int i = 0; i < 5; i++) await Verify(client, token, wrong);
        var locked = await Verify(client, token, employee.Last4);
        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
        Assert.Contains("Too many failed attempts", await Text(locked));
        Assert.NotNull(LocalDb.Scalar<DateTime?>("SELECT LockedUntil FROM dbo.SecureDownloadLinks WHERE LinkId = @token", new { token }));
    }

    [LocalDbFact]
    public async Task AnExpiredLinkIsRefused()
    {
        var employee = TestEmployees.Create(AnEmployer(), LatestYear());
        var token = await NewLink(employee);
        LocalDb.Execute("UPDATE dbo.SecureDownloadLinks SET ExpirationDate = DATEADD(DAY, -1, GETUTCDATE()) WHERE LinkId = @token", new { token });
        using var client = _app.NewClient();

        var response = await Verify(client, token, employee.Last4);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Verification Failed", await Text(response));
    }

    [LocalDbFact]
    public async Task NoFormWithoutVerifyingAndNoneAfterLeaving()
    {
        var employee = TestEmployees.Create(AnEmployer(), LatestYear());
        var token = await NewLink(employee);
        using var client = _app.NewClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/MyForms/DownloadForm?year={employee.Year}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PageAsync($"/MyForms/Access?token={Guid.Empty}")).StatusCode);

        await Verify(client, token, employee.Last4);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/MyForms/DownloadForm?year={employee.Year}")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.PageAsync("/MyForms/Logout")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/MyForms/DownloadForm?year={employee.Year}")).StatusCode);
    }

    [LocalDbFact]
    public async Task TheFormEmailCarriesTheLinkAndIsLogged()
    {
        var employee = TestEmployees.Create(AnEmployer(), LatestYear());
        var link = $"https://localhost/MyForms/Access?token={Guid.NewGuid()}";

        var result = await Service<IDistributionService>().SendEmployeeFormEmailAsync(employee.Id, employee.Year, link);

        Assert.True(result.Success);
        var message = Assert.Single(_app.Mail.To(employee.Email));
        Assert.Contains(employee.Year.ToString(), message.Subject);
        Assert.Contains(WebUtility.HtmlEncode(link), message.Html);
        Assert.Contains(link, message.Text);
        Assert.Contains("last four digits", message.Text);
        Assert.Equal(1, LocalDb.Scalar<int>(
            "SELECT COUNT(*) FROM dbo.CommunicationLog WHERE EntityType = 'Employee' AND RelatedEntityId = @id AND SentTo = @email",
            new { id = employee.Id, email = employee.Email }));
    }

    // ---- Portal area: activated login

    private static Task<HttpResponseMessage> PortalSignIn(AcaClient client, string email, string password) =>
        client.PostFormAsync("/Portal/Account/Login", "/Portal/Account/Login",
            new Dictionary<string, string> { ["Email"] = email, ["Password"] = password });

    private async Task<(TestEmployee Employee, string Password)> ActivatedPortalUser(AcaClient client)
    {
        var employee = TestEmployees.Create(AnEmployer(), LatestYear(), "Portaluser");
        var temp = await Service<IEmployeePortalAuthService>().ActivateAsync(employee.Id, employee.Year, employee.Email, "ACA360.Tests");

        var first = await PortalSignIn(client, employee.Email, temp);
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.Contains("/Portal/Account/SetPassword", first.Headers.Location!.OriginalString);

        var password = "P0rtal!" + Guid.NewGuid().ToString("N")[..10];
        var set = await client.PostFormAsync("/Portal/Account/SetPassword", "/Portal/Account/SetPassword",
            new Dictionary<string, string> { ["Email"] = employee.Email, ["NewPassword"] = password, ["ConfirmPassword"] = password });
        Assert.Equal(HttpStatusCode.Redirect, set.StatusCode);
        Assert.Contains("/Portal/TaxForms", set.Headers.Location!.OriginalString);
        return (employee, password);
    }

    [LocalDbFact]
    public async Task AnActivatedEmployeeSetsAPasswordAndSeesTheirForms()
    {
        using var client = _app.NewClient();
        var (employee, password) = await ActivatedPortalUser(client);

        var forms = await client.PageAsync("/Portal/TaxForms");
        Assert.Equal(HttpStatusCode.OK, forms.StatusCode);
        Assert.Contains(employee.Year.ToString(), await Text(forms));

        // The temporary password no longer works; the chosen one does.
        Assert.False(LocalDb.Scalar<bool>("SELECT RequirePasswordReset FROM dbo.EmployeePortalUser WHERE Email = @e", new { e = employee.Email }));
        using var again = _app.NewClient();
        var signIn = await PortalSignIn(again, employee.Email, password);
        Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);
        Assert.Contains("/Portal/TaxForms", signIn.Headers.Location!.OriginalString);
    }

    [LocalDbFact]
    public async Task AWrongPortalPasswordIsRefused()
    {
        using var client = _app.NewClient();
        var (employee, _) = await ActivatedPortalUser(client);

        using var other = _app.NewClient();
        var response = await PortalSignIn(other, employee.Email, "Wrong-" + Guid.NewGuid().ToString("N")[..8]);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Invalid email or password", await Text(response));
        Assert.Equal(1, LocalDb.Scalar<int>("SELECT FailedLoginCount FROM dbo.EmployeePortalUser WHERE Email = @e", new { e = employee.Email }));
    }

    [LocalDbFact]
    public async Task AnInactivePortalLoginIsRefused()
    {
        using var client = _app.NewClient();
        var (employee, password) = await ActivatedPortalUser(client);
        LocalDb.Execute("UPDATE dbo.EmployeePortalUser SET IsActive = 0 WHERE Email = @e", new { e = employee.Email });

        using var other = _app.NewClient();
        var response = await PortalSignIn(other, employee.Email, password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("inactive", await Text(response), StringComparison.OrdinalIgnoreCase);
    }

    [LocalDbFact]
    public async Task TheFormDownloadsOnlyWithElectronicConsent()
    {
        using var client = _app.NewClient();
        var (employee, _) = await ActivatedPortalUser(client);

        var beforeConsent = await client.GetAsync($"/Portal/TaxForms/Download?year={employee.Year}");
        Assert.Equal(HttpStatusCode.NotFound, beforeConsent.StatusCode);

        var save = await client.PostFormAsync("/Portal/TaxForms/SavePreference", "/Portal/TaxForms", new Dictionary<string, string>
        {
            ["TaxYear"] = employee.Year.ToString(),
            ["DeliveryPreference"] = "Electronic",
            ["NotificationEmail"] = employee.Email,
            ["AgreeToConsent"] = "true",
        });
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        Assert.Equal("Active", LocalDb.Scalar<string>(
            "SELECT Status FROM dbo.EmployeeConsent WHERE EmployeeId = @id AND TaxYear = @y", new { id = employee.Id, y = employee.Year }));

        var form = await client.GetAsync($"/Portal/TaxForms/Download?year={employee.Year}");
        Assert.Equal(HttpStatusCode.OK, form.StatusCode);
        var pdf = await form.Content.ReadAsByteArrayAsync();
        Assert.True(PdfText.IsPdf(pdf));
        Assert.Contains("Portaluser", PdfText.Extract(pdf));
    }

    [LocalDbFact]
    public async Task ElectronicConsentNeedsTheAgreementAndAnEmail()
    {
        using var client = _app.NewClient();
        var (employee, _) = await ActivatedPortalUser(client);

        var response = await client.PostFormAsync("/Portal/TaxForms/SavePreference", "/Portal/TaxForms", new Dictionary<string, string>
        {
            ["TaxYear"] = employee.Year.ToString(),
            ["DeliveryPreference"] = "Electronic",
            ["NotificationEmail"] = "",
            ["AgreeToConsent"] = "false",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);   // the form again, with the errors
        var html = await Text(response);
        Assert.Contains("You must consent", html);
        Assert.Contains("notification email is required", html);
        Assert.Equal(0, LocalDb.Scalar<int>("SELECT COUNT(*) FROM dbo.EmployeeConsent WHERE EmployeeId = @id AND Status = 'Active'", new { id = employee.Id }));
    }

    [LocalDbFact]
    public async Task APortalLoginCannotOpenStaffPages()
    {
        using var client = _app.NewClient();
        await ActivatedPortalUser(client);

        foreach (var url in new[] { "/Dashboard", "/UserAccounts", "/Employee", "/TrackerEmployer" })
        {
            var response = await client.PageAsync(url);
            Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Redirect,
                $"{url} answered {(int)response.StatusCode}");
            if (response.StatusCode == HttpStatusCode.Redirect)
                Assert.DoesNotContain(url, response.Headers.Location!.OriginalString.Split('?')[0], StringComparison.OrdinalIgnoreCase);
        }
    }

    [LocalDbFact]
    public async Task SetPasswordNeedsTheTemporaryPasswordStepFirst()
    {
        using var client = _app.NewClient();
        var response = await client.PageAsync("/Portal/Account/SetPassword");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Portal/Account/Login", response.Headers.Location!.OriginalString);
    }
}
