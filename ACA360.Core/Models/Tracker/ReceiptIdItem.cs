using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Models.Tracker
{
    public class ReceiptIdItem
    {
        public int ReceiptEntryId { get; set; }
        public int EmployerId { get; set; }
        public int AffiliateEINId { get; set; }
        public int EmployerServiceId { get; set; }
        public int? ServiceId { get; set; }
        public string? ReceiptIdValue { get; set; }
        public DateTime? ReceivedDate { get; set; }
        public string? PlanYear { get; set; }
        public string? Notes { get; set; }
        public DateTime? CreatedDate { get; set; }
        public int? CreatedBy { get; set; }

        // Joined from Employer table for display
        public string? AffiliateName { get; set; }
        public string? AffiliateEIN { get; set; }
        public bool IsPrimary { get; set; }
    }
}
