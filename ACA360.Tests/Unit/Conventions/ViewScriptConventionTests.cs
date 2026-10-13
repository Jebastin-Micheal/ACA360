using System.Text.RegularExpressions;
using ACA360.Tests.Integration.Infrastructure;

namespace ACA360.Tests.Unit.Conventions;

/// <summary>
/// jQuery is loaded once, by the layouts. A view or partial that loads it again replaces the
/// page's copy and drops its plugins (input masks, select2, DataTables...): the Employee page's
/// blank detail panel broke input masks that way until the page was reloaded.
/// </summary>
public class ViewScriptConventionTests
{
    private static readonly string[] Layouts =
    {
        Path.Combine("Views", "Shared", "Components", "_Scripts.cshtml"),
        Path.Combine("Views", "Shared", "_EmployeeLayout.cshtml"),
        Path.Combine("Areas", "Portal", "Views", "Shared", "_PortalLayout.cshtml"),
    };

    [Fact]
    public void OnlyTheLayoutsLoadJQuery()
    {
        var jquery = new Regex(@"<script[^>]+src=[^>]*jquery(\.min)?(-[\d.]+)?\.js", RegexOptions.IgnoreCase);
        var offenders = Directory.GetFiles(AcaWebFactory.WebRoot, "*.cshtml", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(AcaWebFactory.WebRoot, f))
            .Where(f => !f.StartsWith("bin" + Path.DirectorySeparatorChar) && !f.StartsWith("obj" + Path.DirectorySeparatorChar))
            .Where(f => !Layouts.Contains(f))
            .Where(f => jquery.IsMatch(File.ReadAllText(Path.Combine(AcaWebFactory.WebRoot, f))))
            .ToList();
        Assert.True(offenders.Count == 0, "These views load jQuery again:\n" + string.Join("\n", offenders));
    }
}
