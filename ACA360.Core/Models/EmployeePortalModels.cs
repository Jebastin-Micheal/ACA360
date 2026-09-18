// TODO karthi: NEW FILE for the Consent + Employee-Portal feature (copied in during integration).
using System;
using System.ComponentModel.DataAnnotations;

namespace ACA360.Core.Models
{
    /// <summary>
    /// A row from EmployeePortalUser joined to the employee (name + tax year).
    /// </summary>
    public class EmployeePortalUser
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? PasswordHash { get; set; }
        public string? TempPassword { get; set; }
        public bool RequirePasswordReset { get; set; }
        public bool IsActive { get; set; }
        public int FailedLoginCount { get; set; }
        public DateTime? LastLogin { get; set; }

        // From the joined Employee row.
        public int? TaxYear { get; set; }
        public string? EmployeeName { get; set; }
    }

    public class PortalLoginViewModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required, DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
    }

    public class PortalForgotPasswordViewModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required, StringLength(4, MinimumLength = 4, ErrorMessage = "Enter the last 4 digits of your SSN.")]
        public string Last4SSN { get; set; } = string.Empty;

        [Required, DataType(DataType.Date)]
        public DateTime? DateOfBirth { get; set; }
    }

    public class PortalSetPasswordViewModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required, DataType(DataType.Password), MinLength(8, ErrorMessage = "Password must be at least 8 characters.")]
        public string NewPassword { get; set; } = string.Empty;

        [Required, DataType(DataType.Password), Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
