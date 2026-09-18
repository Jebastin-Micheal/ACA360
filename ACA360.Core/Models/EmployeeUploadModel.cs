namespace ACA360.Core.Models
{
    public class EmployeeUploadModel
    {
        public string? PrimaryEIN { get; set; }
        public string? EINAssociatedWithEE { get; set; }
        public string? EmployeeLegalFirstName { get; set; }
        public string? EmployeeMiddleInitial { get; set; }
        public string? EmployeeLegalLastName { get; set; }
        public string? EmployeeSuffix { get; set; }
        public string? EmployeeSSN { get; set; }
        public DateTime? EmployeeBirthdate { get; set; }
        public string? Status { get; set; }
        public bool? Expatriate { get; set; }
        public DateTime? StatusStartDate { get; set; }
        public DateTime? StatusEndDate { get; set; }
        public DateTime? HireDate { get; set; }
        public DateTime? TerminationDate { get; set; }
        public decimal? W2IncomeAnnualSalary { get; set; }
        public decimal? AdditionalIncome { get; set; }
        public decimal? HourlyRate { get; set; }
        public string? EmailAddress { get; set; }
        public bool? ForeignAddress { get; set; }
        public string? Address1 { get; set; }
        public string? Address2 { get; set; }
        public string? City { get; set; }
        public string? StateProvince { get; set; }
        public string? Zip { get; set; }
        public string? Country { get; set; }
        public string? LowestCostPlanOffered { get; set; }
        public DateTime? DateEligibleForCoverage { get; set; }
        public bool? CoverageElected { get; set; }
        public DateTime? CoverageStartDate { get; set; }
        public DateTime? CoverageEndDate { get; set; }
        public bool? UnionEmployee { get; set; }
        public DateTime? DateEmployerFirstPaysToUnionBenefits { get; set; }
        public DateTime? DateEmployerLastPaysToUnionBenefits { get; set; }
        public bool? COBRABenefitsElected { get; set; }
        public DateTime? COBRACoverageEffectiveDate { get; set; }
        public DateTime? COBRACoverageEndDate { get; set; }
        public bool? RetireeBenefitsElected { get; set; }
        public DateTime? RetireePlanStartDate { get; set; }
        public DateTime? RetireePlanEndDate { get; set; }
    }
}