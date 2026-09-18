using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ACA360.Core.Models
{
    [Table("FileLifecycleHistory")]
    public class FileLifecycleHistory
    {
        [Key]
        public int HistoryId { get; set; }

        public int FileLogId { get; set; }

        public int? PreviousStatusId { get; set; } // Nullable if it's the first action

        public int NewStatusId { get; set; } // The ID of the status *after* the action

        [Required]
        [StringLength(100)]
        public string? ActionName { get; set; } // e.g., "Upload", "Auto-Assign", "Reject"

        [StringLength(450)]
        public string? PerformedByUserId { get; set; }

        [StringLength(255)]
        public string? PerformedByUserName { get; set; } // Stored for display speed

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [StringLength(500)]
        public string? Message { get; set; } // e.g., "System found 300 errors."
    }
}