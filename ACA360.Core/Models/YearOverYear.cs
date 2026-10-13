using System;

namespace ACA360.Core.Models
{
    /// <summary>One employer's filing-year figures from its 1095-C codes (dbo.fn_EmployerYearFigures, script 064).</summary>
    public class EmployerYearFigures
    {
        public int EmployerId { get; set; }
        public int FilingYear { get; set; }
        public int Employees { get; set; }
        public int FullTimeEmployees { get; set; }
        public int OfferedMonths { get; set; }
        public int EnrolledMonths { get; set; }
        public int NotOfferedMonths { get; set; }
        public int UnaffordableMonths { get; set; }
        public int AtRiskEmployees { get; set; }
        public decimal EstimatedPenalty { get; set; }
        public int CodeRows { get; set; }
    }

    /// <summary>An employer whose estimated penalty grew since last year (script 064).</summary>
    public class ExposureGrowthRow
    {
        public int EmployerId { get; set; }
        public string? EmployerName { get; set; }
        public string? Ein { get; set; }
        public decimal ThisYear { get; set; }
        public decimal LastYear { get; set; }
        public decimal Growth { get; set; }
    }
}
