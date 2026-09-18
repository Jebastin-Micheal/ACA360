using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Models
{
    public class AuditLog
    {
        public string? UserName { get; set; }
        public string? Operation { get; set; }
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
        public DateTime? Date { get; set; }
        public String? UserId { get; set; }
    }
}
