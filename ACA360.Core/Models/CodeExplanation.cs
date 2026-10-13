using System;
using System.Collections.Generic;

namespace ACA360.Core.Models
{
    /// <summary>
    /// One month as sp_Generate1095Codes worked it out (dbo.EmployeeCodeMonthDetail, script 058):
    /// what it knew about the employee and the plan, the year's limits, and the codes it gave.
    /// </summary>
    public class CodeMonthDetail
    {
        public int EmployeeCodeId { get; set; }
        public int MonthNum { get; set; }
        public bool? Employed { get; set; }
        public bool? IsFullTime { get; set; }
        public int? PlanId { get; set; }
        public string? PlanName { get; set; }
        public bool? MinimumValue { get; set; }
        public int? OfferedSpouse { get; set; }
        public int? ConditionalSpouse { get; set; }
        public int? OfferedDependents { get; set; }
        public string? FundingType { get; set; }
        public bool? Code1A { get; set; }
        public decimal? Premium { get; set; }
        public bool? Enrolled { get; set; }
        public bool? UnionMonth { get; set; }
        public bool? CobraMonth { get; set; }
        public bool? IsIchra { get; set; }
        public decimal? IchraLcsp { get; set; }
        public decimal? IchraAllowance { get; set; }
        public decimal? IchraNetCost { get; set; }
        public decimal? HourlyRate { get; set; }
        public decimal? AnnualW2 { get; set; }
        public int? HiredMonths { get; set; }
        public decimal? MonthlyFpl { get; set; }
        public decimal? AffordabilityPct { get; set; }
        public int? FullTimeHours { get; set; }
        public DateTime? HireDate { get; set; }
        public DateTime? TerminationDate { get; set; }
        public DateTime? EligibleDate { get; set; }
        public string? Line14 { get; set; }
        public decimal? Line15 { get; set; }
        public string? Line16 { get; set; }
        public DateTime CalculatedAt { get; set; }
        public string? CalculatedBy { get; set; }
    }

    /// <summary>One month of "Why this code?": the codes on file, the calculated ones and why.</summary>
    public class CodeExplanationMonth
    {
        public int Month { get; set; }
        public string MonthName { get; set; } = "";
        public string? Line14 { get; set; }
        public decimal? Line15 { get; set; }
        public string? Line16 { get; set; }
        /// <summary>The codes on file differ from the calculated ones: someone changed them by hand.</summary>
        public bool ChangedByHand { get; set; }
        public string? Calculated14 { get; set; }
        public decimal? Calculated15 { get; set; }
        public string? Calculated16 { get; set; }
        public List<string> Why14 { get; set; } = new();
        public string? Why15 { get; set; }
        public List<string> Why16 { get; set; } = new();
        public bool PenaltyRisk { get; set; }
        public CodeMonthDetail? Detail { get; set; }
    }

    public class CodeExplanation
    {
        public int EmployeeId { get; set; }
        public int EmployeeCodeId { get; set; }
        public int EmployerId { get; set; }
        public int FilingYear { get; set; }
        public string? EmployeeName { get; set; }
        public string? EmployerName { get; set; }
        public bool IsLocked { get; set; }
        public bool DisableCoding { get; set; }
        /// <summary>False until codes are generated once after script 058.</summary>
        public bool HasDetail { get; set; }
        public DateTime? CalculatedAt { get; set; }
        public decimal? MonthlyFpl { get; set; }
        public decimal? AffordabilityPct { get; set; }
        public int? FullTimeHours { get; set; }
        public decimal? HourlyRate { get; set; }
        public decimal? AnnualW2 { get; set; }
        public List<CodeExplanationMonth> Months { get; set; } = new();
    }
}
