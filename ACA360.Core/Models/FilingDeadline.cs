using System;
using System.ComponentModel.DataAnnotations;

namespace ACA360.Core.Models
{
    /// <summary>A filing deadline (dbo.FilingDeadlines, script 063): IRS or state, for one tax year.</summary>
    public class FilingDeadline
    {
        public int FilingDeadlineId { get; set; }

        [Range(2015, 2100)]
        public int FilingYear { get; set; }

        [Required, StringLength(10)]
        public string Jurisdiction { get; set; } = "IRS";

        [Required, StringLength(20)]
        public string Kind { get; set; } = "Furnish";

        [Required, StringLength(200)]
        public string Title { get; set; } = "";

        public DateTime DueDate { get; set; }

        [StringLength(500)]
        public string? Notes { get; set; }

        /// <summary>Seeded and not yet confirmed by an administrator.</summary>
        public bool NeedsCheck { get; set; }
        public string? ModifiedBy { get; set; }
        public DateTime? ModifiedAt { get; set; }

        /// <summary>Whole days from today to the due date (negative once passed).</summary>
        public int DaysLeft(DateTime today) => (DueDate.Date - today.Date).Days;

        public static readonly string[] Kinds = { "Furnish", "E-file", "Paper", "Other" };
    }
}
