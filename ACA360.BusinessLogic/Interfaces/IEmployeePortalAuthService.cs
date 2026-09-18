// TODO karthi: NEW FILE for the Consent + Employee-Portal feature (copied in during integration).
using ACA360.Core.Models;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Interfaces
{
    public enum PortalLoginStatus { InvalidCredentials, InactiveAccount, RequiresPasswordReset, Success }

    public class PortalLoginResult
    {
        public PortalLoginStatus Status { get; set; }
        public EmployeePortalUser? User { get; set; }
    }

    /// <summary>
    /// Authentication for the employee self-service portal (email + BCrypt password),
    /// with an admin-driven activation that issues a one-time temp password.
    /// </summary>
    public interface IEmployeePortalAuthService
    {
        /// <summary>Activates (or re-activates) portal access; returns the generated temp password to relay.</summary>
        Task<string> ActivateAsync(int employeeId, int taxYear, string email, string createdBy);

        /// <summary>Validates an email + password login attempt.</summary>
        Task<PortalLoginResult> ValidateLoginAsync(string email, string password);

        /// <summary>Fetches a portal user by email (with employee name + tax year).</summary>
        Task<EmployeePortalUser?> GetByEmailAsync(string email);

        /// <summary>Sets the permanent BCrypt password and clears the temp/reset flags.</summary>
        Task SetPasswordAsync(string email, string newPassword);

        /// <summary>Verifies a forgot-password attempt (email + last-4 SSN + date of birth).</summary>
        Task<bool> VerifyIdentityAsync(string email, string last4SSN, System.DateTime dateOfBirth);

        /// <summary>Latest filing year in the system (for the portal to show the current year).</summary>
        Task<int?> GetLatestFilingYearAsync();
    }
}
