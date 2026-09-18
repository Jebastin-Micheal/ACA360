using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace ACA360.Core.ViewModels
{
    public class FlagDefinitionViewModel
    {
        public int FlagId { get; set; }

        [Required]
        [Display(Name = "Flag Code")]
        public string FlagCode { get; set; } // E.g. "16.1"

        [Required]
        [Display(Name = "Flag Name")]
        public string FlagName { get; set; }

        [Required]
        public string Category { get; set; } // 'Potential Penalty', 'QLE', etc.

        [Required]
        public string Severity { get; set; } // 'Warning', 'Critical'

        public string Description { get; set; }
        public string RemediationSuggestion { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsSystemFlag { get; set; }
        // TODO: [FLAG-FIX DB] Fix-button routing (admin sets where "Fix" jumps for this flag).
        [Display(Name = "Target Tab")]
        public string TargetTab { get; set; }
        [Display(Name = "Target Field(s)")]
        public string TargetField { get; set; }

        // --- LOGIC CONFIGURATION ---
        [Required]
        [Display(Name = "Logic Type")]
        public string LogicType { get; set; } // 'Simple', 'CodeCombination', 'CustomSQL'

        public string TargetTable { get; set; } = "EmployeeMonthlyCodes";

        // Condition 1
        public string TargetColumn { get; set; }
        public string Operator { get; set; }
        public string ComparisonValue { get; set; }

        // Condition 2 (For Compound Logic)
        public string Connector { get; set; } // AND / OR
        public string SecondaryColumn { get; set; }
        public string SecondaryOperator { get; set; }
        public string SecondaryValue { get; set; }

        // Advanced
        [Display(Name = "Custom SQL Predicate")]
        public string CustomSqlPredicate { get; set; }


        // --- DROPDOWNS FOR UI ---
        public List<SelectListItem> LogicTypes { get; } = new List<SelectListItem>
        {
            new SelectListItem { Text = "Simple (One Condition)", Value = "Simple" },
            new SelectListItem { Text = "Code Combination (Two Conditions)", Value = "CodeCombination" },
            new SelectListItem { Text = "Advanced (Custom SQL)", Value = "CustomSQL" }
        };

        public List<SelectListItem> Operators { get; } = new List<SelectListItem>
        {
            new SelectListItem { Text = "Equals (=)", Value = "EQUALS" },
            new SelectListItem { Text = "Not Equals (!=)", Value = "NOT_EQUALS" },
            new SelectListItem { Text = "Is Empty/Null", Value = "IS_NULL" },
            new SelectListItem { Text = "Is Not Empty", Value = "IS_NOT_NULL" },
            new SelectListItem { Text = "Contains", Value = "CONTAINS" }
        };

        public List<SelectListItem> Tables { get; } = new List<SelectListItem>
        {
            new SelectListItem { Text = "Monthly Codes (Line 14/16)", Value = "EmployeeMonthlyCodes" },
            new SelectListItem { Text = "Employee Data (Status/Dates)", Value = "Staging_Employees" },
            new SelectListItem { Text = "Dependents", Value = "Staging_Dependents" }
        };

        public List<SelectListItem> Columns { get; } = new List<SelectListItem>
        {
            new SelectListItem { Text = "Line 14 Code", Value = "Line14Code" },
            new SelectListItem { Text = "Line 16 Code", Value = "Line16Code" },
            new SelectListItem { Text = "Employee Status", Value = "Status" },
            new SelectListItem { Text = "Coverage Elected", Value = "CoverageElected" },
            new SelectListItem { Text = "Coverage End Date", Value = "CoverageEndDate" }
        };
        // TODO: [FLAG-FIX DB] Target Tab options for the Fix-navigation dropdown. Value must match the
        // TAB_TO_SECTION keys in EmployeeDetails.cshtml (the flag-fix handler).
        public List<SelectListItem> FixTargetTabs { get; } = new List<SelectListItem>
 {
     new SelectListItem { Text = "— none (no Fix jump) —", Value = "" },
     new SelectListItem { Text = "Basic Info",          Value = "basic" },
     new SelectListItem { Text = "Medical",             Value = "medical" },
     new SelectListItem { Text = "Codes matrix",        Value = "codes" },
     new SelectListItem { Text = "Status",              Value = "status" },
     new SelectListItem { Text = "Hire / Termination",  Value = "hire" },
     new SelectListItem { Text = "Payroll / Banding",   Value = "payroll" },
     new SelectListItem { Text = "COBRA",               Value = "cobra" },
     new SelectListItem { Text = "Retiree",             Value = "retiree" },
     new SelectListItem { Text = "Union",               Value = "union" },
     new SelectListItem { Text = "Covered Individuals", Value = "covered" }
 };
    }
}