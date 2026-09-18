// File: ACA360.Core/Models/FlagDefinition.cs
using System;
using System.ComponentModel.DataAnnotations;

namespace ACA360.Core.Models
{
    public class FlagDefinition
    {
        [Key]
        public int FlagId { get; set; }
        public string? FlagCode { get; set; }        // E.g., "16.1"
        public string? FlagName { get; set; }
        public string? Description { get; set; }
        public string? Category { get; set; }        // 'Potential Penalty', 'QLE'
        public string? Severity { get; set; }        // 'Warning', 'Critical'
        public string? TargetTab { get; set; }
        public string? TargetField { get; set; }
        // Logic Configuration
        public string? LogicType { get; set; }       // 'Simple', 'CodeCombination', 'CustomSQL'
        public string? TargetTable { get; set; }     // 'EmployeeMonthlyCodes', 'Staging_Employees'

        // Conditions
        public string? TargetColumn { get; set; }
        public string? Operator { get; set; }
        public string? ComparisonValue { get; set; }

        public string? Connector { get; set; }       // 'AND', 'OR'
        public string? SecondaryColumn { get; set; }
        public string? SecondaryOperator { get; set; }
        public string? SecondaryValue { get; set; }

        public string? CustomSqlPredicate { get; set; }
        public string? RemediationSuggestion { get; set; }

        public bool IsActive { get; set; }
        public bool IsSystemFlag { get; set; }      // Protected flags
        public DateTime CreatedDate { get; set; }
        public DateTime? ModifiedDate { get; set; }
        public string? CreatedBy { get; set; }
    }
}