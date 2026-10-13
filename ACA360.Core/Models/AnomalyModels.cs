using System.Collections.Generic;

namespace ACA360.Core.Models
{
    public class DataHealthScorecard
    {
        public int EmployerId { get; set; }
        public int HealthScore { get; set; } // 0 - 100
        public string? Rating { get; set; }   // "Excellent", "Good", "Risky", "Critical"
        public List<AnomalyAlert> Alerts { get; set; } = new List<AnomalyAlert>();
    }

    public class AnomalyAlert
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Severity { get; set; } // "High", "Medium", "Low"
        public int AffectedRecordCount { get; set; }
        public string? Suggestion { get; set; }
    }
}