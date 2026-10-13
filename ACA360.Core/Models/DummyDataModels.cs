using System.ComponentModel.DataAnnotations;

namespace ACA360.Core.Models
{
    /// <summary>What the Dummy Data page (SuperAdmin) generates: one import workbook per employer.</summary>
    public sealed class DummyDataRequest
    {
        /// <summary>Existing main employers; each gets a workbook with its EIN as the Primary EIN.</summary>
        public List<int> EmployerIds { get; set; } = new();

        /// <summary>
        /// New employer names, one per line: each gets a workbook with a made-up EIN. Nothing is
        /// created in ACA360, so the upload refuses the file until that employer exists.
        /// </summary>
        public string? NewEmployerNames { get; set; }

        [Range(2000, 2100)]
        public int FilingYear { get; set; }

        /// <summary>Made-up affiliate employers inside each workbook.</summary>
        [Range(0, 20)]
        public int Affiliates { get; set; }

        [Range(1, 20)]
        public int Plans { get; set; } = 2;

        /// <summary>How premiums are banded: Monthly, Salary, Age, or Mixed (the plans take turns).</summary>
        public string Banding { get; set; } = "Monthly";

        /// <summary>Employees per workbook, spread over the main employer and its affiliates.</summary>
        [Range(1, 50_000)]
        public int Employees { get; set; } = 100;

        /// <summary>Dependents per employee who elected coverage, between these two numbers.</summary>
        [Range(0, 6)]
        public int DependentsMin { get; set; }

        [Range(0, 6)]
        public int DependentsMax { get; set; } = 3;

        // Employee mix, as shares of the employees (COBRA and retiree: of those who leave with coverage).
        [Range(0, 100)] public int PartTimePercent { get; set; }
        [Range(0, 100)] public int NewHirePercent { get; set; } = 30;
        [Range(0, 100)] public int CoveragePercent { get; set; } = 85;
        [Range(0, 100)] public int TerminatedPercent { get; set; }
        [Range(0, 100)] public int CobraPercent { get; set; }
        [Range(0, 100)] public int RetireePercent { get; set; }
        [Range(0, 100)] public int UnionPercent { get; set; }

        /// <summary>Share of employees given one warning (still importable).</summary>
        [Range(0, 100)]
        public int WarningPercent { get; set; }

        /// <summary>Share of employees given one error (excluded from the import until fixed).</summary>
        [Range(0, 100)]
        public int ErrorPercent { get; set; }

        /// <summary>The warning and error kinds to use (DummyDataGenerator.Issues codes); none means all.</summary>
        public List<string> IssueCodes { get; set; } = new();

        /// <summary>The same seed and inputs give the same file.</summary>
        public int? Seed { get; set; }
    }

    /// <summary>An employer as the generator needs it. Id 0: a new name, only in the file.</summary>
    public sealed record DummyEmployer(int Id, string Name, string Ein)
    {
        public bool IsNew => Id == 0;
    }

    /// <summary>What one generated workbook holds.</summary>
    public sealed record DummyFileSummary(
        string FileName,
        string EmployerName,
        string Ein,
        bool IsNewEmployer,
        int Employers,
        int Plans,
        int Premiums,
        int Employees,
        int Dependents,
        int CleanEmployees,
        IReadOnlyDictionary<string, int> Mix,
        IReadOnlyDictionary<string, int> Warnings,
        IReadOnlyDictionary<string, int> Errors);
}
