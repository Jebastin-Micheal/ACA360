using System.Collections.Concurrent;
using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Models;

namespace ACA360.Tests.Integration.Infrastructure;

/// <summary>
/// Stands in for SendGrid in every integration test, so no test can send a real e-mail.
/// Keeps what would have been sent.
/// </summary>
public sealed class FakeMail : IMailDeliveryService
{
    public sealed record Message(string To, string Subject, string Html, string? Text);

    public ConcurrentQueue<Message> Sent { get; } = new();

    public bool IsConfigured => true;

    public string ProviderName => "Test";

    public Task<MailDeliveryResult> SendAsync(string toEmail, string? toName, string subject, string htmlBody,
        string? plainTextBody = null, CancellationToken cancellationToken = default)
    {
        Sent.Enqueue(new Message(toEmail, subject, htmlBody, plainTextBody));
        return Task.FromResult(MailDeliveryResult.Sent("test-" + Guid.NewGuid().ToString("N"), 202));
    }

    public IEnumerable<Message> To(string email) =>
        Sent.Where(m => string.Equals(m.To, email, StringComparison.OrdinalIgnoreCase));
}
