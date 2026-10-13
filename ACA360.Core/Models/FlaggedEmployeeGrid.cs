namespace ACA360.Core.Models;

public class FlaggedEmployeeGrid
{
    public int EmployeeId { get; set; }

    // Employee scope returned by the GET; selected child IDs are filled from the JSON collections.
    public int EmployeeCodeId { get; set; }

    // One employee-code row. SQL fills only the active tab's child collection;
    // the remaining arrays stay empty until that tab is requested.
    public string HireSpansJson { get; set; } = "[]";
    public string EnrollmentsJson { get; set; } = "[]";
    public string StatusesJson { get; set; } = "[]";
    public string PayrollsJson { get; set; } = "[]";
    public string DependentsJson { get; set; } = "[]";
    public string? InfoFields { get; set; }
    public string? CriticalFlagCodes { get; set; }
    public string? WarningFlagCodes { get; set; }
    public string? InfoFlagCodes { get; set; }

    public int? HireSpanId { get; set; }
    public int? EnrollmentId { get; set; }
    public int? StatusId { get; set; }
    public int? PayrollId { get; set; }
    public int? CoveredIndividualId { get; set; }

    public string? EmployeeNo { get; set; }
    public string? FullName { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? SSN { get; set; }
    public string? Birthday { get; set; }
    public string? Address { get; set; }
    public string? Address2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Zip { get; set; }
    public string? HireDate { get; set; }
    public string? HireEndDate { get; set; }
    public string? CoverageStart { get; set; }
    public string? Email { get; set; }

    // Plan Information
    public int? PlanId { get; set; }
    public string? PlanName { get; set; }

    // Union Information
    public bool? IsUnionMember { get; set; }
    public string? Union_ContributionStartDate { get; set; }
    public string? Union_ContributionEndDate { get; set; }

    // Coverage Information
    public string? CoverageOfferDate { get; set; }

    // Medical Enrollment
    public bool? IsMedicalEnrolled { get; set; }
    public string? Medical_CoverageStartDate { get; set; }
    public string? Medical_CoverageEndDate { get; set; }

    // COBRA Enrollment
    public bool? IsCOBRAEnrolled { get; set; }
    public string? COBRA_StartDate { get; set; }
    public string? COBRA_EndDate { get; set; }

    // Retiree Enrollment
    public bool? IsRetireeEnrolled { get; set; }
    public string? Retiree_StartDate { get; set; }
    public string? Retiree_EndDate { get; set; }

    // Status Information
    public string? Status { get; set; }
    public string? StatusStartDate { get; set; }
    public string? StatusEndDate { get; set; }

    // Pay Period Information
    public string? PayPeriodStartDate { get; set; }
    public string? PayPeriodEndDate { get; set; }
    public decimal? PayPeriodTotalHours { get; set; }
    public decimal? PayPeriodSalaryAmount { get; set; }
    public decimal? PayPeriodHourlyAmount { get; set; }
    public string? PayPeriodAdditional { get; set; }

    // Notes
    public string? Note { get; set; }

    // Dependents
    public string? DependentFirstName { get; set; }
    public string? DependentMiddleName { get; set; }
    public string? DependentLastName { get; set; }
    public string? DependentSSN { get; set; }
    public string? DependentBirthday { get; set; }
    public string? DependentCoverageStartDate { get; set; }
    public string? DependentCoverageEndDate { get; set; }
    public string? DependentSuffix { get; set; }

    // Split aggregations for exact UI styling
    public string? CriticalFields { get; set; } // e.g. "SSN,Zip"
    public string? WarningFields { get; set; }  // e.g. "Address,City"
    public string? AllFlagCodes { get; set; }   // e.g. "16.1,14.A"
}
