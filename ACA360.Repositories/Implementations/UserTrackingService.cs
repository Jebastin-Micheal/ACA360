using ACA360.Core.Models;
using ACA360.Repositories.Interfaces;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Threading.Tasks;

namespace ACA360.Repositories.Implementations
{
    public class UserTrackingService : IUserTrackingService
    {
        private readonly string _connectionString;

        public UserTrackingService(string connectionString)
        {
            _connectionString = connectionString;
        }

        // --- PREFERENCES ---

        public async Task UpdateUserPreferenceAsync(string userId, int employerId, int year)
        {
            using var conn = new SqlConnection(_connectionString);
            var sql = @"
                MERGE UserPreferences AS target
                USING (SELECT @UserId AS UserId) AS source
                ON (target.UserId = source.UserId)
                WHEN MATCHED THEN
                    UPDATE SET LastSelectedEmployerId = @EmpId, LastSelectedFilingYear = @Year, LastUpdatedDate = GETDATE()
                WHEN NOT MATCHED THEN
                    INSERT (UserId, LastSelectedEmployerId, LastSelectedFilingYear, LastUpdatedDate)
                    VALUES (@UserId, @EmpId, @Year, GETDATE());";

            await conn.ExecuteAsync(sql, new { UserId = userId, EmpId = employerId, Year = year });
        }

        public async Task<UserPreferenceModel> GetUserPreferenceAsync(string userId)
        {
            using var conn = new SqlConnection(_connectionString);
            return await conn.QuerySingleOrDefaultAsync<UserPreferenceModel>(
                "SELECT * FROM UserPreferences WHERE UserId = @UserId", new { UserId = userId });
        }

        // --- ACTIVITY TRACKING ---

        public async Task<int> LogLoginAsync(string userId, string ip, string userAgent, string sessionId, string originalUserId = null)
        {
            using var conn = new SqlConnection(_connectionString);
            var sql = @"
                INSERT INTO UserLoginHistory (UserId, SessionId, LoginTime, LastActiveTime, IPAddress, UserAgent, OriginalUserId)
                VALUES (@UserId, @SessionId, GETDATE(), GETDATE(), @IP, @UserAgent, @OriginalUserId);
                SELECT CAST(SCOPE_IDENTITY() as int);";

            return await conn.ExecuteScalarAsync<int>(sql, new { UserId = userId, IP = ip, UserAgent = userAgent, SessionId = sessionId, OriginalUserId = originalUserId });
        }

        public async Task LogLogoutAsync(int logId)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.ExecuteAsync("UPDATE UserLoginHistory SET LogoutTime = GETDATE() WHERE LogId = @Id", new { Id = logId });
        }

        public async Task UpdateLastActiveAsync(int logId)
        {
            using var conn = new SqlConnection(_connectionString);
            // Optimization: Only update if LastActiveTime is older than 2 minutes to reduce DB noise
            await conn.ExecuteAsync(@"
                UPDATE UserLoginHistory 
                SET LastActiveTime = GETDATE() 
                WHERE LogId = @Id AND LastActiveTime < DATEADD(minute, -2, GETDATE())",
                new { Id = logId });
        }
    }
}