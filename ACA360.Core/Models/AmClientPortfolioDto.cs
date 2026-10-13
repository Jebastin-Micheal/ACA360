namespace ACA360.Core.Models
{
    public class AmClientPortfolioDto
    {
        public int EmployerId { get; set; }
        public string? EmployerName { get; set; }
        public string? TaxId { get; set; }
        public DateTime? LastUploadDate { get; set; }
        // Simple status logic: 1 = Good, 2 = Warning (Missing Data), 3 = At Risk
        public int ComplianceStatusId { get; set; }
        public string? ContactPerson { get; set; }
        public string? ContactEmail { get; set; }
    }
}