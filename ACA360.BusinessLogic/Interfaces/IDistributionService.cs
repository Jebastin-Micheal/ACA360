using ACA360.Core.Models;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Interfaces
{
    public interface IDistributionService
    {
        // 1. Dashboard Data
        Task<DistributionDashboardViewModel> GetDistributionStatsAsync(int employerId, int year, PaginationEntity pagination, string statusFilter = "all");

        // 2. Generate Secure Link
        Task<string> GenerateSecureLinkAsync(int employeeId, int year);

        // 3. Verify Token (For Employee Portal)
        /// <summary>
        /// Validates an employee portal link. The caller's address is recorded against
        /// the link so a leaked one leaves a trail, and failed attempts are counted on
        /// the row itself rather than in memory — see F-32.
        /// </summary>
        Task<SecureTokenValidationResult?> ValidateTokenAsync(Guid linkId, string last4SSN, string? ipAddress = null);

        // 4. Mark as Sent
        /// <summary>
        /// Composes, sends and records the employee's 1095-C notification. Returns the
        /// delivery outcome rather than throwing, so a batch survives one bad address.
        /// </summary>
        Task<MailDeliveryResult> SendEmployeeFormEmailAsync(int employeeId, int year, string secureLink);

        /// <summary>
        /// Retained for existing callers. Delegates to
        /// <see cref="SendEmployeeFormEmailAsync"/> so the audit row and the actual send
        /// cannot drift apart.
        /// </summary>
        Task LogEmailSentAsync(int employeeId, int year, string secureLink);
        Task ProcessBatchEmailDistributionAsync(int employerId, int year, List<int> employeeIds);
    }
}