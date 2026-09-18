namespace ACA360.Web.Models
{
    public class ContactViewModel
    {
        public string? EmployerId { get; set; } // String as per your requirement
        public string? ContactId { get; set; } // String as per your requirement
        public string? Name { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public bool IsBilling { get; set; }
    }
}
