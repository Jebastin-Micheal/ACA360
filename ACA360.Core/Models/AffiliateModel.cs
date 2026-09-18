using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Models
{
    public class AffiliateDto
    {
        public int EmployerId { get; set; }
        public int AffiliateId { get; set; }
        public int ParentEmployerId { get; set; }
        public string AffiliateName { get; set; } = string.Empty;
        public string EIN { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string Zip { get; set; } = string.Empty;
        public int NumberOfEmployees { get; set; }
        public int FilingYear { get; set; }
    }
}