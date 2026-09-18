namespace ACA360.Core.Models
{
    public class DependentUploadModel
    {
        public string? PrimaryEIN { get; set; }
        public string? EINAssociatedWithEE { get; set; }
        public string? EmployeeSSN { get; set; }
        public string? DependentLegalFirstName { get; set; }
        public string? DependentMiddleInitial { get; set; }
        public string? DependentLegalLastName { get; set; }
        public string? DependentSuffix { get; set; }
        public string? DependentSSN { get; set; }
        public DateTime? DependentBirthdate { get; set; }
        public DateTime? DependentCoverageStartDate { get; set; }
        public DateTime? DependentCoverageEndDate { get; set; }
        public string? Relationship { get; set; }
    }
}