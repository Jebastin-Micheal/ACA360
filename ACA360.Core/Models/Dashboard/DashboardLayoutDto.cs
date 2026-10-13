using System.Collections.Generic;

namespace ACA360.Core.Models.Dashboard
{
    /// <summary>
    /// Purpose: Shape of a user's saved dashboard layout - which widget keys are hidden,
    /// and the display order of whatever's left visible. Serialized as JSON into the
    /// UserDashboardLayout.WidgetOrder column (that column name was kept as-is to avoid
    /// a migration; it now holds this whole object, not just a bare order array).
    /// </summary>
    public class DashboardLayoutDto
    {
        public List<string> Order { get; set; } = new List<string>();
        public List<string> Hidden { get; set; } = new List<string>();
    }
}
