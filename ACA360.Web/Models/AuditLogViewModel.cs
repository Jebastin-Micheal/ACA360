using ACA360.Core.Models;

namespace ACA360.Web.Models
{
    public class AuditLogViewModel
    {
        public int EmployerId { get; set; }
        public List<AuditLog> Entries { get; set; } = new List<AuditLog>();
    }
}
