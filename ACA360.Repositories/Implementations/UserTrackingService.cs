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

        public async Task<UserPreferenceModel?> GetUserPreferenceAsync(string userId)
        {
            using var conn = new SqlConnection(_connectionString);
            return await conn.QuerySingleOrDefaultAsync<UserPreferenceModel>(
                "SELECT * FROM UserPreferences WHERE UserId = @UserId", new { UserId = userId });
        }
    }
}