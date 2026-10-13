using System.Text.Json;
using ACA360.Core.Models;

namespace ACA360.Tests.Unit.Helpers;

/// <summary>The JSON setup Program.cs uses: every string upper-cased unless a property opts out.</summary>
public class JsonCaseTests
{
    private static readonly JsonSerializerOptions AppOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new UppercaseStringJsonConverter() },
    };

    private sealed class OrdinaryModel
    {
        public string? Name { get; set; }
    }

    [Fact]
    public void OrdinaryStringsAreStillUpperCased() =>
        Assert.Equal("ACME INC", JsonSerializer.Deserialize<OrdinaryModel>("{\"name\":\"Acme Inc\"}", AppOptions)!.Name);

    [Fact]
    public void CalendarEventTextIsKeptAsTyped()
    {
        var json = """
            {"title":"Team call","description":"Talk about Q4","colorCode":"#ff3e1d",
             "eventUrl":"https://meet.example.com/AbC","location":"Room 2b","guests":"Pat@Example.com",
             "startDate":"2026-10-05T09:00:00","isAllDay":false}
            """;
        var model = JsonSerializer.Deserialize<CalendarEventModel>(json, AppOptions)!;
        Assert.Equal("Team call", model.Title);
        Assert.Equal("Talk about Q4", model.Description);
        Assert.Equal("#ff3e1d", model.ColorCode);
        Assert.Equal("https://meet.example.com/AbC", model.EventUrl);
        Assert.Equal("Room 2b", model.Location);
        Assert.Equal("Pat@Example.com", model.Guests);
    }

    [Fact]
    public void ANullStaysNull() =>
        Assert.Null(JsonSerializer.Deserialize<CalendarEventModel>("{\"title\":\"x\",\"description\":null,\"startDate\":\"2026-10-05T09:00:00\"}", AppOptions)!.Description);
}
