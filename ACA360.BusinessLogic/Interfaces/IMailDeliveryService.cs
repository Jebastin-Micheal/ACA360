using System.Threading;
using System.Threading.Tasks;
using ACA360.Core.Models;

namespace ACA360.BusinessLogic.Interfaces
{
    /// <summary>
    /// Sends mail to the outside world.
    ///
    /// Distinct from <c>IEmailService</c>, which despite its name is the in-application
    /// inbox — it reads and writes the <c>Inbox</c> table and has never sent anything.
    /// Before this interface existed nothing in the solution sent an email at all: the
    /// only SmtpClient was the "test connection" button on System Settings, and
    /// DistributionController recorded deliveries it never performed (F-37).
    /// </summary>
    public interface IMailDeliveryService
    {
        /// <summary>
        /// True when a provider is configured and sending is switched on. Check this
        /// before offering the user an action that depends on mail going out.
        /// </summary>
        bool IsConfigured { get; }

        /// <summary>Name of the active provider, for logging and diagnostics.</summary>
        string ProviderName { get; }

        /// <summary>
        /// Hands one message to the provider. Never throws for an ordinary delivery
        /// failure — inspect the result. A batch caller can therefore keep going when
        /// one recipient is bad.
        /// </summary>
        /// <param name="toEmail">Recipient address.</param>
        /// <param name="toName">Recipient display name, optional.</param>
        /// <param name="subject">Message subject.</param>
        /// <param name="htmlBody">HTML body.</param>
        /// <param name="plainTextBody">
        /// Plain-text alternative. Strongly recommended: HTML-only mail scores worse
        /// with spam filters, and these messages carry a link an employee must trust.
        /// </param>
        Task<MailDeliveryResult> SendAsync(
            string toEmail,
            string? toName,
            string subject,
            string htmlBody,
            string? plainTextBody = null,
            CancellationToken cancellationToken = default);
    }
}
