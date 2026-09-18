using System;
using System.Collections.Generic;

namespace ACA360.Core.Models
{
    public class FormGenerationDashboardViewModel
    {
        public int SelectedYear { get; set; }
        public string? SelectedFormType { get; set; } = "1094-C_1095-C";

        // Filter & Sort State
        public string? FilterStatus { get; set; } = "All"; // "All", "Ready", "NotReady"
        public string? SortColumn { get; set; } = "Name"; // "Name", "Employees"
        public string? SortOrder { get; set; } = "Asc";   // "Asc", "Desc"

        public string? SearchTerm { get; set; } = ""; //search

        public List<EmployerGenerationStatusDto> Employers { get; set; } = new List<EmployerGenerationStatusDto>();
    }

    public class EmployerGenerationStatusDto
    {
        public int EmployerId { get; set; }
        public string? EmployerName { get; set; }
        public string? TaxId { get; set; }
        public int filingYear { get; set; }
        // Readiness Logic
        public int EmployeeCount { get; set; }
        public int EmployeesWithCodes { get; set; }

        public bool IsReady => EmployeeCount > 0 && EmployeeCount == EmployeesWithCodes;

        // Advanced Features
        public DateTime? LastGeneratedDate { get; set; }
        public int MissingSSNCount { get; set; }
        public int MissingAddressCount { get; set; }

        public int GapCount => EmployeeCount - EmployeesWithCodes;
    }

    public class BatchProcessingRequest
    {
        public List<int> EmployerIds { get; set; } = new List<int>();
        public int Year { get; set; }
        public string? FormType { get; set; }
    }
    public class Employer1094BViewModel
    {
        public Employer Employer { get; set; } = new Employer();
        public int TotalForms { get; set; }
        public string? SignatureDate { get; set; }
    }

    public class MultiBatchRequest
    {
        public List<int> EmployerIds { get; set; } = new List<int>();
        public List<int> EmployeeIds { get; set; } = new List<int>();
        public int Year { get; set; }
        public string? FormType { get; set; }
        public bool SuppressSSN { get; set; }
    }
    
}