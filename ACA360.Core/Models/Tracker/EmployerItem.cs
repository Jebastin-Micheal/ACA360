using System;
using System.ComponentModel.DataAnnotations;

namespace ACA360.Core.Models.Tracker
{
    public class TrackerEmployerItem
    {
        public int EmployerId { get; set; }
        public string? Filingyear { get; set; }

        // --- Row 1 ---
        [Required(ErrorMessage = "Employer Name is required")]
        public string? EmployerName { get; set; }
        public string? EIN { get; set; }
        public string? FirmName { get; set; } // For Display
        public string? BrokerName { get; set; } // For Display
        public string? BrokerContactName { get; set; } // For Display
        public string? IndustryName { get; set; } // For Display
        public string? DataTypeName { get; set; } // For Display

        // --- Row 2 ---
        public string? Address { get; set; }
        public string? Address2 { get; set; }
        public string? City { get; set; }
        public string? Zip { get; set; }

        // --- Row 3 ---
        public string? State { get; set; }
        public int StateId { get; set; }
        public string? PlanYear { get; set; }

        // --- Row 4 ---
        public string? ContactName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }

        // --- Row 5 ---
        public int? BrokerId { get; set; }
        public int? AcctManagerId { get; set; }
        public string? AccountManagerName { get; set; }
        public string? FundingType { get; set; }

        // --- Row 6 ---
        public int? SalesRepId { get; set; }
        public string? FormsProposed { get; set; } // New
        public string? Form1095Process { get; set; } // New

        // --- Row 7 ---
        public string? Vendor { get; set; }

        public string? DataAnalyst { get; set; } // New
        public string? FormType { get; set; }

        // --- Row 8 ---
        public string? WaitingPeriod { get; set; }
        public string? BandingType { get; set; }
        public string? OverProposedFormCount { get; set; } // "20% Over..."

        // --- Row 9 ---
        public string? DataFrequency { get; set; }
        public string? LevelOfComplexity { get; set; }
        public string? DataLoads { get; set; }

        // --- Row 10 ---
        public decimal? OverProposedFormPrice { get; set; }
        public int? IndustryId { get; set; }
        public string? BrokerInvolvement { get; set; }

        // --- Row 11 ---
        public string? Communication { get; set; }
        public string? OverProposedTrackedCount { get; set; }
        public decimal? OverProposedTrackedPrice { get; set; }

        // --- Row 12 ---
        public decimal? LateTerminationFee { get; set; }
        public int? DataTypeId { get; set; }
        public string? PlanTermination { get; set; }

        // --- Bottom Checkboxes ---
        public bool ConditionalOffer { get; set; }
        public bool IsPriority { get; set; }
        public bool SFTP { get; set; }
        public bool HasSpecialMailing { get; set; }
        public bool SpecialPaymentDates { get; set; }
        public bool AnnualPO { get; set; }
        public bool AnnualCOI { get; set; }
        public bool NoAutoRenewal { get; set; }
        public bool CannotRenewal { get; set; } // New

        public int TotalCount { get; set; }

        public int? AffiliateId { get; set; } // New
        public string? LastContactDate { get; set; } // "Date Account Manager Most Recently Contacted User"

        // --- Group 2: Broker Information ---
        public int? BrokerContactId { get; set; } // New
        public string? BrokerLastContactDate { get; set; } // New
        public string? BrokerPhone { get; set; } // New
        public string? BrokerEmail { get; set; } // New

        // --- Group 3: Contact / Roles / Billing ---
        public int? ACAContactId { get; set; } // "Contact" (Dropdown)
        public string? ACAContactName { get; set; }

        public string? ACAContactLastDate { get; set; } // New
        public string? ACAContactPhone { get; set; }
        public string? ConnectUser { get; set; } // New
        public string? ACARole { get; set; } // "ACA Roles" (Dropdown)
        public string? ACAContactEmail { get; set; }

        public int? BillingContactId { get; set; } // New
        public string? BillingPhone { get; set; } // New
        public string? BillingEmail { get; set; } // New

        // --- Group 4: Form & Funding ---
        public int? NumberOf1095Cs { get; set; } // New
       // public int? FormsProposed { get; set; } // "Number of Forms Proposed"

        // --- Group 5: Team ---
        public int? DataAnalystId { get; set; } // Changed from string to ID for Dropdown

        // --- Group 9: Notes ---
        public string? Notes { get; set; }

        // --- AFFILIATE LINK ---
        // Read-Only Details (Populated via JOIN)
        public string? AffiliateName { get; set; }
        public string? AffiliateEIN { get; set; }
        public int? AffiliateEmpCount { get; set; }
        public string? AffiliateAddress { get; set; }

        // --- BILLING CONTACT LINK ---

        // Read-Only Details (Populated via JOIN)
        public string? BillingName { get; set; }
        public string? BillingAddress { get; set; }
        public string? BillingAddress2 { get; set; } // Secondary Address
        public string? FirmId { get; set; }
        // --- Portal Login (client portal credentials) ---
        
        public bool CreatePortalLogin { get; set; }
        
        public bool HasPortalLogin { get; set; }
        
        public bool ChangePortalPassword { get; set; }
        public string? PortalUserName { get; set; }
        public string? PortalPassword { get; set; }
    }

    public class EmployerFilterModel
    {
        // ── Search ───────────────────────────────────────────────────────
        public string SearchText { get; set; }
        // ── Employer (Main → Affiliate cascade) ──────────────────────────
        public int? MainEmployerId { get; set; }   // ← ADD
        public int? AffiliateEmployerId { get; set; }   // ← ADD
                                                        // ── Organization ─────────────────────────────────────────────────
        public int? FirmId { get; set; }
        public int? BrokerId { get; set; }
        public string State { get; set; }
        public string FilingYear { get; set; }

        // ── Team ─────────────────────────────────────────────────────────
        public int? AcctManagerId { get; set; }
        public int? DataAnalystId { get; set; }
        public int? SalesRepId { get; set; }

        // ── Configuration ────────────────────────────────────────────────
        public string FundingType { get; set; }
        public string FormType { get; set; }
        public string LevelOfComplexity { get; set; }
        public int? IndustryId { get; set; }

        // ── Flags ────────────────────────────────────────────────────────
        public bool? IsPriority { get; set; }
        public bool? NoAutoRenewal { get; set; }
        public bool? SFTP { get; set; }
        public bool? AnnualCOI { get; set; }

        // ── Pagination & sorting ─────────────────────────────────────────
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 15;
        public string SortColumn { get; set; } = "EmployerName";
        public string SortOrder { get; set; } = "ASC";

    }



    // ── AffiliateEmployerItem DTO ─────────────────────────────────────
    // Add this class to ACA360.Core.Models.Tracker namespace
    // (in TrackerEmployerItem.cs or a new AffiliateEmployerItem.cs)
    // ─────────────────────────────────────────────────────────────────

    public class AffiliateEmployerItem
    {
        public int EmployerId { get; set; }  // Employer.id
        public int AffiliateId { get; set; }  // alias of EmployerId
        public int? ParentEmployerId { get; set; }  // Employer.companyId (null = primary)
        public string? AffiliateName { get; set; }  // Employer.name
        public string? EIN { get; set; }  // Employer.taxid
        public string? Address { get; set; }
        public string? Address2 { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? Zip { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public bool IsMain { get; set; }  // true = Primary Employer
        public string? Filingyear { get; set; }
    }
    public class EmployerNoteItem
    {
        public int EmployerNoteId { get; set; }  // tbl_Medtracker_Note.noteid
        public int EmployerId { get; set; }  // tbl_Medtracker_Note.employerid
        public string? Category { get; set; }  // tbl_Medtracker_Note._category
        public string? NoteText { get; set; }  // tbl_Medtracker_Note._xnote
        public int NoteYear { get; set; }  // tbl_Medtracker_Note._PlanYear
        public string? CreatedDate { get; set; }  // tbl_Medtracker_Note._timestamp
        public int? CreatedByUserId { get; set; }  // tbl_Medtracker_Note._inputUser
        public string? CreatedByName { get; set; }  // resolved from StaffAccounts join
    }

}