namespace ACA360.Core.Models
{
    public class AnalysisResultViewModel
    {
        public List<EntityStats> Summary { get; set; } = new List<EntityStats>();
        public List<AnalysisDetailRow> Details { get; set; } = new List<AnalysisDetailRow>();
    }

    public class EntityStats
    {
        public string? EntityType { get; set; } // "Employee", "Plan", etc.
        public int Total { get; set; }
        public int Inserts { get; set; }
        public int Updates { get; set; }
        public int Skips { get; set; }
    }

    public class AnalysisDetailRow
    {
        public string? EntityType { get; set; }
        public string? Identifier { get; set; }    // e.g. "John Doe" or "Gold Plan"
        public string? SubIdentifier { get; set; } // e.g. "SSN"
        public string? ActionType { get; set; }
        public string? ChangesJson { get; set; }
    }
}