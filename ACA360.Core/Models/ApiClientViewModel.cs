using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Models
{
    public class ApiClient
    {
        public int EmployerId { get; set; }
        public string? Name { get; set; }
        public bool IsActive { get; set; }
    }
    public class ApiClientViewModel
    {
        public Guid ClientId { get; set; }
        public string? Name { get; set; }
        public DateTime CreatedOn { get; set; }
        public DateTime? LastUsedOn { get; set; }
        public bool IsActive { get; set; }
    }

    public class NewKeyResult
    {
        public Guid ClientId { get; set; }
        public string? ClientSecret { get; set; }
        public string? Name { get; set; }
    }
}
