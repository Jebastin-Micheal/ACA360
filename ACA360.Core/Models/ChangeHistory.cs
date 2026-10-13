namespace ACA360.Core.Models
{
    /// <summary>One line of an employee's history (script 068): an import, a hand edit or a code change.</summary>
    public class EmployeeHistoryRow
    {
        public DateTime ChangedAt { get; set; }
        /// <summary>Import, Edit or Codes.</summary>
        public string Source { get; set; } = "";
        public string? Summary { get; set; }
        public string? ChangedBy { get; set; }
        public int? FileLogId { get; set; }
        public string? FileName { get; set; }
        public string? FieldName { get; set; }
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
        /// <summary>Rows of one save or one import share it.</summary>
        public string GroupKey { get; set; } = "";
    }

    /// <summary>One event on an employer's timeline (script 069).</summary>
    public class EmployerTimelineEvent
    {
        public DateTime EventAt { get; set; }
        /// <summary>File, Codes, Forms, Filing, Note, Edit, Assignment or Deadline.</summary>
        public string Kind { get; set; } = "";
        public string? Title { get; set; }
        public string? Detail { get; set; }
        public string? Who { get; set; }
        public int? FileLogId { get; set; }
        public bool IsUpcoming { get; set; }
    }
}
