using System;

namespace ACA360.Core.Models
{
    // 1. THE DATABASE ENTITY (Matches dbo.EmployeeCode table structure)
    public class EmployeeCode
    {
        // IRS 226J Defense
        public bool IsAtPenaltyRisk { get; set; }
        public int PenaltyMonths { get; set; }
        public decimal? EstimatedPenalty { get; set; }
        public string? DefenseSummary { get; set; }
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public int EmployeeCodeID { get; set; }
        public int EmployerId { get; set; }
        public int FilingYear { get; set; }

        // Control Flags
        public int GetForm { get; set; } = 1; // Default to 1 (Yes)
        public int IsCorrected { get; set; } = 0;
        public int IsVoid { get; set; } = 0;
        public int DisableCoding { get; set; } = 0;

        // --- Line 14: Offer of Coverage Codes (COC) ---
        public string? ALLM_COC { get; set; }
        public string? JAN_COC { get; set; }
        public string? FEB_COC { get; set; }
        public string? MAR_COC { get; set; }
        public string? APR_COC { get; set; }
        public string? MAY_COC { get; set; }
        public string? JUN_COC { get; set; }
        public string? JUL_COC { get; set; }
        public string? AUG_COC { get; set; }
        public string? SEP_COC { get; set; }
        public string? OCT_COC { get; set; }
        public string? NOV_COC { get; set; }
        public string? DEC_COC { get; set; }

        // --- Line 15: Employee Required Contribution (LCMP) ---
        // Note: Defined as 'money' in SQL, so we use 'decimal?' in C#
        public decimal? ALLM_LCMP { get; set; }
        public decimal? JAN_LCMP { get; set; }
        public decimal? FEB_LCMP { get; set; }
        public decimal? MAR_LCMP { get; set; }
        public decimal? APR_LCMP { get; set; }
        public decimal? MAY_LCMP { get; set; }
        public decimal? JUN_LCMP { get; set; }
        public decimal? JUL_LCMP { get; set; }
        public decimal? AUG_LCMP { get; set; }
        public decimal? SEP_LCMP { get; set; }
        public decimal? OCT_LCMP { get; set; }
        public decimal? NOV_LCMP { get; set; }
        public decimal? DEC_LCMP { get; set; }

        // --- Line 16: Safe Harbor Codes (SHC) ---
        public string? ALLM_SHC { get; set; }
        public string? JAN_SHC { get; set; }
        public string? FEB_SHC { get; set; }
        public string? MAR_SHC { get; set; }
        public string? APR_SHC { get; set; }
        public string? MAY_SHC { get; set; }
        public string? JUN_SHC { get; set; }
        public string? JUL_SHC { get; set; }
        public string? AUG_SHC { get; set; }
        public string? SEP_SHC { get; set; }
        public string? OCT_SHC { get; set; }
        public string? NOV_SHC { get; set; }
        public string? DEC_SHC { get; set; }

        // --- Line 17: ICHRA ZIP codes (residence or work-site) ---
        public string? ALLM_ZIP { get; set; }
        public string? JAN_ZIP { get; set; }
        public string? FEB_ZIP { get; set; }
        public string? MAR_ZIP { get; set; }
        public string? APR_ZIP { get; set; }
        public string? MAY_ZIP { get; set; }
        public string? JUN_ZIP { get; set; }
        public string? JUL_ZIP { get; set; }
        public string? AUG_ZIP { get; set; }
        public string? SEP_ZIP { get; set; }
        public string? OCT_ZIP { get; set; }
        public string? NOV_ZIP { get; set; }
        public string? DEC_ZIP { get; set; }

        public bool IsLocked { get; set; } // New Flag
                                           // Audit / IRS Defense

        public int? PlanStartMonth { get; set; }
        public SafeHarborType? AppliedSafeHarbor { get; set; }
    }

    public enum OfferOfCoverageType
    {
        None = 0,
        QualifyingOffer = 1, // 1A
        EmployeeOnly = 2,    // 1B
        EmployeeDependents = 3, // 1C
        EmployeeSpouse = 4, // 1D
        Family = 5,         // 1E
        NoOffer = 6         // 1H
    }

    // 2. HELPER: Holds the result for ONE month during calculation
    // (Used internally by ACALogicService before saving to the wide table)
    public class MonthlyCodeResult
    {
        public string? Line14 { get; set; }
        public decimal? Line15 { get; set; }
        public string? Line16 { get; set; }
    }

    // 3. HELPER: Context data needed to perform the calculation
    public class CalculationContext
    {
        public int EmployeeId { get; set; }
        public DateTime? HireDate { get; set; }
        public DateTime? TerminationDate { get; set; }
        public int Status { get; set; } // 1=FT, 0=PT

        // Coverage Info
        public bool CoverageElected { get; set; }
        public DateTime? CoverageStartDate { get; set; }
        public DateTime? CoverageEndDate { get; set; }
        public DateTime? DateEligible { get; set; }

        // Plan Info
        public int? PlanId { get; set; }
        public string? PlanName { get; set; }
        public bool OfferedToSpouse { get; set; }
        public bool OfferedToDependents { get; set; }
        public bool MinimumValue { get; set; }   // 🔹 REQUIRED for 1A

        // Cost Info
        public decimal? LowestCostPremium { get; set; }

        // Pay Info (NEW – required for safe harbor logic)
        public decimal? W2Wages { get; set; }
        public decimal? HourlyRate { get; set; }
        public decimal? MonthlyHours { get; set; }

        // Preferred Safe Harbor (Employer Policy)
        public string? SafeHarborMethod { get; set; } // FPL / W2 / ROP
    }

    public class EmployeeCodeReviewDto : EmployeeCode
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? SSN { get; set; }
        public string? Address1 { get; set; }
        public string? Address2 { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? Zip { get; set; }
    }
    public enum SafeHarborType
    {
        None = 0,
        W2 = 1,        // 2F
        FederalPovertyLine = 2, // 2G
        RateOfPay = 3  // 2H
    }
    public class EmployeeCodeAuditDto
    {
        public int EmployeeId { get; set; }
        public int EmployerId { get; set; }
        public string? FilingYear { get; set; }

        public string? MonthCode { get; set; }   // JAN, FEB...
        public bool AppliesToAllMonths { get; set; }

        public int LineNumber { get; set; }      // 14, 15, 16
        public string? FieldName { get; set; }    // JAN_COC, FEB_SHC

        public string? OldValue { get; set; }
        public string? NewValue { get; set; }

        public string? ChangeSource { get; set; } // SYSTEM / MANUAL / AUTOFIX
        public string? ChangeReason { get; set; }
        public string? SafeHarborApplied { get; set; } // 2F/2G/2H

        public string? ChangedByUserId { get; set; }
        public string? ChangedByUserName { get; set; }

        public bool IsSystemGenerated { get; set; }
        public bool IsLocked { get; set; }
    }
    public class EmployeeCodeAuditTimelineDto
    {
        public long AuditId { get; set; }

        public int EmployeeId { get; set; }
        public int EmployerId { get; set; }
        public int FilingYear { get; set; }

        public string? MonthCode { get; set; }       // JAN, FEB
        public int LineNumber { get; set; }          // 14,15,16
        public string? FieldName { get; set; }        // JAN_COC

        public string? OldValue { get; set; }
        public string? NewValue { get; set; }

        public string? ChangeSource { get; set; }     // SYSTEM / MANUAL / AUTOFIX
        public string? ChangeReason { get; set; }
        public string? SafeHarborApplied { get; set; }

        public string? ChangedByUserName { get; set; }
        public DateTime ChangedOn { get; set; }

        public bool IsSystemGenerated { get; set; }
    }

}