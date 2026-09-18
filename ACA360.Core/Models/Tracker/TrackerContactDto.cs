using System;
namespace ACA360.Core.Models.Tracker
{

    public class TrackerContactDto
    {
        public int? ContactId { get; set; }
        public int EmployerId { get; set; }
        public bool IsBilling { get; set; }
        public string Name { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        // Regular contact (IsBilling = false)
        public string AcaUsername { get; set; }
        public string AcaRoles { get; set; }
        // Billing contact (IsBilling = true)
        public string Address1 { get; set; }
        public string Address2 { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string Zip { get; set; }
    }
}