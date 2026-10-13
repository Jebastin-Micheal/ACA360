using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Models
{
    public class ContactDto
    {
        public int ContactId { get; set; }
        public string? EmployerId { get; set; }
        public string? Name { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public bool IsBilling { get; set; } // True for Billing, False for Regular
    }
}
