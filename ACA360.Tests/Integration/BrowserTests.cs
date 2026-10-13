using System.Text.RegularExpressions;
using ACA360.Tests.Integration.Infrastructure;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace ACA360.Tests.Integration;

/// <summary>
/// The JavaScript, in Google Chrome (headless): no script errors on the main pages, the guided
/// tour (Driver.js: overlay, steps, "seen" kept on the server), the AJAX grids (search, paging,
/// page size, sorting), and that every page fits a phone (390px) and a tablet (768px) screen.
/// Screenshots go to bin/.../screenshots.
/// </summary>
[Collection(AppCollection.Name)]
public class BrowserTests
{
    private readonly BrowserApp _browser;
    private readonly AcaWebFactory _app;

    public BrowserTests(BrowserApp browser, AcaWebFactory app) => (_browser, _app) = (browser, app);

    private static readonly string[] MainPages =
    {
        "/Dashboard", "/Employer", "/Employee", "/Plan", "/FileUpload", "/TrackerEmployer", "/FormGeneration",
        "/Filing", "/Distribution", "/UserAccounts", "/SystemSettings", "/Calendar", "/Faq", "/Settings",
    };

    /// <summary>Uncaught exceptions and console.error calls (failed downloads are reported separately by the browser and left out).</summary>
    private static List<string> WatchErrors(IPage page)
    {
        var errors = new List<string>();
        page.PageError += (_, e) => errors.Add("uncaught: " + e);
        page.Console += (_, m) =>
        {
            if (m.Type == "error" && !m.Text.StartsWith("Failed to load resource", StringComparison.Ordinal))
                errors.Add("console.error: " + m.Text);
        };
        return errors;
    }

    private static int AnEmployer() => LocalDb.Scalar<int>(
        @"SELECT TOP 1 e.id FROM dbo.Employer e WHERE ISNULL(e.IsDeleted, 0) = 0 AND ISNULL(e.companyId, 0) = 0
            AND EXISTS (SELECT 1 FROM dbo.Employee x WHERE x.EmployerId = e.id) ORDER BY e.filingYear DESC, e.id");

    [BrowserFact]
    public async Task SigningInInChromeOpensTheDashboard()
    {
        var page = await _browser.SignedInPageAsync(TestUsers.CreateStaff(RoleIds.SuperAdmin, "chrome"));
        // No employer chosen yet: Select Employer first, as for any new user.
        Assert.Contains("/SelectEmployer", page.Url);
        await page.GotoAsync("/Dashboard");
        await Assertions.Expect(page.Locator("body")).ToContainTextAsync("Dashboard");
        await page.Context.CloseAsync();
    }

    [BrowserFact]
    public async Task TheMainPagesRunWithoutScriptErrors()
    {
        var user = TestUsers.CreateStaff(RoleIds.SuperAdmin, "js-errors");
        var page = await _browser.SignedInPageAsync(user);
        await page.GotoAsync($"/SelectEmployer/SelectThis?id={AnEmployer()}");   // the employer pages need one
        var errors = WatchErrors(page);
        var problems = new List<string>();
        foreach (var url in MainPages)
        {
            errors.Clear();
            await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle });
            await page.WaitForTimeoutAsync(500);   // late scripts (charts, grids)
            problems.AddRange(errors.Select(e => $"{url}: {e}"));
        }
        await page.Context.CloseAsync();
        Assert.True(problems.Count == 0, "Script errors:\n" + string.Join("\n", problems.Distinct()));
    }

    // ---- Guided tour (Driver.js)

    private static ILocator Popover(IPage page) => page.Locator(".driver-popover");

    /// <summary>Waits for the home tour to start by itself; if it does not, says what stopped it.</summary>
    private static async Task WaitForTourAsync(IPage page)
    {
        try { await Assertions.Expect(Popover(page)).ToBeVisibleAsync(new() { Timeout = 30000 }); }
        catch (PlaywrightException ex)
        {
            var why = await page.EvaluateAsync<string>(@"async () => {
                const status = await fetch('/Tour/Status?tour=home:1', { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
                    .then(async r => r.status + ' ' + await r.text()).catch(e => 'error ' + e);
                const modals = [...document.querySelectorAll('.modal.show')].map(m => '#' + m.id).join(', ') || 'none';
                return 'page ' + location.pathname + location.search + '; open dialogs: ' + modals
                    + '; tour script loaded: ' + (typeof window.ACA360StartTour === 'function') + '; status: ' + status;
            }");
            throw new Xunit.Sdk.XunitException("The tour did not start: " + why + Environment.NewLine + ex.Message);
        }
    }

    [BrowserFact]
    public async Task TheTourStartsOnceDimsThePageAndIsRememberedAsSeen()
    {
        var page = await _browser.SignedInPageAsync(TestUsers.CreateStaff(RoleIds.SuperAdmin, "tour"));
        var errors = WatchErrors(page);
        await page.GotoAsync("/Dashboard");

        // Starts by itself the first time (after the page settles).
        await WaitForTourAsync(page);
        var overlay = page.Locator("svg.driver-overlay");
        await Assertions.Expect(overlay).ToBeVisibleAsync();
        var dim = await overlay.EvaluateAsync<string>("svg => getComputedStyle(svg.querySelector('path')).opacity");
        Assert.Equal(0.55, double.Parse(dim, System.Globalization.CultureInfo.InvariantCulture), 2);
        await BrowserApp.ScreenshotAsync(page, "tour-desktop-step1");

        // Steps move on, with progress.
        var progress = page.Locator(".driver-popover-progress-text");
        var firstProgress = await progress.InnerTextAsync();
        Assert.Matches(@"^1 of \d+$", firstProgress);
        await page.ClickAsync(".driver-popover-next-btn");
        await Assertions.Expect(progress).ToHaveTextAsync(new Regex(@"^2 of \d+$"));
        await BrowserApp.ScreenshotAsync(page, "tour-desktop-step2");

        // Closing records it as seen, so it does not start again.
        var seen = page.WaitForResponseAsync(r => r.Url.Contains("/Tour/Seen") && r.Request.Method == "POST");
        await page.ClickAsync(".driver-popover-close-btn");
        Assert.True((await seen).Ok);
        await Assertions.Expect(Popover(page)).ToHaveCountAsync(0);

        await page.ReloadAsync();
        await page.WaitForTimeoutAsync(4000);
        await Assertions.Expect(Popover(page)).ToHaveCountAsync(0);

        // "Take the tour" in the profile menu replays it.
        await page.EvaluateAsync("document.querySelector('[data-tour-start]').click()");
        await Assertions.Expect(Popover(page)).ToBeVisibleAsync();

        Assert.True(errors.Count == 0, string.Join("\n", errors));
        await page.Context.CloseAsync();
    }

    // ---- AJAX grid (User Accounts): search, paging, page size, sorting

    private static async Task<List<string>> GridNames(IPage page) =>
        (await page.Locator("#UserAccountsTable tbody tr .user-name .fw-medium").AllInnerTextsAsync()).Select(n => n.Trim()).ToList();

    private static Task WaitForGrid(IPage page, Func<Task> action) =>
        Task.WhenAll(page.WaitForResponseAsync(r => r.Url.Contains("/UserAccounts/UserAccountsListPartial")), action());

    [BrowserFact]
    public async Task TheGridSearchesPagesResizesAndSorts()
    {
        // Twelve logins whose names share a word nobody else has.
        var token = "zq" + Guid.NewGuid().ToString("N")[..6];
        for (int i = 0; i < 12; i++) TestUsers.CreateStaff(RoleIds.DataAnalyst, $"{token}{(char)('a' + i)}");
        var page = await _browser.SignedInPageAsync(TestUsers.CreateStaff(RoleIds.SuperAdmin, "grid"));
        var errors = WatchErrors(page);
        await page.GotoAsync("/UserAccounts", new() { WaitUntil = WaitUntilState.NetworkIdle });

        await WaitForGrid(page, () => page.FillAsync(".txt_search", token));
        Assert.Equal(10, (await GridNames(page)).Count);
        await Assertions.Expect(page.Locator(".dt-info")).ToContainTextAsync("of 12 entries");

        await WaitForGrid(page, () => page.ClickAsync(".page-link[data-pageindex='2']"));
        Assert.Equal(2, (await GridNames(page)).Count);

        await WaitForGrid(page, () => page.SelectOptionAsync(".drpPageSize", "25"));
        var names = await GridNames(page);
        Assert.Equal(12, names.Count);
        Assert.All(names, n => Assert.Contains(token, n, StringComparison.OrdinalIgnoreCase));

        var nameHeader = page.Locator("#UserAccountsTable th.sortable[data-column='0']");
        await WaitForGrid(page, () => nameHeader.ClickAsync());
        var ascending = await GridNames(page);
        Assert.Equal(ascending.OrderBy(n => n, StringComparer.OrdinalIgnoreCase), ascending);
        await WaitForGrid(page, () => page.Locator("#UserAccountsTable th.sortable[data-column='0']").ClickAsync());
        var descending = await GridNames(page);
        Assert.Equal(descending.OrderByDescending(n => n, StringComparer.OrdinalIgnoreCase), descending);
        Assert.NotEqual(ascending, descending);

        Assert.True(errors.Count == 0, string.Join("\n", errors));
        await page.Context.CloseAsync();
    }

    // ---- Phone width (390 x 844)

    /// <summary>The outermost visible elements that reach past the right edge of the screen
    /// (not counting those inside a box that scrolls or clips them).</summary>
    private const string WiderThanScreen = @"() => {
        const w = innerWidth, out = [];
        const clipped = el => {
            for (let a = el.parentElement; a && a !== document.body; a = a.parentElement)
                if (getComputedStyle(a).overflowX !== 'visible') return true;
            return false;
        };
        for (const el of document.querySelectorAll('body *')) {
            const r = el.getBoundingClientRect();
            if (r.width === 0 || r.right <= w + 1 || getComputedStyle(el).visibility === 'hidden') continue;
            const p = el.parentElement;
            if (p && p !== document.body && p.getBoundingClientRect().right > w + 1) continue;
            if (clipped(el)) continue;
            out.push(el.tagName.toLowerCase() + (el.id ? '#' + el.id : '') + [...el.classList].slice(0, 3).map(c => '.' + c).join('') + ' (to ' + Math.round(r.right) + 'px)');
        }
        return out.slice(0, 5).join(', ');
    }";

    [BrowserFact]
    public async Task ThePagesFitAPhoneScreen()
    {
        var page = await _browser.SignedInPageAsync(TestUsers.CreateStaff(RoleIds.SuperAdmin, "phone"), width: 390, height: 844);
        await page.GotoAsync($"/SelectEmployer/SelectThis?id={AnEmployer()}");
        var tooWide = new List<string>();
        foreach (var url in MainPages)
        {
            await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle });
            await page.WaitForTimeoutAsync(500);
            var size = await page.EvaluateAsync<int[]>("() => [document.documentElement.scrollWidth, window.innerWidth]");
            var (scroll, inner) = (size[0], size[1]);
            if (scroll > inner + 1)
                tooWide.Add($"{url}: page is {scroll}px wide on a {inner}px screen; sticking out: {await page.EvaluateAsync<string>(WiderThanScreen)}");
            await BrowserApp.ScreenshotAsync(page, "phone" + url.Replace('/', '-'));
        }

        // The menu is reachable on a phone (the side menu collapses behind the menu button).
        await page.GotoAsync("/Dashboard");
        await Assertions.Expect(page.Locator(".layout-menu-toggle:visible, .navbar .layout-menu-toggle:visible").First).ToBeVisibleAsync();

        await page.Context.CloseAsync();
        Assert.True(tooWide.Count == 0, "Horizontal scrolling at phone width:\n" + string.Join("\n", tooWide));
    }

    // ---- Every other page, at phone and tablet width

    /// <summary>The page is wider than the screen: what sticks out, or null when it fits.</summary>
    private static async Task<string?> OverflowAsync(IPage page)
    {
        var size = await page.EvaluateAsync<int[]>("() => [document.documentElement.scrollWidth, window.innerWidth]");
        return size[0] > size[1] + 1
            ? $"{size[0]}px wide on a {size[1]}px screen; sticking out: {await page.EvaluateAsync<string>(WiderThanScreen)}"
            : null;
    }

    /// <summary>
    /// The staff pages besides the main ones: every page a SuperAdmin can open without input (the
    /// same list AllPagesTests opens), and the detail pages of an employee, an employer and a
    /// Tracker employer.
    /// </summary>
    private static List<string> OtherStaffPages()
    {
        var employer = AnEmployer();
        var (employee, year) = LocalDb.Open().QueryFirst<(int, int)>(
            @"SELECT TOP 1 e.id, c.filingYear FROM dbo.Employee e JOIN dbo.EmployeeCode c ON c.employeeId = e.id
              WHERE e.EmployerId = @employer ORDER BY c.filingYear DESC, e.id", new { employer });
        var pages = AllPagesTests.PagesForSuperAdmin()
            .Select(k => ACA360.Tests.Infrastructure.ControllerCatalog.Actions.First(a => a.Key == (string)k[0]).Url)
            .Where(u => !MainPages.Contains(u, StringComparer.OrdinalIgnoreCase))
            .ToList();
        pages.AddRange(new[]
        {
            $"/Employee/Details?id={employee}&year={year}",
            $"/Employer/Dashboard?id={employer}&year={year}",
            $"/TrackerEmployer/Dashboard/{employer}",
        });
        return pages.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Opens every page at one width; returns how many whole pages were checked and those too wide.</summary>
    private async Task<(int Checked, List<string> TooWide)> CheckPagesAsync(IReadOnlyList<string> urls, int width, string device)
    {
        var page = await _browser.SignedInPageAsync(TestUsers.CreateStaff(RoleIds.SuperAdmin, "fit-" + device), width: width, height: 1000);
        await page.GotoAsync($"/SelectEmployer/SelectThis?id={AnEmployer()}");
        var tooWide = new List<string>();
        int checkedPages = 0;
        foreach (var url in urls)
        {
            try { await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 30_000 }); }
            catch (PlaywrightException) { continue; }   // a file download, not a page
            // Only whole pages: partial views and JSON answers have no top bar.
            if (!await page.EvaluateAsync<bool>("() => !!document.querySelector('#layout-navbar')")) continue;
            checkedPages++;
            if (await OverflowAsync(page) is { } problem) tooWide.Add($"{device} {width}px, {url}: {problem}");
            if (url.Contains("Details") || url.Contains("Dashboard/")) await BrowserApp.ScreenshotAsync(page, device + url.Split('?')[0].Replace('/', '-'));
        }
        await page.Context.CloseAsync();
        return (checkedPages, tooWide);
    }

    [BrowserFact]
    public async Task EveryStaffPageFitsAPhoneAndATablet()
    {
        var urls = MainPages.Concat(OtherStaffPages()).ToList();
        // Both widths at once, each in its own browser window.
        var results = await Task.WhenAll(CheckPagesAsync(urls, 390, "phone"), CheckPagesAsync(urls, 768, "tablet"));

        Assert.All(results, r => Assert.True(r.Checked > MainPages.Length, $"only {r.Checked} pages were checked"));
        var tooWide = results.SelectMany(r => r.TooWide).ToList();
        Assert.True(tooWide.Count == 0, $"Horizontal scrolling on {tooWide.Count} page(s) (of {results[0].Checked} checked per width):\n" + string.Join("\n", tooWide));
    }

    [BrowserTheory]
    [InlineData(390, "phone")]
    [InlineData(768, "tablet")]
    public async Task TheEmployeePagesFitTheScreen(int width, string device)
    {
        var tooWide = new List<string>();
        async Task Check(IPage p, string name)
        {
            if (await OverflowAsync(p) is { } problem) tooWide.Add($"{name}: {problem}");
            await BrowserApp.ScreenshotAsync(p, $"{device}-{name}");
        }

        // Sign-in pages and the e-mailed form link.
        var employee = TestEmployees.Create(AnEmployer(), LocalDb.Scalar<int>("SELECT MAX(filingYear) FROM dbo.FilingYear"), "Fitcheck");
        var link = await _app.Services.CreateScope().ServiceProvider.GetRequiredService<ACA360.BusinessLogic.Interfaces.IDistributionService>()
            .GenerateSecureLinkAsync(employee.Id, employee.Year);
        var page = await _browser.NewPageAsync(width, 1000);
        foreach (var (url, name) in new[] { ("/Account/Login", "staff-sign-in"), ("/Portal/Account/Login", "portal-sign-in"), ($"/MyForms/Access?token={link}", "form-link") })
        {
            await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle });
            await Check(page, name);
        }

        // The Employee Portal: first sign-in with the temporary password, setting a password, the tax forms.
        var temp = await _app.Services.CreateScope().ServiceProvider.GetRequiredService<ACA360.BusinessLogic.Interfaces.IEmployeePortalAuthService>()
            .ActivateAsync(employee.Id, employee.Year, employee.Email, "ACA360.Tests");
        await page.GotoAsync("/Portal/Account/Login");
        await page.FillAsync("input[name=Email]", employee.Email);
        await page.FillAsync("input[name=Password]", temp);
        await Task.WhenAll(page.WaitForURLAsync(u => u.Contains("/Portal/Account/SetPassword", StringComparison.OrdinalIgnoreCase)), page.ClickAsync("button[type=submit]"));
        await Check(page, "portal-set-password");
        var password = "P0rtal!" + Guid.NewGuid().ToString("N")[..10];
        await page.FillAsync("input[name=NewPassword]", password);
        await page.FillAsync("input[name=ConfirmPassword]", password);
        await Task.WhenAll(page.WaitForURLAsync(u => u.Contains("/Portal/TaxForms", StringComparison.OrdinalIgnoreCase)), page.ClickAsync("button[type=submit]"));
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Check(page, "portal-tax-forms");

        await page.Context.CloseAsync();
        Assert.True(tooWide.Count == 0, $"Horizontal scrolling at {device} width ({width}px):\n" + string.Join("\n", tooWide));
    }

    [BrowserFact]
    public async Task TheTourWorksAtPhoneWidth()
    {
        var page = await _browser.SignedInPageAsync(TestUsers.CreateStaff(RoleIds.SuperAdmin, "tour-phone"), width: 390, height: 844);
        await page.GotoAsync("/Dashboard");
        await WaitForTourAsync(page);
        await Assertions.Expect(page.Locator("svg.driver-overlay")).ToBeVisibleAsync();
        var box = await Popover(page).BoundingBoxAsync();
        Assert.NotNull(box);
        Assert.True(box!.X >= 0 && box.X + box.Width <= 390 + 1, $"the tour box is off screen: x={box.X}, width={box.Width}");
        await BrowserApp.ScreenshotAsync(page, "tour-phone-step1");
        await page.Context.CloseAsync();
    }

    /// <summary>
    /// Employee split view: opening it from the table counts the whole filtered list ("1 of N",
    /// fetched only now: the table view does not use it) and Next moves on. A search with no
    /// match shows the blank detail panel, which used to load jQuery a second time and drop
    /// the page's plugins (input masks failed with "mask is not a function").
    /// </summary>
    [BrowserFact]
    public async Task TheEmployeeSplitViewCountsTheWholeListAndAnEmptyListBreaksNothing()
    {
        var employer = LocalDb.Scalar<int>(
            @"SELECT TOP 1 e.id FROM dbo.Employer e JOIN dbo.EmployeeCode c ON c.employerId = e.id AND c.filingYear = e.filingYear
              WHERE ISNULL(e.IsDeleted, 0) = 0 GROUP BY e.id HAVING COUNT(*) > 1 ORDER BY COUNT(*) DESC");
        var year = LocalDb.Scalar<int>("SELECT filingYear FROM dbo.Employer WHERE id = @employer", new { employer });
        var page = await _browser.SignedInPageAsync(TestUsers.CreateStaff(RoleIds.SuperAdmin, "split"));
        await page.GotoAsync($"/SelectEmployer/SelectThis?id={employer}");
        await page.GotoAsync("/Employee", new() { WaitUntil = WaitUntilState.NetworkIdle });
        // The employer's own filing year (a new login starts on the current year).
        Assert.Equal(200, await page.EvaluateAsync<int>(
            @"([id, year]) => fetch('/SelectEmployer/SetEmployer', { method: 'POST',
                headers: { 'RequestVerificationToken': document.querySelector('input[name=__RequestVerificationToken]').value,
                           'Content-Type': 'application/x-www-form-urlencoded' },
                body: 'employerId=' + id + '&year=' + year }).then(r => r.status)", new[] { employer, year }));
        // The Employees tour starts by itself for a new user and its overlay covers the page;
        // with a large employer it can open in the middle of the test.
        Assert.Equal(200, await page.EvaluateAsync<int>(
            @"() => fetch('/Tour/Seen', { method: 'POST',
                headers: { 'RequestVerificationToken': document.querySelector('input[name=__RequestVerificationToken]').value,
                           'Content-Type': 'application/x-www-form-urlencoded' },
                body: 'tour=employees:1' }).then(r => r.status)"));

        var idListCalls = 0;
        page.Request += (_, r) => { if (r.Url.Contains("GetFilteredEmployeeIds")) idListCalls++; };
        await page.GotoAsync("/Employee", new() { WaitUntil = WaitUntilState.NetworkIdle });
        var errors = WatchErrors(page);
        await page.Locator(".grid-view-btn[data-mode='Table']").First.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.WaitForTimeoutAsync(500);
        Assert.Equal(0, idListCalls);

        var counter = page.Locator("#empNavCounter");
        var idList = await page.RunAndWaitForResponseAsync(
            () => page.Locator(".grid-view-btn[data-mode='Split']").First.ClickAsync(),
            r => r.Url.Contains("GetFilteredEmployeeIds"));
        var total = (await idList.JsonAsync())!.Value.GetArrayLength();
        Assert.True(total > 1);
        await Assertions.Expect(counter).ToHaveTextAsync($"1 of {total}", new() { Timeout = 15_000 });
        Assert.Equal(1, idListCalls);

        await page.Locator("#btnEmpNext").ClickAsync();
        await Assertions.Expect(counter).ToHaveTextAsync($"2 of {total}", new() { Timeout = 15_000 });

        await page.Locator(".grid-search-input").First.PressSequentiallyAsync("zzqqxx no such employee");
        await Assertions.Expect(page.Locator("#employeeListContainer .employee-item[data-id]")).ToHaveCountAsync(0, new() { Timeout = 15_000 });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.WaitForTimeoutAsync(500);
        Assert.True(await page.EvaluateAsync<bool>("() => typeof $.fn.mask === 'function' && typeof $.fn.select2 === 'function'"),
            "the page's jQuery plugins are gone");
        await page.Context.CloseAsync();
        Assert.True(errors.Count == 0, "Script errors:\n" + string.Join("\n", errors.Distinct()));
    }

    /// <summary>
    /// The pages that used to load jQuery a second time (their scripts now run after the
    /// layout's): no script errors, their select2 boxes and DataTables are set up, and the
    /// System settings buttons are wired (checked without clicking Save).
    /// </summary>
    [BrowserFact]
    public async Task ThePagesThatLoadedJQueryTwiceStillWork()
    {
        var page = await _browser.SignedInPageAsync(TestUsers.CreateStaff(RoleIds.SuperAdmin, "jquery-pages"));
        await page.GotoAsync($"/SelectEmployer/SelectThis?id={AnEmployer()}");
        var errors = WatchErrors(page);
        var problems = new List<string>();
        var pages = new (string Url, string Check)[]
        {
            ("/eFileReport", "document.querySelectorAll('.dataTables_wrapper, .dt-container').length > 0"),
            ("/MailFulfillmentReport", "document.querySelectorAll('.dataTables_wrapper, .dt-container').length > 0"),
            ("/Report/Employer_Report", "document.querySelectorAll('.dataTables_wrapper, .dt-container').length > 0 && document.querySelectorAll('.select2-container').length > 0"),
            ("/UserAccounts", "document.querySelectorAll('.select2-container').length > 0"),
            ("/System", "['#btnSaveSecurity', '#btnSaveDatabaseSettings'].every(s => { const el = document.querySelector(s); return !el || !!($._data(el, 'events') || {}).click; })"),
        };
        foreach (var (url, check) in pages)
        {
            errors.Clear();
            await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle });
            await page.WaitForTimeoutAsync(500);
            problems.AddRange(errors.Select(e => $"{url}: {e}"));
            if (!await page.EvaluateAsync<bool>("() => typeof $.fn.select2 === 'function' && typeof $.fn.DataTable === 'function' && typeof $.fn.mask === 'function'"))
                problems.Add($"{url}: jQuery plugins missing");
            if (!await page.EvaluateAsync<bool>("() => " + check))
                problems.Add($"{url}: not set up ({check})");
        }
        await page.Context.CloseAsync();
        Assert.True(problems.Count == 0, string.Join("\n", problems.Distinct()));
    }

    /// <summary>Administration > Dummy Data: choose an employer, generate, and the workbook downloads with a summary.</summary>
    [BrowserFact]
    public async Task TheDummyDataPageDownloadsAWorkbook()
    {
        var employer = TestImports.CreateEmployer(2025);
        try
        {
            var page = await _browser.SignedInPageAsync(TestUsers.CreateStaff(RoleIds.SuperAdmin, "dummy-page"));
            var errors = WatchErrors(page);
            await page.GotoAsync("/DummyData", new() { WaitUntil = WaitUntilState.NetworkIdle });
            await page.EvaluateAsync("id => $('#ddEmployers').val([String(id)]).trigger('change')", employer.Id);
            await page.FillAsync("#ddEmployees", "25");
            await page.ClickAsync("[data-preset='warnings']");

            var download = await page.RunAndWaitForDownloadAsync(() => page.ClickAsync("#ddGenerate"));
            Assert.EndsWith(".xlsx", download.SuggestedFilename);
            await Assertions.Expect(page.Locator("#ddResult")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("#ddResultBody")).ToContainTextAsync(employer.Ein);
            await Assertions.Expect(page.Locator("#ddGenerate")).ToBeEnabledAsync();
            await BrowserApp.ScreenshotAsync(page, "dummy-data");

            await page.SetViewportSizeAsync(390, 844);
            await page.Locator("#ddResult").ScrollIntoViewIfNeededAsync();
            await BrowserApp.ScreenshotAsync(page, "dummy-data-phone");
            await page.Context.CloseAsync();
            Assert.True(errors.Count == 0, "Script errors:\n" + string.Join("\n", errors.Distinct()));
        }
        finally
        {
            TestImports.RemoveAll();
        }
    }

    /// <summary>
    /// The screens added with scripts 061-064, in Chrome: System Health, Filing Deadlines (and its
    /// edit dialog), the dashboard's deadline countdown, deadlines in the Calendar (read-only), and
    /// the reason asked for when a warning is acknowledged.
    /// </summary>
    [BrowserFact]
    public async Task TheHealthDeadlinesAndAcknowledgeScreensWork()
    {
        var employer = TestImports.CreateEmployer(2025);
        try
        {
            var admin = TestUsers.CreateStaff(RoleIds.SuperAdmin, "screens-2");
            using var scope = _app.Services.CreateScope();
            var book = ACA360.BusinessLogic.Services.DummyDataGenerator.Generate(new ACA360.Core.Models.DummyDataRequest
            {
                EmployerIds = { employer.Id }, FilingYear = 2025, Plans = 1, Banding = "Monthly", Employees = 4,
                DependentsMin = 0, DependentsMax = 0, NewHirePercent = 0, Seed = 62,
            }, new ACA360.Core.Models.DummyEmployer(employer.Id, employer.Name, employer.Ein), new Random(62), new HashSet<string>(), new HashSet<string>());
            var rows = book.Rows(ACA360.BusinessLogic.Services.DummyDataGenerator.EmployeeSheet);
            rows[1]["State/ Province"] = "NJ"; rows[1]["Zip"] = "98101";   // a warning to acknowledge
            var file = await ValidateAsync(scope.ServiceProvider, book, employer, admin, "screens3");

            var page = await _browser.SignedInPageAsync(admin);
            await page.GotoAsync("/Calendar", new() { WaitUntil = WaitUntilState.NetworkIdle });
            foreach (var tour in new[] { "triage:1", "home:1" })
                await page.EvaluateAsync<int>(
                    @"tour => fetch('/Tour/Seen', { method: 'POST',
                        headers: { 'RequestVerificationToken': document.querySelector('input[name=__RequestVerificationToken]').value,
                                   'Content-Type': 'application/x-www-form-urlencoded' },
                        body: 'tour=' + encodeURIComponent(tour) }).then(r => r.status)", tour);
            var errors = WatchErrors(page);

            // Calendar: deadlines are there, and open read-only.
            await page.GotoAsync("/Calendar", new() { WaitUntil = WaitUntilState.NetworkIdle });
            Assert.True(await page.EvaluateAsync<bool>(
                "() => fetch('/Calendar/GetEvents').then(r => r.json()).then(e => e.some(x => String(x.id).startsWith('deadline-') && x.editable === false))"));

            // System Health.
            await page.GotoAsync("/SystemHealth", new() { WaitUntil = WaitUntilState.NetworkIdle });
            await Assertions.Expect(page.Locator("body")).ToContainTextAsync("Slowest pages");
            await BrowserApp.ScreenshotAsync(page, "system-health");

            // Filing Deadlines: the list and the edit dialog.
            await page.GotoAsync("/FilingDeadlines", new() { WaitUntil = WaitUntilState.NetworkIdle });
            await page.Locator("button[data-bs-target='#deadlineModal']:has-text('Check')").First.ClickAsync();
            await Assertions.Expect(page.Locator("#dlTitle")).Not.ToHaveValueAsync("");
            await BrowserApp.ScreenshotAsync(page, "filing-deadlines");
            await page.Keyboard.PressAsync("Escape");

            // Dashboard: the countdown.
            await page.GotoAsync("/Dashboard", new() { WaitUntil = WaitUntilState.NetworkIdle });
            await Assertions.Expect(page.Locator("[data-section='deadlines']")).ToContainTextAsync("Upcoming deadlines");
            await page.Locator("[data-section='deadlines']").ScrollIntoViewIfNeededAsync();
            await BrowserApp.ScreenshotAsync(page, "dashboard-deadlines");

            // Error Triage: acknowledging asks why.
            await page.GotoAsync($"/ErrorTriage/ErrorTriageDashboard/{file}", new() { WaitUntil = WaitUntilState.NetworkIdle });
            await page.Locator(".skip-btn").First.EvaluateAsync("el => el.click()");
            await Assertions.Expect(page.Locator("#ackReasonPick")).ToBeVisibleAsync();
            await page.SelectOptionAsync("#ackReasonPick", "Other");
            await page.ClickAsync(".swal2-confirm");
            await Assertions.Expect(page.Locator(".swal2-validation-message")).ToContainTextAsync("Say why");
            await page.FillAsync("#ackReasonText", "Employee works remotely from NJ");
            await BrowserApp.ScreenshotAsync(page, "acknowledge-warning");
            await page.ClickAsync(".swal2-confirm");
            await Assertions.Expect(page.Locator("#ErrorTriageListContainer")).ToContainTextAsync("Employee works remotely from NJ", new() { Timeout = 15_000 });

            await page.Context.CloseAsync();
            Assert.True(errors.Count == 0, "Script errors:\n" + string.Join("\n", errors.Distinct()));
        }
        finally
        {
            TestImports.RemoveAll();
        }
    }

    /// <summary>Through the stages the background jobs run, for a Dummy Data workbook (see ImportPipelineTests).</summary>
    private async Task<int> ValidateAsync(IServiceProvider services, ACA360.BusinessLogic.Services.DummyWorkbook book, TestEmployer employer, TestUser uploader, string label)
    {
        var template = Path.Combine(AcaWebFactory.WebRoot, "wwwroot", "templates", "ACA_General_Import_Template.xlsx");
        Directory.CreateDirectory(TestImports.Folder);
        var path = Path.Combine(TestImports.Folder, $"{TestImports.FilePrefix}{label}.{Guid.NewGuid():N}.xlsx");
        File.WriteAllBytes(path, ACA360.BusinessLogic.Services.DummyDataService.Write(book, template));
        var id = await services.GetRequiredService<ACA360.Repositories.Interfaces.IFileUploadLogService>().CreateLogAsync(new ACA360.Core.Models.UploadedFileLog
        {
            OriginalFileName = Path.GetFileName(path), StoredFileName = Path.GetFileName(path), FilePath = path,
            FileHash = Guid.NewGuid().ToString("N"), EmployerName = employer.Name, TaxID = employer.Ein, ImportTemplateId = 2,
            PlanYear = employer.Year, UploadedByUserId = uploader.UserId.ToString(), UploadedAt = DateTime.UtcNow,
            ValidationStatusId = ACA360.Core.Helpers.VS.Pending, WorkflowStatusId = ACA360.Core.Helpers.WS.PendingAssignment,
        });
        await services.GetRequiredService<ACA360.BusinessLogic.Interfaces.IFileProcessingService>().ProcessFileAsync(id, 2);
        LocalDb.Execute("UPDATE dbo.UploadedFileLog SET AssignedToUserId = @u, WorkflowStatusId = @ws, ValidationStatusId = @vs WHERE FileLogId = @id",
            new { u = uploader.UserId.ToString(), ws = ACA360.Core.Helpers.WS.Processing, vs = ACA360.Core.Helpers.VS.Validating, id });
        await services.GetRequiredService<ACA360.BusinessLogic.Interfaces.IDataProcessingService>().ExtractAndValidateDataAsync(id);
        return id;
    }

    /// <summary>
    /// The screens added with scripts 056-059, in Chrome: the Error Triage Corrections tab, the Import
    /// Analysis "What changed" tab, "Why these codes?" on Code Review, and the Ctrl+K search.
    /// </summary>
    [BrowserFact]
    public async Task TheCorrectionsWhatChangedWhyAndSearchScreensWork()
    {
        const int year = 2025;
        var employer = TestImports.CreateEmployer(year);
        try
        {
            var admin = TestUsers.CreateStaff(RoleIds.SuperAdmin, "new-screens");
            using var scope = _app.Services.CreateScope();
            var services = scope.ServiceProvider;
            ACA360.BusinessLogic.Services.DummyWorkbook Book() => ACA360.BusinessLogic.Services.DummyDataGenerator.Generate(new ACA360.Core.Models.DummyDataRequest
            {
                EmployerIds = { employer.Id }, FilingYear = year, Plans = 1, Banding = "Monthly", Employees = 5,
                DependentsMin = 0, DependentsMax = 0, NewHirePercent = 0, Seed = 59,
            }, new ACA360.Core.Models.DummyEmployer(employer.Id, employer.Name, employer.Ein), new Random(59), new HashSet<string>(), new HashSet<string>());

            // The first file is imported and its codes generated; the second is only validated.
            var first = await ValidateAsync(services, Book(), employer, admin, "screens1");
            await services.GetRequiredService<ACA360.BusinessLogic.Interfaces.IDataProcessingService>().ImportToLiveAsync(first, "Merge", admin.UserId.ToString());
            await services.GetRequiredService<ACA360.BusinessLogic.Interfaces.IACALogicService>().GenerateCodesForEmployerAsync(employer.Id, year);
            var book = Book();
            var rows = book.Rows(ACA360.BusinessLogic.Services.DummyDataGenerator.EmployeeSheet);
            rows[0]["Employee Legal Last Name"] = "O'BRIEN";
            rows[1]["Termination Date"] = ACA360.BusinessLogic.Services.DummyDataGenerator.Date(new DateTime(year, 6, 30));
            rows[1]["Status End Date"] = ACA360.BusinessLogic.Services.DummyDataGenerator.Date(new DateTime(year, 6, 30));
            rows.RemoveAt(4);
            var second = await ValidateAsync(services, book, employer, admin, "screens2");

            var page = await _browser.SignedInPageAsync(admin);
            // A new user's tours start by themselves and their overlay covers the page.
            await page.GotoAsync("/Calendar", new() { WaitUntil = WaitUntilState.NetworkIdle });
            foreach (var tour in new[] { "triage:1", "files:1", "home:1" })
                Assert.Equal(200, await page.EvaluateAsync<int>(
                    @"tour => fetch('/Tour/Seen', { method: 'POST',
                        headers: { 'RequestVerificationToken': document.querySelector('input[name=__RequestVerificationToken]').value,
                                   'Content-Type': 'application/x-www-form-urlencoded' },
                        body: 'tour=' + encodeURIComponent(tour) }).then(r => r.status)", tour));
            var errors = WatchErrors(page);

            // Error Triage: the Corrections tab.
            await page.GotoAsync($"/ErrorTriage/ErrorTriageDashboard/{second}", new() { WaitUntil = WaitUntilState.NetworkIdle });
            await page.ClickAsync("#tabCorrectionsBtn");
            await Assertions.Expect(page.Locator("#CorrectionsContainer")).ToContainTextAsync("OBRIEN", new() { Timeout = 15_000 });
            await BrowserApp.ScreenshotAsync(page, "triage-corrections");

            // Import Analysis: What changed.
            await page.GotoAsync("/FileUpload", new() { WaitUntil = WaitUntilState.NetworkIdle });
            await page.EvaluateAsync("id => window.AcaImportAnalysis.open(id)", second);
            await page.ClickAsync("#ia-tab-Changes");
            var changes = page.Locator("#analysisChangesPanel");
            await Assertions.Expect(changes).ToContainTextAsync("Leaving", new() { Timeout = 20_000 });
            await Assertions.Expect(changes).ToContainTextAsync("not in this file");
            await Assertions.Expect(page.Locator("#analysisGridPanel")).ToBeHiddenAsync();
            await page.Locator(".ia-change-kind[data-kind='TERMINATED']").ClickAsync();
            await Assertions.Expect(changes.Locator("tbody tr")).ToHaveCountAsync(1, new() { Timeout = 15_000 });
            await BrowserApp.ScreenshotAsync(page, "import-what-changed");
            await page.ClickAsync("#ia-tab-Employee");
            await Assertions.Expect(page.Locator("#analysisGridPanel")).ToBeVisibleAsync();
            await page.Keyboard.PressAsync("Escape");

            // Code Review: Why these codes?
            await page.GotoAsync($"/ACA/CodeReview?employerId={employer.Id}&year={year}", new() { WaitUntil = WaitUntilState.NetworkIdle });
            await page.Locator("button:has-text('Why these codes?')").First.ClickAsync(new() { Timeout = 15_000 });
            await Assertions.Expect(page.Locator("#codeExplanationBody")).ToContainTextAsync("December", new() { Timeout = 15_000 });
            await Assertions.Expect(page.Locator("#codeExplanationBody")).ToContainTextAsync("Line 14");
            await BrowserApp.ScreenshotAsync(page, "why-these-codes");
            await page.Keyboard.PressAsync("Escape");

            // Ctrl+K: an employer by name, then Enter opens it.
            await page.GotoAsync("/Dashboard", new() { WaitUntil = WaitUntilState.NetworkIdle });
            await page.Keyboard.PressAsync("Control+k");
            await Assertions.Expect(page.Locator("#globalSearchInput")).ToBeFocusedAsync();
            await page.Locator("#globalSearchInput").PressSequentiallyAsync(employer.Name[..20]);
            await Assertions.Expect(page.Locator("#globalSearchResults")).ToContainTextAsync(employer.Name, new() { Timeout = 15_000 });
            await BrowserApp.ScreenshotAsync(page, "global-search");
            await page.SetViewportSizeAsync(390, 844);
            await BrowserApp.ScreenshotAsync(page, "global-search-phone");

            await page.Context.CloseAsync();
            Assert.True(errors.Count == 0, "Script errors:\n" + string.Join("\n", errors.Distinct()));
        }
        finally
        {
            TestImports.RemoveAll();
        }
    }

    /// <summary>
    /// The third batch's screens, in Chrome: trying a rule, Duplicate SSNs, State Filings, safe harbor
    /// advice, the notification bell and choices, the employer Timeline and employee History tabs, the
    /// Files and Code Review bulk bars, Map columns on a rejected file, and the grid's Columns menu.
    /// </summary>
    [BrowserFact]
    public async Task TheThirdBatchScreensWork()
    {
        // The Employees page lists the year in session, which is this year unless another is chosen.
        var year = DateTime.UtcNow.Year;
        var employer = TestImports.CreateEmployer(year);
        try
        {
            var admin = TestUsers.CreateStaff(RoleIds.SuperAdmin, "batch3-screens");
            using var scope = _app.Services.CreateScope();
            var services = scope.ServiceProvider;
            var book = ACA360.BusinessLogic.Services.DummyDataGenerator.Generate(new ACA360.Core.Models.DummyDataRequest
            {
                EmployerIds = { employer.Id }, FilingYear = year, Plans = 1, Banding = "Monthly", Employees = 5,
                DependentsMin = 0, DependentsMax = 1, NewHirePercent = 0, Seed = 66,
            }, new ACA360.Core.Models.DummyEmployer(employer.Id, employer.Name, employer.Ein), new Random(66), new HashSet<string>(), new HashSet<string>());
            var file = await ValidateAsync(services, book, employer, admin, "batch3");
            await services.GetRequiredService<ACA360.BusinessLogic.Interfaces.IDataProcessingService>().ImportToLiveAsync(file, "Merge", admin.UserId.ToString());
            await services.GetRequiredService<ACA360.BusinessLogic.Interfaces.IACALogicService>().GenerateCodesForEmployerAsync(employer.Id, year);
            var employee = LocalDb.Scalar<int>("SELECT TOP 1 id FROM dbo.Employee WHERE EmployerId = @id ORDER BY id", new { id = employer.Id });
            LocalDb.Execute(@"INSERT INTO dbo.tbl_UserNotifications (UserId, Title, Message, Type, LinkUrl, CreatedDate, IsRead)
                              VALUES (@u, 'File ready to import', '<b>not bold</b> ACA360TESTS file', 'success', '/FileUpload', GETUTCDATE(), 0)", new { u = admin.UserId });

            var page = await _browser.SignedInPageAsync(admin);
            await page.GotoAsync("/Calendar", new() { WaitUntil = WaitUntilState.NetworkIdle });
            foreach (var tour in new[] { "home:1", "files:1", "rules:1", "employers:1", "employees:1", "plans:1" })
                await page.EvaluateAsync<int>(
                    @"tour => fetch('/Tour/Seen', { method: 'POST',
                        headers: { 'RequestVerificationToken': document.querySelector('input[name=__RequestVerificationToken]').value,
                                   'Content-Type': 'application/x-www-form-urlencoded' },
                        body: 'tour=' + encodeURIComponent(tour) }).then(r => r.status)", tour);
            await page.GotoAsync($"/SelectEmployer/SelectThis?id={employer.Id}", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var errors = WatchErrors(page);

            // The bell: the notification is listed as text (not HTML), unread.
            await page.GotoAsync("/Notifications", new() { WaitUntil = WaitUntilState.NetworkIdle });
            await page.ClickAsync("li[data-tour='notifications'] > a");
            await Assertions.Expect(page.Locator("#notification-list")).ToContainTextAsync("<b>not bold</b>", new() { Timeout = 15_000 });
            await Assertions.Expect(page.Locator("#notification-count")).Not.ToHaveTextAsync("0 New");
            await BrowserApp.ScreenshotAsync(page, "notification-bell");
            await page.Keyboard.PressAsync("Escape");
            // Choices: switching one off is saved.
            await page.ClickAsync("[data-bs-target='#notificationPrefs']");
            await page.Locator("#pref_ImportDone").UncheckAsync();
            await Assertions.Expect(page.Locator("[data-pref-status]")).ToHaveTextAsync("Saved.", new() { Timeout = 10_000 });
            await BrowserApp.ScreenshotAsync(page, "notification-choices");

            // Validation Rules: try a rule before saving it.
            await page.GotoAsync("/Rules", new() { WaitUntil = WaitUntilState.NetworkIdle });
            await page.ClickAsync("#add_Rules_controls");
            await Assertions.Expect(page.Locator("#ruleForm")).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await page.SelectOptionAsync("#ruleForm [name='ValidationType']", "REQUIRED");
            await page.SelectOptionAsync("#Rule_TableName", "Staging_Employees");
            await Assertions.Expect(page.Locator("#ColumnName option[value='EmployeeMiddleInitial']")).ToHaveCountAsync(1, new() { Timeout = 10_000 });
            await page.SelectOptionAsync("#ColumnName", "EmployeeMiddleInitial");
            await page.ClickAsync("#ruleWhatIfBtn");
            await Assertions.Expect(page.Locator("[data-what-if]")).ToContainTextAsync("would flag", new() { Timeout = 30_000 });
            await BrowserApp.ScreenshotAsync(page, "rule-what-if");
            await page.Keyboard.PressAsync("Escape");

            // Reports.
            await page.GotoAsync($"/DuplicateSsn?year={year}", new() { WaitUntil = WaitUntilState.NetworkIdle });
            await Assertions.Expect(page.Locator("h4")).ToContainTextAsync("Duplicate SSNs");
            await BrowserApp.ScreenshotAsync(page, "duplicate-ssns");
            await page.GotoAsync($"/StateFilings?year={year}", new() { WaitUntil = WaitUntilState.NetworkIdle });
            await Assertions.Expect(page.Locator("[data-state-card='CA']")).ToBeVisibleAsync();
            var cell = page.Locator("[data-state-open][data-employer='" + employer.Id + "']").First;
            if (await cell.CountAsync() > 0)
            {
                await cell.ClickAsync();
                await Assertions.Expect(page.Locator("#stateResidentsBody table")).ToBeVisibleAsync(new() { Timeout = 15_000 });
                await BrowserApp.ScreenshotAsync(page, "state-filings-residents");
                await page.Keyboard.PressAsync("Escape");
            }
            await BrowserApp.ScreenshotAsync(page, "state-filings");

            // Safe harbor advice, from Penalty Risk.
            await page.GotoAsync($"/ACA/PenaltyRisk?employerId={employer.Id}&year={year}", new() { WaitUntil = WaitUntilState.NetworkIdle });
            await page.ClickAsync("a:has-text('Best safe harbor')");
            await Assertions.Expect(page.Locator("h4")).ToContainTextAsync("Safe Harbor Advice");
            await BrowserApp.ScreenshotAsync(page, "safe-harbor");

            // Code Review: ticking employees shows the bulk bar; locking reloads the grid with the lock.
            await page.GotoAsync($"/ACA/CodeReview?employerId={employer.Id}&year={year}", new() { WaitUntil = WaitUntilState.NetworkIdle });
            await page.Locator("#smartCodeTable input.bulk-row").First.CheckAsync(new() { Timeout = 15_000 });
            await Assertions.Expect(page.Locator("#codeBulkBar")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("#codeBulkBar [data-bulk-count]")).ToHaveTextAsync("1");
            await BrowserApp.ScreenshotAsync(page, "code-review-bulk");
            page.Dialog += (_, d) => d.AcceptAsync();
            await page.ClickAsync("[data-code-bulk='lock']");
            await Assertions.Expect(page.Locator("#smartCodeTable .bxs-lock-alt").First).ToBeVisibleAsync(new() { Timeout = 15_000 });

            // Employer: the Timeline tab.
            await page.GotoAsync("/Employer", new() { WaitUntil = WaitUntilState.NetworkIdle });
            await page.Locator($".edit-emp-btn[data-employer-id='{employer.Id}']").First.ClickAsync(new() { Timeout = 15_000 });
            await page.Locator("[data-timeline-tab]").ClickAsync(new() { Timeout = 15_000 });
            await Assertions.Expect(page.Locator("[data-employer-timeline]")).ToContainTextAsync("File uploaded", new() { Timeout = 15_000 });
            await page.Locator("[data-employer-timeline]").ScrollIntoViewIfNeededAsync();
            await BrowserApp.ScreenshotAsync(page, "employer-timeline");

            // The employer list: bulk bar and the Columns menu, remembered after a reload.
            await page.GotoAsync("/Employer", new() { WaitUntil = WaitUntilState.NetworkIdle });
            await page.Locator("#employerListContainer input.bulk-row").First.CheckAsync();
            await Assertions.Expect(page.Locator("#employerBulkBar")).ToBeVisibleAsync();
            await page.Locator(".grid-columns-wrapper > button").ClickAsync();
            await page.Locator(".grid-column-toggle[data-column-text='City']").UncheckAsync();
            await Assertions.Expect(page.Locator("#employerTable th:has-text('City')")).ToBeHiddenAsync();
            await BrowserApp.ScreenshotAsync(page, "employer-list-bulk-columns");
            await page.WaitForTimeoutAsync(1500);
            await page.ReloadAsync(new() { WaitUntil = WaitUntilState.NetworkIdle });
            await Assertions.Expect(page.Locator("#employerTable th:has-text('City')")).ToBeHiddenAsync(new() { Timeout = 10_000 });
            await page.Locator(".grid-columns-wrapper > button").ClickAsync();
            await page.ClickAsync(".grid-columns-reset");

            // Employee: the History tab.
            await page.GotoAsync($"/Employee?focusId={employee}", new() { WaitUntil = WaitUntilState.NetworkIdle });
            await page.Locator("[data-history-tab]:visible").First.ClickAsync(new() { Timeout = 20_000 });
            // The visible panel, not a copy left by an earlier render.
            await Assertions.Expect(page.Locator("[data-employee-history]:visible")).ToContainTextAsync("Added by an import", new() { Timeout = 15_000 });
            await page.Locator("[data-employee-history]:visible").ScrollIntoViewIfNeededAsync();
            await BrowserApp.ScreenshotAsync(page, "employee-history");

            // Files: Map columns on a file rejected for a header it calls something else, and the bulk bar.
            var path = Path.Combine(TestImports.Folder, $"{TestImports.FilePrefix}batch3-map.{Guid.NewGuid():N}.xlsx");
            File.Copy(LocalDb.Scalar<string>("SELECT FilePath FROM dbo.UploadedFileLog WHERE FileLogId = @file", new { file }), path);
            OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.Commercial;
            using (var package = new OfficeOpenXml.ExcelPackage(new FileInfo(path)))
            {
                var ws = package.Workbook.Worksheets["Employee"];
                for (int c = 1; c <= ws.Dimension.End.Column; c++)
                    if (ws.Cells[1, c].Text.Trim() == "Employee SSN") ws.Cells[1, c].Value = "SSN #";
                package.Save();
            }
            var rejected = await services.GetRequiredService<ACA360.Repositories.Interfaces.IFileUploadLogService>().CreateLogAsync(new ACA360.Core.Models.UploadedFileLog
            {
                OriginalFileName = Path.GetFileName(path), StoredFileName = Path.GetFileName(path), FilePath = path,
                FileHash = Guid.NewGuid().ToString("N"), EmployerName = employer.Name, TaxID = employer.Ein, ImportTemplateId = 2,
                PlanYear = year, UploadedByUserId = admin.UserId.ToString(), UploadedAt = DateTime.UtcNow,
                ValidationStatusId = ACA360.Core.Helpers.VS.Pending, WorkflowStatusId = ACA360.Core.Helpers.WS.PendingAssignment,
            });
            await services.GetRequiredService<ACA360.BusinessLogic.Interfaces.IFileProcessingService>().ProcessFileAsync(rejected, 2);

            await page.GotoAsync("/FileUpload", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var row = page.Locator($"tr[data-fileid='{rejected}']");
            if (!await row.IsVisibleAsync())
                await page.Locator($"[data-group-key^='{employer.Ein}'] [data-bs-toggle='collapse']").First.ClickAsync();
            await row.Locator("button:has-text('Map columns')").ClickAsync(new() { Timeout = 15_000 });
            await Assertions.Expect(page.Locator("#columnMappingForm")).ToContainTextAsync("Employee SSN", new() { Timeout = 15_000 });
            await page.Locator("#columnMappingForm select[data-map-select]").First.SelectOptionAsync("SSN #");
            await BrowserApp.ScreenshotAsync(page, "map-columns");
            await page.ClickAsync("#columnMappingSave");
            await Assertions.Expect(page.Locator("#columnMappingModal")).ToBeHiddenAsync(new() { Timeout = 15_000 });
            Assert.Equal(1, LocalDb.Scalar<int>("SELECT COUNT(*) FROM dbo.EmployerColumnMaps WHERE EmployerEin = @ein", new { ein = employer.Ein.Replace("-", "") }));

            await page.Locator($"tr[data-fileid='{file}'] input.bulk-row").CheckAsync();
            await Assertions.Expect(page.Locator("[data-bulk-bar]")).ToBeVisibleAsync();
            await BrowserApp.ScreenshotAsync(page, "files-bulk");

            // Account Settings: the two-step switch, where you are signed in, and the notification choices.
            await page.GotoAsync("/Settings?tab=security", new() { WaitUntil = WaitUntilState.NetworkIdle });
            await Assertions.Expect(page.Locator("#twoStepSwitch")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("[data-my-sessions]")).ToContainTextAsync("This browser");
            await BrowserApp.ScreenshotAsync(page, "settings-security");
            await page.GotoAsync("/Settings?tab=notifications", new() { WaitUntil = WaitUntilState.NetworkIdle });
            await Assertions.Expect(page.Locator("#notifications-tab [data-event-code='FileReady']")).ToBeVisibleAsync();
            await BrowserApp.ScreenshotAsync(page, "settings-notifications");

            // Phone width: the new pages do not scroll sideways.
            await page.SetViewportSizeAsync(390, 844);
            foreach (var url in new[] { $"/StateFilings?year={year}", $"/ACA/SafeHarbor?employerId={employer.Id}&year={year}", $"/DuplicateSsn?year={year}", "/Settings?tab=security" })
            {
                await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle });
                Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"), url + " scrolls sideways on a phone");
            }
            await BrowserApp.ScreenshotAsync(page, "duplicate-ssns-phone");

            await page.Context.CloseAsync();
            Assert.True(errors.Count == 0, "Script errors:\n" + string.Join("\n", errors.Distinct()));
        }
        finally
        {
            TestImports.RemoveAll();
        }
    }
}
