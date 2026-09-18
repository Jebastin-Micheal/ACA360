

namespace ACA360.Web.Models
{
    // This class lives in WEB and is used only for the UI/Controller
    public class AffiliateViewModel    {
   
        public string? EmployerId { get; set; }   
        public string? Name { get; set; }
        public string? Ein { get; set; }
        public string? Address { get; set; }
        public string? Phone { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? Zip { get; set; }
        public int NumberOfEmployees { get; set; }
        public int AffiliateId { get; set; }
        public int FilingYear { get; set; }
        public int ParentEmployerId { get; set; }
       public string AffiliateName { get; set; } 
       public string EIN { get; set; } 
        public string Email { get; set; }

    }
}