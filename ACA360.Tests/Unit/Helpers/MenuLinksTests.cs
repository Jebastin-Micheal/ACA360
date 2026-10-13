using ACA360.Core.Models;
using ACA360.Web.Helpers;

namespace ACA360.Tests.Unit.Helpers;

public class MenuLinksTests
{
    [Theory]
    [InlineData("Employee", "Employees", "Index")]
    [InlineData("Partner", "Firm Management", "Index")]
    [InlineData("Partner", "Broker Management", "Brokers")]
    [InlineData(" Partner ", "broker management", "Brokers")]
    [InlineData("Tracker", "Broker Report", "Index")]   // only the Partner page has a broker view
    [InlineData(null, null, "Index")]
    public void EachEntryOpensItsPage(string? controller, string? name, string expected) =>
        Assert.Equal(expected, MenuLinks.ActionFor(controller, name));

    private static readonly List<Menu> Menus = new()
    {
        new() { Id = 20, MenuName = "Master Settings", Controller = "Rules" },
        new() { Id = 21, MenuName = "Validation Rules", Controller = "Rules", ParentMenuId = 20 },
        new() { Id = 1028, MenuName = "Tracker", Controller = "Tracker" },
        new() { Id = 1031, MenuName = "Firm Management", Controller = "Partner", ParentMenuId = 1028 },
        new() { Id = 1051, MenuName = "Broker Management", Controller = "Partner", ParentMenuId = 1028 },
        new() { Id = 7, MenuName = "Employees", Controller = "Employee" },
    };

    [Theory]
    [InlineData("Partner", "Index", 1031)]
    [InlineData("Partner", "Brokers", 1051)]
    [InlineData("Partner", "GetFirmGrid", 1031)]   // any other action: the first entry for the controller
    [InlineData("partner", "brokers", 1051)]
    [InlineData("Rules", "Index", 21)]             // the entry, not its parent with the same controller
    [InlineData("Employee", "Details", 7)]
    [InlineData("Calendar", "Index", null)]
    [InlineData(null, null, null)]
    public void TheSidebarHighlightsTheEntryForThePage(string? controller, string? action, int? expected) =>
        Assert.Equal(expected, MenuLinks.ActiveMenuId(Menus, controller, action));
}
