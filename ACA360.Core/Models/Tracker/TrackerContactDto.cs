using System;
namespace ACA360.Core.Models.Tracker
{

    public class TrackerContactDto
    {
        public int? ContactId { get; set; }
        public int EmployerId { get; set; }
        public bool IsBilling { get; set; }
        public string Name { get; set; } = null!;
        public string Phone { get; set; } = null!;
        public string Email { get; set; } = null!;
        // Regular contact (IsBilling = false)
        public string AcaUsername { get; set; } = null!;
        public string AcaRoles { get; set; } = null!;
        // Billing contact (IsBilling = true)
        public string Address1 { get; set; } = null!;
        public string Address2 { get; set; } = null!;
        public string City { get; set; } = null!;
        public string State { get; set; } = null!;
        public string Zip { get; set; } = null!;
    }
}