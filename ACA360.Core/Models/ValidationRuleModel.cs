using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Models
{
    public class ValidationRule
    {
        public int RuleId { get; set; }

        [Required]
        [DisplayName("Rule Name")]
        public string? RuleName { get; set; }

        [Required]
        [DisplayName("Target Table")]
        public string? TargetTable { get; set; }

        [Required]
        [DisplayName("Target Column")]
        public string? TargetColumn { get; set; }

        [Required]
        [DisplayName("Validation Type")]
        public string? ValidationType { get; set; }

        [DisplayName("Parameter 1")]
        public string? RuleParameter1 { get; set; }

        [DisplayName("Parameter 2")]
        public string? RuleParameter2 { get; set; }

        [Required]
        [DisplayName("Error Message")]
        public string? ErrorMessage { get; set; }

        [DisplayName("Is Active?")]
        public bool IsActive { get; set; }
        // Add the new properties to your ValidationRule model
        public string? Severity { get; set; }
        public bool IsCorrectable { get; set; }
        public string? HelpText { get; set; }

        public string? Topic { get; set; }
        public string? AuditTabNote { get; set; }
        public string? AlternateNote { get; set; }
        public string? EmailSummaryNote { get; set; }
        public string? RuleCode { get; set; }
        public bool IsAutoCorrect { get; set; }
        public string? AutoCorrectReplacement { get; set; } // Optional: What to replace bad chars with
    }
}
