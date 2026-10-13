using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ACA360.Tests.Integration.Infrastructure;

namespace ACA360.Tests.Integration;

/// <summary>
/// The pages that post JSON or AJAX send the layout's antiforgery token in the
/// RequestVerificationToken header (Calendar, the grids' view mode, Plan). These requests are
/// built exactly as those pages build them: with the header they work, without it they are refused.
/// </summary>
[Collection(AppCollection.Name)]
public class AntiforgeryFlowTests
{
    private readonly AcaWebFactory _app;

    public AntiforgeryFlowTests(AcaWebFactory app) => _app = app;

    private static async Task<string> LayoutToken(AcaClient client) =>
        AcaClient.Token(await (await client.GetAsync("/Calendar/Index")).Content.ReadAsStringAsync());

    private static HttpRequestMessage Post(string url, HttpContent? content, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        if (token != null) request.Headers.Add("RequestVerificationToken", token);
        return request;
    }

    private static JsonContent Event(int id = 0) => JsonContent.Create(new
    {
        id, title = "Test event", startDate = DateTime.Today.AddHours(9), endDate = DateTime.Today.AddHours(10),
        isAllDay = false, colorCode = "primary",
    });

    [LocalDbFact]
    public async Task TheCalendarAddsUpdatesAndDeletesWithTheToken()
    {
        var user = TestUsers.CreateStaff(RoleIds.Admin, "calendar");
        using var client = await _app.SignedInAs(user);
        var token = await LayoutToken(client);

        var added = await client.Http.SendAsync(Post("/Calendar/AddEvent", Event(), token));
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var json = JsonDocument.Parse(await added.Content.ReadAsStringAsync()).RootElement;
        Assert.True(json.GetProperty("success").GetBoolean());
        var id = json.GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.OK, (await client.Http.SendAsync(Post("/Calendar/UpdateEvent", Event(id), token))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.Http.SendAsync(Post($"/Calendar/DeleteEvent?id={id}", null, token))).StatusCode);
        Assert.Equal(0, LocalDb.Scalar<int>("SELECT COUNT(*) FROM dbo.tbl_CalendarEvents WHERE UserId = @u", new { u = user.UserId }));
    }

    [LocalDbTheory]
    [InlineData("/Calendar/AddEvent")]
    [InlineData("/Calendar/UpdateEvent")]
    [InlineData("/Calendar/DeleteEvent?id=1")]
    [InlineData("/Plan/PlanSave")]
    public async Task WithoutTheTokenChangesAreRefused(string url)
    {
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.Admin, "nocsrf"));
        var response = await client.Http.SendAsync(Post(url, Event(), token: null));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [LocalDbFact]
    public async Task TheGridViewModeIsSavedWithTheTokenOnly()
    {
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.Admin, "viewmode"));
        FormUrlEncodedContent Form() => new(new Dictionary<string, string> { ["moduleKey"] = "employees", ["viewMode"] = "card" });

        Assert.Equal(HttpStatusCode.BadRequest, (await client.Http.SendAsync(Post("/UserPreference/SaveViewMode", Form(), null))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.Http.SendAsync(Post("/UserPreference/SaveViewMode", Form(), await LayoutToken(client)))).StatusCode);
    }

    [LocalDbFact]
    public async Task ThePagesCarryTheTokenTheirScriptsRead()
    {
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.Admin, "pages-token"));
        var calendar = await (await client.GetAsync("/Calendar/Index")).Content.ReadAsStringAsync();
        Assert.Contains("name=\"__RequestVerificationToken\"", calendar);
        Assert.Contains("'RequestVerificationToken': antiForgeryToken()", calendar);

        var grid = await (await client.GetAsync("/js/common-grid.js")).Content.ReadAsStringAsync();
        Assert.Contains("'RequestVerificationToken'", grid);
    }
}
