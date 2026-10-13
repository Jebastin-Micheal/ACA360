using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ACA360.Repositories.Interfaces
{
    public sealed class AuthSessionInfo
    {
        public Guid SessionId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastActivityAt { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
    }

    public interface IAuthSessionRepository
    {
        Task CreateAsync(Guid sessionId, string subject, string? ipAddress, string? userAgent);
        Task<bool> ValidateAsync(Guid sessionId, int idleMinutes, bool touch);
        Task RevokeAsync(Guid sessionId);
        Task PurgeAsync(int olderThanDays);
        /// <summary>The subject's live sessions, most recently used first (script 075).</summary>
        Task<List<AuthSessionInfo>> ListLiveAsync(string subject, int idleMinutes);
        /// <summary>Signs out every other session of the subject; returns how many.</summary>
        Task<int> RevokeOthersAsync(string subject, Guid keepSessionId);
    }
}
