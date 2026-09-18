using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Models
{
    public class StagingEmployer
    {
        public long StagingEmployerId { get; set; }
        public int FileLogId { get; set; }
        public int RowNumber { get; set; }

        // Columns from 'Employer Tab' Excel Sheet
        public string? PrimaryEIN { get; set; }
        public string? AffiliatedEIN { get; set; }
        public string? EmployerName { get; set; }
        public string? ForeignAddress { get; set; }
        public string? Ind { get; set; }
        public string? Address { get; set; }
        public string? Address2 { get; set; }
        public string? City { get; set; }
        public string? StateOrProvince { get; set; }
        public string? Zip { get; set; }
        public string? Country { get; set; }
        public string? Phone { get; set; }
        public string? Contact { get; set; }
        public string? Title { get; set; }
        public string? OriginCode { get; set; }
        public string? SHOPIdentifier { get; set; }
        public string? Notes { get; set; }

        // Columns for validation result
        public bool IsValid { get; set; }
        public string? ErrorMessages { get; set; }
    }

    public class StagingPlan
    {
        public long StagingPlanId { get; set; }
        public int FileLogId { get; set; }
        public int RowNumber { get; set; }

        // Columns from 'Plan Information' Excel Sheet
        public string? PrimaryEIN { get; set; }
        public string? PlanName { get; set; }
        public string? PlanType { get; set; }
        public string? OfferedToSpouse { get; set; }
        public string? OfferedToDependents { get; set; }
        public string? WaitingPeriodNumberOfDays { get; set; }
        public string? EligibleFirstOfTheMonth { get; set; }
        public string? FundingType { get; set; }
        public string? PlanRenewalMonth { get; set; }
        public string? PlanTerminatesOnDateOfTermination { get; set; }
        public string? MinimumValue { get; set; }
        public string? BandingType { get; set; }
        public string? PremiumCap { get; set; }

        // Columns for validation result
        public bool IsValid { get; set; }
        public string? ErrorMessages { get; set; }
    }

    public class StagingPremium
    {
        public long StagingPremiumId { get; set; }
        public int FileLogId { get; set; }
        public int RowNumber { get; set; }

        // Columns from 'Premium' Excel Sheet
        public string? PrimaryEIN { get; set; }
        public string? PlanName { get; set; }
        public string? BandingType { get; set; }
        public string? Start { get; set; }
        public string? End { get; set; }
        public string? StartDate { get; set; }
        public string? EndDate { get; set; }
        public string? EEMonthlyContribution { get; set; }

        // Columns for validation result
        public bool IsValid { get; set; }
        public string? ErrorMessages { get; set; }
    }

    public class StagingEmployee
    {
        public long StagingEmployeeId { get; set; }
        public int FileLogId { get; set; }
        public int RowNumber { get; set; }

        // Columns from 'Employee' Excel Sheet
        public string? PrimaryEIN { get; set; }
        public string? EINAssociatedWithEE { get; set; }
        public string? EmployeeLegalFirstName { get; set; }
        public string? EmployeeMiddleInitial { get; set; }
        public string? EmployeeLegalLastName { get; set; }
        public string? EmployeeSuffix { get; set; }
        public string? EmployeeSSN { get; set; }
        public string? EmployeeBirthdate { get; set; }
        public string? Status { get; set; }
        public string? Expatriate { get; set; }
        public string? StatusStartDate { get; set; }
        public string? StatusEndDate { get; set; }
        public string? HireDate { get; set; }
        public string? TerminationDate { get; set; }
        public string? W2IncomeOrAnnualSalary { get; set; }
        public string? AdditionalIncome { get; set; }
        public string? HourlyRate { get; set; }
        public string? EmailAddress { get; set; }
        public string? ForeignAddress { get; set; }
        public string? Address1 { get; set; }
        public string? Address2 { get; set; }
        public string? City { get; set; }
        public string? StateOrProvince { get; set; }
        public string? Zip { get; set; }
        public string? Country { get; set; }
        public string? LowestCostPlanOffered { get; set; }
        public string? DateEligibleForCoverage { get; set; }
        public string? CoverageElected { get; set; }
        public string? CoverageStartDate { get; set; }
        public string? CoverageEndDate { get; set; }
        public string? UnionEmployee { get; set; }
        public string? DateEmployerFirstPaysToUnionBenefits { get; set; }
        public string? DateEmployerLastPaysToUnionBenefits { get; set; }
        public string? COBRABenefitsElected { get; set; }
        public string? COBRACoverageEffectiveDate { get; set; }
        public string? COBRACoverageEndDate { get; set; }
        public string? RetireeBenefitsElected { get; set; }
        public string? RetireePlanStartDate { get; set; }
        public string? RetireePlanEndDate { get; set; }

        // Columns for validation result
        public bool IsValid { get; set; }
        public string? ErrorMessages { get; set; }
    }

    public class StagingDependent
    {
        public long StagingDependentId { get; set; }
        public int FileLogId { get; set; }
        public int RowNumber { get; set; }

        // Columns from 'Dependents' Excel Sheet
        public string? PrimaryEIN { get; set; }
        public string? EINAssociatedWithEE { get; set; }
        public string? EmployeeSSN { get; set; }
        public string? DependentLegalFirstName { get; set; }
        public string? DependentMiddleInitial { get; set; }
        public string? DependentLegalLastName { get; set; }
        public string? DependentSuffix { get; set; }
        public string? DependentSSN { get; set; }
        public string? DependentBirthdate { get; set; }
        public string? DependentCoverageStartDate { get; set; }
        public string? DependentCoverageEndDate { get; set; }

        // Columns for validation result
        public bool IsValid { get; set; }
        public string? ErrorMessages { get; set; }
    }
}
