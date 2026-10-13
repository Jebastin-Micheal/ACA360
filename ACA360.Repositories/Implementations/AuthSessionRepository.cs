using ACA360.Repositories.Interfaces;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Threading.Tasks;

namespace ACA360.Repositories.Implementations
{
    public class AuthSessionRepository : IAuthSessionRepository
    {
        private readonly string _connectionString;

        public AuthSessionRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task CreateAsync(Guid sessionId, string subject, string? ipAddress, string? userAgent)
        {
            using var db = new SqlConnection(_connectionString);
            await db.ExecuteAsync("dbo.sp_AuthSession_Create",
                new { SessionId = sessionId, Subject = subject, IpAddress = ipAddress, UserAgent = userAgent },
                commandType: CommandType.StoredProcedure);
        }

        public async Task<bool> ValidateAsync(Guid sessionId, int idleMinutes, bool touch)
        {
            using var db = new SqlConnection(_connectionString);
            return await db.ExecuteScalarAsync<bool>("dbo.sp_AuthSession_Validate",
                new { SessionId = sessionId, IdleMinutes = idleMinutes, Touch = touch },
                commandType: CommandType.StoredProcedure);
        }

        public async Task RevokeAsync(Guid sessionId)
        {
            using var db = new SqlConnection(_connectionString);
            await db.ExecuteAsync("dbo.sp_AuthSession_Revoke",
                new { SessionId = sessionId },
                commandType: CommandType.StoredProcedure);
        }

        public async Task<System.Collections.Generic.List<AuthSessionInfo>> ListLiveAsync(string subject, int idleMinutes)
        {
            using var db = new SqlConnection(_connectionString);
            return System.Linq.Enumerable.ToList(await db.QueryAsync<AuthSessionInfo>("dbo.sp_AuthSession_ListForSubject",
                new { Subject = subject, IdleMinutes = idleMinutes }, commandType: CommandType.StoredProcedure));
        }

        public async Task<int> RevokeOthersAsync(string subject, Guid keepSessionId)
        {
            using var db = new SqlConnection(_connectionString);
            return await db.ExecuteScalarAsync<int>("dbo.sp_AuthSession_RevokeOthers",
                new { Subject = subject, KeepSessionId = keepSessionId }, commandType: CommandType.StoredProcedure);
        }

        public async Task PurgeAsync(int olderThanDays)
        {
            using var db = new SqlConnection(_connectionString);
            await db.ExecuteAsync("dbo.sp_AuthSession_Purge",
                new { OlderThanDays = olderThanDays },
                commandType: CommandType.StoredProcedure);
        }
    }
}
