namespace ACA360.Core.Models
{
    public class EmployerUploadModel
    {
        public string? PrimaryEIN { get; set; }
        public string? AffiliatedEIN { get; set; }
        public string? EmployerName { get; set; }
        public bool? ForeignAddressInd { get; set; }
        public string? Address { get; set; }
        public string? Address2 { get; set; }
        public string? City { get; set; }
        public string? StateProvince { get; set; }
        public string? Zip { get; set; }
        public string? Country { get; set; }
        public string? Phone { get; set; }
        public string? Contact { get; set; }
        public string? Title { get; set; }
        public string? OriginCode { get; set; }
        public string? SHOPIdentifier { get; set; }
        public string? Notes { get; set; }
    }
}