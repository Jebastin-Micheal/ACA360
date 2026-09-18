namespace ACA360.Core.Models
{
    public class PremiumUploadModel
    {
        public string? PrimaryEIN { get; set; }
        public string? PlanName { get; set; }
        public string? BandingType { get; set; }
        public int? Start { get; set; } // Assuming 'Start' is an integer (e.g., number of employees)
        public int? End { get; set; } // Assuming 'End' is an integer
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public decimal? EEMonthlyContribution { get; set; } // EE Monthly Contribution
    }
}