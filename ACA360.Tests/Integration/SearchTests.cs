using System.Text.Json;
using ACA360.Tests.Integration.Infrastructure;

namespace ACA360.Tests.Integration;

/// <summary>Global search (Ctrl+K, script 059): each source shows only what its own page would.</summary>
[Collection(AppCollection.Name)]
public class SearchTests : IDisposable
{
    private const int Year = 2025;
    private readonly AcaWebFactory _app;

    public SearchTests(AcaWebFactory app) => _app = app;

    public void Dispose()
    {
        TestEmployees.RemoveAll();
        TestImports.RemoveAll();
    }

    private static List<string> Titles(JsonElement result, string group) =>
        result.GetProperty(group).EnumerateArray().Select(h => h.GetProperty("title").GetString()!).ToList();

    [LocalDbFact]
    public async Task SearchFindsPagesEmployersAndTheSelectedEmployersEmployees()
    {
        var employer = TestImports.CreateEmployer(Year);
        var person = TestEmployees.Create(employer.Id, Year, "SEARCHABLE");
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.SuperAdmin, "search"));

        // Pages come from the user's own menu, with their plain addresses.
        var pages = await client.JsonAsync("/Search/Query?q=dummy");
        Assert.Contains("Dummy Data", Titles(pages, "pages"));
        Assert.Contains(pages.GetProperty("pages").EnumerateArray(), h => h.GetProperty("url").GetString() == "/DummyData");

        // An employer by name and by EIN digits; choosing it selects it.
        var byName = await client.JsonAsync("/Search/Query?q=" + Uri.EscapeDataString(employer.Name));
        Assert.Contains(employer.Name, Titles(byName, "employers"));
        var byEin = await client.JsonAsync("/Search/Query?q=" + new string(employer.Ein.Where(char.IsDigit).ToArray()));
        var hit = byEin.GetProperty("employers").EnumerateArray().Single(h => h.GetProperty("title").GetString() == employer.Name);
        Assert.Contains("/SelectEmployer/SelectThis", hit.GetProperty("url").GetString());

        // Employees come from the employer and year being worked in.
        var selected = await client.PostFormAsync("/SelectEmployer/SetEmployer", "/Calendar/Index",
            new Dictionary<string, string> { ["employerId"] = employer.Id.ToString(), ["year"] = Year.ToString() }, ajax: true);
        Assert.True(selected.IsSuccessStatusCode);
        var byLast4 = await client.JsonAsync("/Search/Query?q=" + person.Last4);
        var employee = byLast4.GetProperty("employees").EnumerateArray().Single(h => h.GetProperty("title").GetString()!.Contains("SEARCHABLE"));
        Assert.Contains("···" + person.Last4, employee.GetProperty("subtitle").GetString());
        Assert.DoesNotContain(person.Ssn, byLast4.GetRawText());
        Assert.EndsWith($"focusId={person.Id}", employee.GetProperty("url").GetString());
        Assert.Contains("SEARCHABLE " + TestEmployees.LastName, Titles(await client.JsonAsync("/Search/Query?q=searchable"), "employees"));
    }

    [LocalDbFact]
    public async Task SearchShowsNothingOutsideTheUsersScope()
    {
        var employer = TestImports.CreateEmployer(Year);
        using var client = await _app.SignedInAs(TestUsers.CreateStaff(RoleIds.AccountManager, "search-out"));

        var result = await client.JsonAsync("/Search/Query?q=" + Uri.EscapeDataString(employer.Name));
        Assert.Empty(result.GetProperty("employers").EnumerateArray());
        Assert.Empty(result.GetProperty("employees").EnumerateArray());
        Assert.Equal("Select an employer to search its employees.", result.GetProperty("employeeNote").GetString());
        // Too short: nothing is searched.
        Assert.Empty((await client.JsonAsync("/Search/Query?q=a")).GetProperty("pages").EnumerateArray());
    }
}
