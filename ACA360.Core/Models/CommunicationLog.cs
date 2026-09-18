using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Models
{
    public class CommunicationLog
    {
        public int LogId { get; set; }
        public string? EmployerEIN { get; set; }
        public int? AssociatedFileLogId { get; set; }
        public long UserId { get; set; }
        public DateTime LogDate { get; set; }
        public string? CommunicationType { get; set; }
        public string? Subject { get; set; }
        public string? Body { get; set; }
        public string? FilePath { get; set; }
    }
}
