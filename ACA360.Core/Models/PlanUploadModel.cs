namespace ACA360.Core.Models
{
    public class PlanUploadModel
    {
        public string? PrimaryEIN { get; set; }
        public string? PlanName { get; set; }
        public string? PlanType { get; set; }
        public bool? OfferedToSpouse { get; set; }
        public bool? OfferedToDependents { get; set; }
        public int? WaitingPeriodDays { get; set; }
        public bool? EligibleFirstOfMonth { get; set; }
        public string? FundingType { get; set; }
        public string? PlanRenewalMonth { get; set; }
        public bool? TerminatesOnDateOfTermination { get; set; }
        public string? MinimumValue { get; set; }
        public string? BandingType { get; set; }
        public decimal? PremiumCap { get; set; }
    }
}