using System.ComponentModel.DataAnnotations;

namespace ACA360.Core.Models.Tracker
{
    public class EmployerServiceItem
    {
        public int EmployerServiceId { get; set; }

        // Links to the Employer (Left Pane)
        public int EmployerId { get; set; }
      
        public string? AffiliateName { get; set; } // Populated via SQL Join  

        // Links to the Service Catalog
        [Required(ErrorMessage = "Service selection is required")]
        public int ServiceId { get; set; }

        // Display Property (Populated via SQL Join)
        public string? ServiceName { get; set; }
        public int PlanYear { get; set; }

        // Status & Notes
        public string? Status { get; set; } = "Pending";
        public string? Notes { get; set; }

        // --- 1. Status & Config ---
        public string? PotentialPenalty { get; set; }
        public string? SafeHarbor { get; set; }
        public bool MedcomCOBRA { get; set; }
        public bool CDHPClient { get; set; }
        public bool Required131 { get; set; }
        public bool WillNotSignSA { get; set; }

        // --- 2. Dates ---
        public string? ProposalSentDate { get; set; }
        public string? ProposalSignedDate { get; set; }
        public string? ContractSentDate { get; set; }
        public string? ContractSignedDate { get; set; }

        // --- 3. Process Steps ---
        public string? ProcessStep1095 { get; set; }
        public string? LastUpdated1095 { get; set; }
        public string? NextFollowUp1095 { get; set; }
        public string? SpecialDateDeadline { get; set; }

        public string? FTETrackingStep { get; set; }
        public string? LastUpdatedFTE { get; set; }
        public string? NextFollowUpFTE { get; set; }
        public int? ActualEmployeesTracked { get; set; }

        public string? StateFilingStep { get; set; }
        public string? LastUpdatedState { get; set; }
        public string? NextFollowUpState { get; set; }

        // --- 4. Financials ---
        public string? MonthInitialData { get; set; }
        public string? RenewalDate { get; set; }
        public string? DataSheetReceivedDate { get; set; }
        public string? FollowUpDate { get; set; }
        
        public decimal? Pricing { get; set; }
        public decimal? InitialBill { get; set; }
        public decimal? FinalBill { get; set; }
        public decimal? SOSAmount { get; set; }
        public string? FirstInvoiceDate { get; set; }
        public string? FinalInvoiceDate { get; set; }
        public decimal? AdditionalInvoiceAmount { get; set; }
        public string? Payer { get; set; }
        public string? ExtensionFiled { get; set; }
        public string? AuditBy { get; set; }
        public string? AuditDate { get; set; }
        
        public decimal? ACAOverageBill { get; set; }
        public decimal? FTEOverageBill { get; set; }

        // --- 5. Misc ---
        public int? FormsMailed { get; set; }
        public int? EmployeesTracked { get; set; }
        public string? MostRecentFollowUpdate { get; set; }
        public string? ReceiptID { get; set; }
        public int? AffiliateId { get; set; }
    }
}