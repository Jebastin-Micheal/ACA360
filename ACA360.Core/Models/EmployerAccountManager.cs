using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Models
{
    public class EmployerAccountManager
    {
        public int AssignmentId { get; set; }
        public string? EmployerEIN { get; set; }
        public long AccountManagerUserId { get; set; }
        public DateTime AssignedDate { get; set; }
        public bool IsActive { get; set; }
    }
}
