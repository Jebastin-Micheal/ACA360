
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Models
{
    public class Process_steps
    {
        public string? ProcessId { get; set; }
        public string? Category { get; set; }
        public string? ProcessName { get; set; }
        public int? ProcessDeleted { get; set; }
        public int? FollowUpDays { get; set; }
        public int? DisplayOrder { get; set; }
        public string WebName { get; set; }
        public bool IsDoNotDisplay { get; set; }
    }   
}
