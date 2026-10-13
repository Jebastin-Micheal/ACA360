using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ACA360.BusinessLogic.Services
{
    /// <summary>
    /// Sends mail through SendGrid's v3 Mail Send API.
    ///
    /// <para>
    /// Talks to the REST endpoint directly rather than taking the official SendGrid
    /// NuGet package. The call is a single POST with a JSON body, so the package
    /// would add a dependency and a version to track for very little. Everything
    /// this needs — HttpClient, System.Text.Json — is already present. If the SDK is
    /// wanted later for attachments or scheduling, it drops in behind
    /// <see cref="IMailDeliveryService"/> without touching any caller.
    /// </para>
    ///
    /// <para>
    /// Configuration, in the same place as the connection string and the encryption
    /// key — User Secrets in development, environment or key vault in production.
    /// The API key is a credential and does not belong in the settings table beside
    /// SMTP_Pass, which is stored in clear.
    /// </para>
    /// <code>
    /// "SendGrid": {
    ///   "ApiKey":    "SG.xxxxx",
    ///   "FromEmail": "no-reply@yourdomain.com",   // must be a verified sender
    ///   "FromName":  "ACA360",
    ///   "ReplyTo":   "support@yourdomain.com",    // optional
    ///   "Enabled":   true                          // optional kill switch
    /// }
    /// </code>
    /// </summary>
    public class SendGridMailDeliveryService : IMailDeliveryService
    {
        private const string SendEndpoint = "https://api.sendgrid.com/v3/mail/send";

        private readonly HttpClient _http;
        private readonly ILogger<SendGridMailDeliveryService> _logger;

        private readonly string? _apiKey;
        private readonly string? _fromEmail;
        private readonly string _fromName;
        private readonly string? _replyTo;
        private readonly bool _enabled;

        public SendGridMailDeliveryService(
            HttpClient http,
            IConfiguration config,
            ILogger<SendGridMailDeliveryService> logger)
        {
            _http = http;
            _logger = logger;

            _apiKey    = config["SendGrid:ApiKey"];
            _fromEmail = config["SendGrid:FromEmail"];
            _fromName  = config["SendGrid:FromName"] ?? "ACA360";
            _replyTo   = config["SendGrid:ReplyTo"];

            // Absent means enabled, so configuring a key is enough to switch mail on.
            _enabled = !bool.TryParse(config["SendGrid:Enabled"], out var on) || on;

            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                _http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _apiKey);
            }
        }

        public string ProviderName => "SendGrid";

        public bool IsConfigured =>
            _enabled
            && !string.IsNullOrWhiteSpace(_apiKey)
            && !string.IsNullOrWhiteSpace(_fromEmail);

        public async Task<MailDeliveryResult> SendAsync(
            string toEmail,
            string? toName,
            string subject,
            string htmlBody,
            string? plainTextBody = null,
            CancellationToken cancellationToken = default)
        {
            if (!_enabled)
                return MailDeliveryResult.Disabled("Mail sending is switched off (SendGrid:Enabled = false).");

            if (string.IsNullOrWhiteSpace(_apiKey))
                return MailDeliveryResult.Disabled("SendGrid:ApiKey is not configured.");

            if (string.IsNullOrWhiteSpace(_fromEmail))
                return MailDeliveryResult.Disabled("SendGrid:FromEmail is not configured.");

            if (!LooksLikeEmail(toEmail))
                return MailDeliveryResult.Skipped(
                    string.IsNullOrWhiteSpace(toEmail)
                        ? "Recipient has no email address on file."
                        : $"Recipient address is not usable: '{Redact(toEmail)}'.");

            // SendGrid requires the plain-text part before the HTML part.
            var content = new List<object>();
            if (!string.IsNullOrWhiteSpace(plainTextBody))
                content.Add(new { type = "text/plain", value = plainTextBody });
            content.Add(new { type = "text/html", value = htmlBody });

            var payload = new Dictionary<string, object?>
            {
                ["personalizations"] = new[]
                {
                    new { to = new[] { new { email = toEmail, name = string.IsNullOrWhiteSpace(toName) ? null : toName } } }
                },
                ["from"]    = new { email = _fromEmail, name = _fromName },
                ["subject"] = subject,
                ["content"] = content
            };

            if (!string.IsNullOrWhiteSpace(_replyTo))
                payload["reply_to"] = new { email = _replyTo };

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, SendEndpoint)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(payload,
                            new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }),
                        Encoding.UTF8,
                        "application/json")
                };

                using var response = await _http.SendAsync(request, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var messageId = response.Headers.TryGetValues("X-Message-Id", out var ids)
                        ? ids.FirstOrDefault()
                        : null;

                    _logger.LogInformation(
                        "SendGrid accepted mail for {Recipient}. MessageId {MessageId}.",
                        Redact(toEmail), messageId ?? "(none)");

                    return MailDeliveryResult.Sent(messageId, (int)response.StatusCode);
                }

                // SendGrid returns { "errors": [ { "message": ..., "field": ... } ] }.
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var detail = SummariseError(body, response.StatusCode.ToString());

                _logger.LogError(
                    "SendGrid rejected mail for {Recipient}. HTTP {Status}. {Detail}",
                    Redact(toEmail), (int)response.StatusCode, detail);

                return MailDeliveryResult.Failed(detail, (int)response.StatusCode);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return MailDeliveryResult.Failed("Send was cancelled.");
            }
            catch (Exception ex)
            {
                // Network fault, DNS, TLS, timeout. Never rethrow: a batch must survive
                // one bad send, and the caller records the outcome either way.
                _logger.LogError(ex, "SendGrid call failed for {Recipient}.", Redact(toEmail));
                return MailDeliveryResult.Failed($"Could not reach SendGrid: {ex.Message}");
            }
        }

        /// <summary>
        /// Pulls the useful sentence out of SendGrid's error envelope. Falls back to the
        /// raw body, truncated, when the shape is not what we expect.
        /// </summary>
        private static string SummariseError(string body, string fallback)
        {
            if (string.IsNullOrWhiteSpace(body)) return fallback;

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("errors", out var errors)
                    && errors.ValueKind == JsonValueKind.Array)
                {
                    var messages = errors.EnumerateArray()
                        .Select(e => e.TryGetProperty("message", out var m) ? m.GetString() : null)
                        .Where(m => !string.IsNullOrWhiteSpace(m))
                        .ToList();

                    if (messages.Count > 0)
                        return Truncate(string.Join("; ", messages), 380);
                }
            }
            catch (JsonException)
            {
                // Not JSON. Fall through to the raw body.
            }

            return Truncate(body, 380);
        }

        private static string Truncate(string value, int max) =>
            value.Length <= max ? value : value[..max] + "…";

        /// <summary>
        /// Very deliberately not a full RFC 5322 validator. It only rejects what would
        /// certainly fail, so a slightly odd but real address still gets its chance and
        /// SendGrid gives the authoritative answer.
        /// </summary>
        private static bool LooksLikeEmail(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;

            var trimmed = value.Trim();
            var at = trimmed.IndexOf('@');

            return at > 0
                && at < trimmed.Length - 1
                && trimmed.IndexOf('@', at + 1) < 0
                && trimmed.IndexOf('.', at) > at + 1
                && !trimmed.Any(char.IsWhiteSpace);
        }

        /// <summary>
        /// Masks the local part so an employee's address is not spread across log files
        /// that a wider group can read. The domain stays, which is what is useful when
        /// diagnosing a delivery problem.
        /// </summary>
        private static string Redact(string? email)
        {
            if (string.IsNullOrWhiteSpace(email)) return "(none)";

            var at = email.IndexOf('@');
            if (at <= 0) return "(invalid)";

            var local = email[..at];
            var shown = local.Length <= 2 ? local[..1] : local[..2];

            return shown + new string('*', Math.Max(1, local.Length - shown.Length)) + email[at..];
        }
    }
}
