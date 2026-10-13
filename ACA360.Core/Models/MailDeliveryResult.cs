using System;

namespace ACA360.Core.Models
{
    /// <summary>
    /// Outcome of one attempt to hand a message to the mail provider.
    ///
    /// Deliberately a result rather than an exception: a single employee's address
    /// being rejected must not abort a batch of five hundred, and the caller needs
    /// the outcome to record against the communication log rather than a stack
    /// trace. Genuine faults — a missing API key, the provider unreachable — still
    /// come back as a failed result with <see cref="Detail"/> saying which.
    /// </summary>
    public class MailDeliveryResult
    {
        /// <summary>The provider accepted the message for delivery.</summary>
        public bool Success { get; init; }

        /// <summary>
        /// Short status suitable for CommunicationLog.Status: Sent, Failed,
        /// Skipped or Disabled. Kept within the column's 50 characters.
        /// </summary>
        public string Status { get; init; } = "Failed";

        /// <summary>Human-readable reason. Never contains the API key.</summary>
        public string? Detail { get; init; }

        /// <summary>Provider message id, when one was returned. Useful for support tickets.</summary>
        public string? ProviderMessageId { get; init; }

        /// <summary>HTTP status from the provider, when the call was actually made.</summary>
        public int? HttpStatus { get; init; }

        public static MailDeliveryResult Sent(string? messageId, int httpStatus) => new()
        {
            Success = true,
            Status = "Sent",
            ProviderMessageId = messageId,
            HttpStatus = httpStatus,
            Detail = "Accepted by provider"
        };

        public static MailDeliveryResult Failed(string detail, int? httpStatus = null) => new()
        {
            Success = false,
            Status = "Failed",
            Detail = detail,
            HttpStatus = httpStatus
        };

        /// <summary>Nothing was attempted because there was no usable address.</summary>
        public static MailDeliveryResult Skipped(string detail) => new()
        {
            Success = false,
            Status = "Skipped",
            Detail = detail
        };

        /// <summary>Sending is switched off, or no provider is configured.</summary>
        public static MailDeliveryResult Disabled(string detail) => new()
        {
            Success = false,
            Status = "Disabled",
            Detail = detail
        };
    }
}
