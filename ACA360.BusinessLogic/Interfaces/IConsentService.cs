// TODO karthi: NEW FILE for the Consent + Employee-Portal feature (copied in during integration).
using ACA360.Core.Models;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Interfaces
{
    /// <summary>
    /// Reads and writes an employee's ACA tax-form delivery consent
    /// (Electronic vs Paper) and its audit trail. Backs the employee
    /// self-service page today and the employer tracking screens later.
    /// </summary>
    public interface IConsentService
    {
        /// <summary>
        /// Returns the current consent record + history for an employee/year,
        /// or a fresh (empty) model if none exists yet.
        /// </summary>
        Task<EmployeeConsentViewModel> GetConsentAsync(int employeeId, int taxYear);

        /// <summary>
        /// Upserts the delivery preference and writes the matching audit event.
        /// Returns the generated ConsentId (null for paper).
        /// </summary>
        Task<string?> SaveConsentAsync(EmployeeConsentViewModel model, string ipAddress, string userAgent);

        /// <summary>
        /// Records a standalone audit event (e.g. "Disclosure Viewed").
        /// No-op if the employee has no consent record yet.
        /// </summary>
        Task LogEventAsync(int employeeId, int taxYear, string eventType, string? detail);

        /// <summary>
        /// Reverts the employee to paper delivery and marks consent withdrawn.
        /// </summary>
        Task WithdrawAsync(int employeeId, int taxYear);

        /// <summary>True if an ACA form is available for this employee and tax year.</summary>
        Task<bool> IsFormAvailableAsync(int employeeId, int taxYear);

        /// <summary>Filing years for which this employee has a downloadable ACA form (newest first).</summary>
        Task<System.Collections.Generic.List<int>> GetAvailableFormYearsAsync(int employeeId);

        /// <summary>The ACA form type for this employee/year — "1095-C" or "1095-B".</summary>
        Task<string> GetFormTypeAsync(int employeeId, int taxYear);

        /// <summary>Records that the employee wants to be notified when the form is ready.</summary>
        Task RequestFormNotificationAsync(int employeeId, int taxYear);

        /// <summary>True if the employee already has a pending notify request for this year.</summary>
        Task<bool> IsNotifyRequestedAsync(int employeeId, int taxYear);
    }
}
