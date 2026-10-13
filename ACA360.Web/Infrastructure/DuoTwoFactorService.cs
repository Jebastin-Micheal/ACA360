using ACA360.Core.Constants;
using ACA360.Core.Models;
using DuoUniversal;

namespace ACA360.Web.Infrastructure
{
    /// <summary>
    /// The "Duo" configuration section: a Duo "Web SDK" application. Keep the secret out of the
    /// repo (user secrets on a developer machine, the server's configuration in production).
    /// Duo is off until ClientId, ClientSecret and ApiHost are all set.
    /// </summary>
    public sealed class DuoSettings
    {
        public const string Section = "Duo";
        public string? ClientId { get; set; }
        public string? ClientSecret { get; set; }
        /// <summary>The API hostname, e.g. api-xxxxxxxx.duosecurity.com.</summary>
        public string? ApiHost { get; set; }
        /// <summary>Optional. Where Duo sends the user back; by default this site's /Account/DuoCallback.</summary>
        public string? RedirectUri { get; set; }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId)
                                    && !string.IsNullOrWhiteSpace(ClientSecret)
                                    && !string.IsNullOrWhiteSpace(ApiHost);
    }

    /// <summary>Who must pass Duo, and the Universal Prompt round trip.</summary>
    public interface IDuoTwoFactorService
    {
        bool IsConfigured { get; }
        /// <summary>Whoever turned it on (tbl_User.isMFA = 1): each user decides, administrators included.</summary>
        bool IsRequiredFor(UserModel user);
        /// <summary>Admins may not sign in while Duo cannot be reached; others get through (logged).</summary>
        bool MustBlockWhenUnavailable(UserModel user);
        Task<bool> IsAvailableAsync(string redirectUri);
        /// <summary>The Duo address to send the user to, and the state to check when they come back.</summary>
        (string Url, string State) Start(string username, string redirectUri);
        /// <summary>True when Duo confirms this user passed the second factor.</summary>
        Task<bool> FinishAsync(string duoCode, string username, string redirectUri);
    }

    public sealed class DuoTwoFactorService : IDuoTwoFactorService
    {
        private readonly DuoSettings _settings;

        public DuoTwoFactorService(DuoSettings settings) => _settings = settings;

        public bool IsConfigured => _settings.IsConfigured;

        public static bool IsAdmin(UserModel user) =>
            user.Role_Name == UserRoles.SuperAdmin || user.Role_Name == UserRoles.Admin;

        public bool IsRequiredFor(UserModel user) => user.IsMFA == 1;

        public bool MustBlockWhenUnavailable(UserModel user) => IsAdmin(user);

        private Client Build(string redirectUri) =>
            new ClientBuilder(_settings.ClientId, _settings.ClientSecret, _settings.ApiHost,
                string.IsNullOrWhiteSpace(_settings.RedirectUri) ? redirectUri : _settings.RedirectUri).Build();

        public async Task<bool> IsAvailableAsync(string redirectUri)
        {
            if (!IsConfigured) return false;
            try { return await Build(redirectUri).DoHealthCheck(false); }
            catch { return false; }
        }

        public (string Url, string State) Start(string username, string redirectUri)
        {
            var client = Build(redirectUri);
            var state = Client.GenerateState();
            return (client.GenerateAuthUri(username, state), state);
        }

        public async Task<bool> FinishAsync(string duoCode, string username, string redirectUri)
        {
            // Throws DuoException when the code is bad, expired, or for another user.
            var token = await Build(redirectUri).ExchangeAuthorizationCodeFor2faResult(duoCode, username);
            return token != null && string.Equals(token.Username, username, StringComparison.OrdinalIgnoreCase)
                   && (token.AuthResult == null || string.Equals(token.AuthResult.Result, "allow", StringComparison.OrdinalIgnoreCase));
        }
    }
}
