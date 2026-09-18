using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using System.ComponentModel.DataAnnotations;

namespace ACA360.Core.Models.Tracker
{
    public class FirmItem
    {
        public int FirmId { get; set; }

        [Required(ErrorMessage = "Official Firm Name is required")]
        public string FirmName { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? Zip { get; set; }
        [ValidateNever]
        public int? StateId { get; set; }
        [ValidateNever]
        public string? State { get; set; }

        [ValidateNever]
        public string? Code { get; set; }
        public bool IsActive { get; set; } = true;

        [ValidateNever]
        public int TotalCount { get; set; }
        [ValidateNever]
        public int BrokerCount { get; set; }
    }

    public class BrokerItem
    {
        public int BrokerId { get; set; }
        public int FirmId { get; set; }

        [ValidateNever]
        public string? FirmName { get; set; }

        [Required(ErrorMessage = "Broker Name is required")]
        public string BrokerName { get; set; }


        public string? Email { get; set; }

        [ValidateNever]
        [StringLength(50, ErrorMessage = "Phone cannot exceed 50 characters")]
        public string? Phone { get; set; }

        [ValidateNever] public string? Address { get; set; }
        [ValidateNever] public string? City { get; set; }
        [ValidateNever] public int? StateId { get; set; }
        [ValidateNever] public string? State { get; set; }
        [ValidateNever] public string? Zip { get; set; }
        [ValidateNever] public string? ConnectUser { get; set; }
        [ValidateNever] public string? ACARoles { get; set; }

        public bool IsActive { get; set; } = true;

        [ValidateNever]
        public int TotalCount { get; set; }
    }
}