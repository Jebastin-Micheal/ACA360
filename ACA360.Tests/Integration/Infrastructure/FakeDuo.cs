using ACA360.Core.Models;
using ACA360.Web.Infrastructure;

namespace ACA360.Tests.Integration.Infrastructure;

/// <summary>
/// Duo for the tests: nothing leaves the machine. Off (as with no Duo keys) unless a test turns it on;
/// then it sends the user to duo.example.test and approves the code "approved" for the user it started.
/// </summary>
public sealed class FakeDuo : IDuoTwoFactorService
{
    public bool Configured { get; set; }
    public bool Available { get; set; } = true;
    public string? Started { get; private set; }

    public bool IsConfigured => Configured;
    public bool IsRequiredFor(UserModel user) => user.IsMFA == 1;
    public bool MustBlockWhenUnavailable(UserModel user) => DuoTwoFactorService.IsAdmin(user);
    public Task<bool> IsAvailableAsync(string redirectUri) => Task.FromResult(Available);

    public (string Url, string State) Start(string username, string redirectUri)
    {
        Started = username;
        return ("https://duo.example.test/frame?redirect=" + Uri.EscapeDataString(redirectUri), "state-" + username);
    }

    public Task<bool> FinishAsync(string duoCode, string username, string redirectUri) =>
        Task.FromResult(duoCode == "approved" && username == Started);

    public void Reset()
    {
        Configured = false;
        Available = true;
        Started = null;
    }
}
