using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ACA360.BusinessLogic.Interfaces;

namespace ACA360.Tests.Integration.Infrastructure;

/// <summary>
/// The real ACA360 web app, started in memory against the local database. Everything is the
/// same as a normal run except that the Hangfire job server does not start, so the tests never
/// pick up background jobs (the recurring-job registrations still run).
/// </summary>
public class AcaWebFactory : WebApplicationFactory<Program>
{
    /// <summary>Every e-mail the app tried to send during the run (nothing really leaves).</summary>
    public FakeMail Mail { get; } = new();

    /// <summary>Duo, off unless a test switches it on (DuoSignInTests).</summary>
    public FakeDuo Duo { get; } = new();

    /// <summary>The web project folder: the app's content root.</summary>
    public static string WebRoot { get; } = FindWebProject();

    private readonly bool _ownsTestData;

    public AcaWebFactory() : this(ownsTestData: true) { }

    /// <param name="ownsTestData">Whether this instance removes test data when it starts and ends.
    /// A second app in the same run (the browser one) leaves that to the first.</param>
    protected AcaWebFactory(bool ownsTestData)
    {
        _ownsTestData = ownsTestData;
        // The app resolves some files (the 1095 PDF templates) relative to the working
        // directory, which is the web project when it runs normally.
        Directory.SetCurrentDirectory(WebRoot);
        if (!ownsTestData) return;
        TestUsers.RemoveAll();   // leftovers from a run that crashed
        TestEmployees.RemoveAll();
        TestImports.RemoveAll();
    }

    private static string FindWebProject()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ACA360.sln"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("ACA360.sln not found"), "ACA360.Web");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
                services.Remove(hosted);

            foreach (var mail in services.Where(d => d.ServiceType == typeof(IMailDeliveryService)).ToList())
                services.Remove(mail);
            services.AddSingleton<IMailDeliveryService>(Mail);

            foreach (var duo in services.Where(d => d.ServiceType == typeof(ACA360.Web.Infrastructure.IDuoTwoFactorService)).ToList())
                services.Remove(duo);
            services.AddSingleton<ACA360.Web.Infrastructure.IDuoTwoFactorService>(Duo);
        });
    }

    /// <summary>A browser-like client: keeps cookies, does not follow redirects.</summary>
    public AcaClient NewClient() => new(CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true,
        BaseAddress = new Uri("https://localhost"),
    }));

    /// <summary>A client signed in as the given user (signing in is checked to have worked).</summary>
    public async Task<AcaClient> SignedInAs(TestUser user)
    {
        var client = NewClient();
        var response = await client.SignInAsync(user.Email, user.Password);
        if (response.StatusCode != HttpStatusCode.Redirect || AcaClient.IsSignInPage(response.Headers.Location))
            throw new InvalidOperationException($"Signing in as {user.Email} failed: {(int)response.StatusCode} {response.Headers.Location}");
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        if (_ownsTestData)
        {
            TestUsers.RemoveAll();
            TestEmployees.RemoveAll();
            TestImports.RemoveAll();
        }
        base.Dispose(disposing);
    }
}

/// <summary>An HttpClient with the helpers ACA360 pages need: antiforgery tokens, AJAX headers.</summary>
public sealed class AcaClient : IDisposable
{
    public HttpClient Http { get; }

    public AcaClient(HttpClient http) => Http = http;

    public static bool IsSignInPage(Uri? location) =>
        location != null && location.OriginalString.Contains("/Account/Login", StringComparison.OrdinalIgnoreCase);

    public Task<HttpResponseMessage> GetAsync(string url) => Http.GetAsync(url);

    public Task<HttpResponseMessage> PageAsync(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("text/html");
        return Http.SendAsync(request);
    }

    public Task<HttpResponseMessage> AjaxAsync(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        return Http.SendAsync(request);
    }

    public async Task<JsonElement> JsonAsync(string url)
    {
        var response = await AjaxAsync(url);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    public static string Token(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        return match.Success ? match.Groups[1].Value : throw new InvalidOperationException("The page has no antiforgery token.");
    }

    /// <summary>A form post carrying the antiforgery token from <paramref name="tokenPage"/>.</summary>
    public async Task<HttpResponseMessage> PostFormAsync(string url, string tokenPage, IDictionary<string, string> fields, bool ajax = false)
    {
        var html = await (await Http.GetAsync(tokenPage)).Content.ReadAsStringAsync();
        var form = new Dictionary<string, string>(fields) { ["__RequestVerificationToken"] = Token(html) };
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
        if (ajax) request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        return await Http.SendAsync(request);
    }

    public Task<HttpResponseMessage> SignInAsync(string userName, string password) =>
        PostFormAsync("/Account/Login", "/Account/Login", new Dictionary<string, string> { ["Username"] = userName, ["Password"] = password });

    public void Dispose() => Http.Dispose();
}

[CollectionDefinition(Name)]
public sealed class AppCollection : ICollectionFixture<AcaWebFactory>, ICollectionFixture<BrowserApp>
{
    /// <summary>All integration tests share one app and run one after another.</summary>
    public const string Name = "ACA360 app on the local database";
}
