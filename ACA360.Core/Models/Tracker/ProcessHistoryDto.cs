using System;

namespace ACA360.Core.Models.Tracker
{
    public class ProcessHistoryDto
    {
        public DateTime CreatedDate { get; set; }
        public string UpdatedBy { get; set; }
        public string ProcessName { get; set; }
        public DateTime? NextFollowUpDate { get; set; }
    }
}