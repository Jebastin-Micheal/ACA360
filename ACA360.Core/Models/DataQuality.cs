namespace ACA360.Core.Models
{
    /// <summary>One file in a rule what-if (script 067): what the saved rule flags now and what the trial would.</summary>
    public class RuleWhatIfFile
    {
        public int FileLogId { get; set; }
        public string? FileName { get; set; }
        public string? EmployerName { get; set; }
        public int? PlanYear { get; set; }
        public DateTime? UploadedAt { get; set; }
        public int TotalRows { get; set; }
        public int NowFlagged { get; set; }
        public int WouldFlag { get; set; }
        public int NewlyFlagged { get; set; }
        public int NoLongerFlagged { get; set; }
    }

    /// <summary>A row the trial rule flags. SSN columns come back as their last four digits.</summary>
    public class RuleWhatIfSample
    {
        public int FileLogId { get; set; }
        public int RowNumber { get; set; }
        public string? Value { get; set; }
        public bool NowFlagged { get; set; }
    }

    public class RuleWhatIfResult
    {
        /// <summary>Why the rule could not run as written (unknown keyword, missing column...).</summary>
        public List<string> Problems { get; set; } = new();
        public List<RuleWhatIfFile> Files { get; set; } = new();
        public List<RuleWhatIfSample> Samples { get; set; } = new();
        public int WouldFlag => Files.Sum(f => f.WouldFlag);
        public int NowFlagged => Files.Sum(f => f.NowFlagged);
        public int NewlyFlagged => Files.Sum(f => f.NewlyFlagged);
        public int NoLongerFlagged => Files.Sum(f => f.NoLongerFlagged);
    }

    /// <summary>An employee record whose SSN is shared or inconsistent (script 066). The SSN itself is never loaded.</summary>
    public class DuplicateSsnRow
    {
        public int GroupNo { get; set; }
        public string? SsnMasked { get; set; }
        public int Records { get; set; }
        public int Employers { get; set; }
        public bool SeveralEmployers { get; set; }
        public bool RepeatedInEmployer { get; set; }
        public bool DifferentNames { get; set; }
        public bool DifferentBirthdates { get; set; }
        public long EmployeeId { get; set; }
        public string? EmployeeName { get; set; }
        public DateTime? Birthdate { get; set; }
        public long EmployerId { get; set; }
        public string? EmployerName { get; set; }
        public string? EIN { get; set; }
        public int? FileLogId { get; set; }
        public string? FileName { get; set; }
    }
}
