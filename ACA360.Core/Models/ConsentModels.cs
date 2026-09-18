// TODO karthi: NEW FILE for the Consent + Employee-Portal feature (copied in during integration).
using System;
using System.Collections.Generic;

namespace ACA360.Core.Models
{
    /// <summary>
    /// Backs the employee self-service delivery-preference / consent page
    /// (MyForms/DeliveryPreference) and carries the current consent state
    /// plus the audit-trail history for display.
    /// </summary>
    public class EmployeeConsentViewModel
    {
        public int EmployeeId { get; set; }
        public int TaxYear { get; set; }

        // Display fields sourced from the Employee record (for the header + status card).
        public string? EmployeeName { get; set; }
        public string? WorkEmail { get; set; }
        public string? EmployerName { get; set; }
        public string? SsnMasked { get; set; }

        // 'Electronic' | 'Paper'
        public string DeliveryPreference { get; set; } = "Electronic";

        public string? NotificationEmail { get; set; }

        // Populated after a save (e.g. EC-2026-000013); null for paper.
        public string? ConsentId { get; set; }

        // 'Active' | 'Withdrawn' | null (no record yet)
        public string? Status { get; set; }

        public string ConsentMethod { get; set; } = "Employee Portal";

        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }

        public DateTime? CreatedOn { get; set; }
        public DateTime? UpdatedOn { get; set; }

        // Bound from the consent checkbox on the form.
        public bool AgreeToConsent { get; set; }

        // Consent History timeline (newest first).
        public List<ConsentEvent> History { get; set; } = new List<ConsentEvent>();

        // True once the employee has an existing saved record.
        public bool HasRecord => !string.IsNullOrEmpty(Status);

        // Convenience for the status card.
        public bool IsElectronicActive =>
            string.Equals(DeliveryPreference, "Electronic", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Status, "Active", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A single entry in the consent audit trail (EmployeeConsentEvent).
    /// </summary>
    public class ConsentEvent
    {
        public string? EventType { get; set; }
        public string? Detail { get; set; }
        public DateTime EventOn { get; set; }
    }
}
