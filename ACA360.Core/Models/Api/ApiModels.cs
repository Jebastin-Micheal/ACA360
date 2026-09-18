using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ACA360.Core.Models.Api
{
    public class EmployeePushRequest
    {
        [Required]
        public int Year { get; set; }
        public List<EmployeeApiDto> Employees { get; set; } = new List<EmployeeApiDto>();
    }

    public class EmployeeApiDto
    {
        [Required]
        public string? SSN { get; set; }
        [Required]
        public string? FirstName { get; set; }
        [Required]
        public string? LastName { get; set; }

        public string? Address1 { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? Zip { get; set; }

        public DateTime? HireDate { get; set; }
        public DateTime? TerminationDate { get; set; }

        // ACA Logic (Lines 14, 15, 16)
        public string? PlanOfferCode_All12 { get; set; }
        public string? SafeHarborCode_All12 { get; set; }
        public decimal MonthlyPremium { get; set; }
    }

    public class ApiImportResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? ErrorMessage { get; set; }
        public int RecordsProcessed { get; set; }
        public Guid TransactionId { get; set; }
    }
}