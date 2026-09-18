using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations.Schema;

namespace ACA360.Core.Models
{
    public class RecentConsentActivity
    {
        public int EmployeeId { get; set; }
        public string? EmployeeName { get; set; }

        // Raw values straight from EmployeeConsentEvent
        public string? EventType { get; set; }   // Preference Selected / Consent Provided / Consent Withdrawn / Disclosure Viewed
        public string? Detail { get; set; }
        public DateTime? ActionDate { get; set; }

        [NotMapped]
        public string DisplayText => EventType switch
        {
            "Consent Provided" => "Consented to electronic delivery",
            "Consent Withdrawn" => "Switched to paper delivery",
            "Disclosure Viewed" => "Viewed electronic disclosure",
            "Preference Selected" => Detail ?? "Updated delivery preference",
            _ => Detail ?? "Updated consent"
        };

        [NotMapped]
        public string BadgeIcon => EventType switch
        {
            "Consent Provided" => "bx-check-circle text-success",
            "Consent Withdrawn" => "bx-envelope text-warning",
            _ => "bx-user text-secondary"
        };
    }
}
