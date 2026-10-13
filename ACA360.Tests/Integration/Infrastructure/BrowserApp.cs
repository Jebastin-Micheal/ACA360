using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Playwright;

namespace ACA360.Tests.Integration.Infrastructure;

/// <summary>
/// The app on a real HTTPS port (Kestrel, the developer certificate) for a real browser, with
/// the same test set-up as <see cref="AcaWebFactory"/> (no Hangfire server, fake mail), and the
/// installed Google Chrome driven headless by Playwright. Started the first time a browser test
/// asks for it, so other test runs do not pay for it.
/// </summary>
public sealed class BrowserApp : IAsyncLifetime
{
    public static readonly string ChromePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe");

    /// <summary>Screenshots taken by the browser tests, for a person to look at.</summary>
    public static readonly string ScreenshotFolder = Path.Combine(AppContext.BaseDirectory, "screenshots");

    private readonly SemaphoreSlim _gate = new(1, 1);
    private KestrelFactory? _factory;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public string BaseUrl => _factory?.BaseUrl ?? throw new InvalidOperationException("Not started.");

    private async Task EnsureStartedAsync()
    {
        if (_browser != null) return;
        await _gate.WaitAsync();
        try
        {
            if (_browser != null) return;
            _factory = new KestrelFactory();
            _ = _factory.Server;   // builds and starts both hosts
            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new() { Channel = "chrome", Headless = true });
        }
        finally { _gate.Release(); }
    }

    /// <summary>A new browser context (its own cookies) and page.</summary>
    public async Task<IPage> NewPageAsync(int width = 1366, int height = 900)
    {
        await EnsureStartedAsync();
        var context = await _browser!.NewContextAsync(new()
        {
            BaseURL = BaseUrl,
            IgnoreHTTPSErrors = true,
            ViewportSize = new() { Width = width, Height = height },
        });
        // Generous: the first visit to each page compiles its view, and a busy machine is slow.
        context.SetDefaultTimeout(60_000);
        context.SetDefaultNavigationTimeout(60_000);
        return await context.NewPageAsync();
    }

    /// <summary>Signs in through the sign-in page, as a person would.</summary>
    public async Task<IPage> SignedInPageAsync(TestUser user, int width = 1366, int height = 900)
    {
        var page = await NewPageAsync(width, height);
        await page.GotoAsync("/Account/Login");
        await page.FillAsync("input[name=Username]", user.Email);
        await page.FillAsync("input[name=Password]", user.Password);
        await Task.WhenAll(page.WaitForURLAsync(u => !u.Contains("/Account/Login", StringComparison.OrdinalIgnoreCase)),
            page.ClickAsync("button[type=submit]"));
        return page;
    }

    public static async Task<string> ScreenshotAsync(IPage page, string name)
    {
        Directory.CreateDirectory(ScreenshotFolder);
        var path = Path.Combine(ScreenshotFolder, name + ".png");
        await page.ScreenshotAsync(new() { Path = path, FullPage = false });
        return path;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_browser != null) await _browser.DisposeAsync();
        _playwright?.Dispose();
        if (_factory != null) await _factory.DisposeAsync();
    }

    /// <summary>
    /// WebApplicationFactory in .NET 8 serves only in memory; this also builds the app on Kestrel
    /// (the usual approach: build the in-memory host first, then switch the builder to Kestrel).
    /// </summary>
    private sealed class KestrelFactory : AcaWebFactory
    {
        private IHost? _kestrelHost;

        public KestrelFactory() : base(ownsTestData: false) { }
        public string? BaseUrl { get; private set; }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            var testHost = builder.Build();
            builder.ConfigureWebHost(web => web.UseKestrel(o => o.Listen(IPAddress.Loopback, 0, l => l.UseHttps())));
            _kestrelHost = builder.Build();
            _kestrelHost.Start();
            BaseUrl = _kestrelHost.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.First().Replace("127.0.0.1", "localhost");
            testHost.Start();
            return testHost;
        }

        protected override void Dispose(bool disposing)
        {
            _kestrelHost?.Dispose();
            base.Dispose(disposing);
        }
    }
}

/// <summary>A local-database test that also needs Google Chrome installed.</summary>
public sealed class BrowserFactAttribute : FactAttribute
{
    public BrowserFactAttribute()
    {
        if (LocalDb.SkipReason != null) Skip = LocalDb.SkipReason;
        else if (!File.Exists(BrowserApp.ChromePath)) Skip = "Google Chrome is not installed.";
    }
}

/// <summary>A local-database theory that also needs Google Chrome installed.</summary>
public sealed class BrowserTheoryAttribute : TheoryAttribute
{
    public BrowserTheoryAttribute()
    {
        if (LocalDb.SkipReason != null) Skip = LocalDb.SkipReason;
        else if (!File.Exists(BrowserApp.ChromePath)) Skip = "Google Chrome is not installed.";
    }
}
