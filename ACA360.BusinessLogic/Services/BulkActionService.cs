using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace ACA360.BusinessLogic.Services
{
    /// <summary>A staff member employers can be assigned to (the assignment screens key them by RefId).</summary>
    public sealed class AssignableStaff
    {
        public int RefId { get; set; }
        public int RoleId { get; set; }
        public string? UserName { get; set; }
        public string? Name { get; set; }
        public string? RoleName { get; set; }
    }

    /// <summary>The parts of the bulk actions (script 074) that need the database.</summary>
    public interface IBulkActionService
    {
        /// <summary>Locks or unlocks the employees' codes for the year; returns how many changed.</summary>
        Task<int> SetCodesLockedAsync(IReadOnlyCollection<int> employeeIds, int year, bool locked, int userId, string? userName);
        /// <summary>"AM" (account managers) or "DA" (data analysts).</summary>
        Task<List<AssignableStaff>> AssignableStaffAsync(string side);
    }

    public class BulkActionService : IBulkActionService
    {
        private readonly string _connectionString;

        public BulkActionService(string connectionString) => _connectionString = connectionString;

        public async Task<int> SetCodesLockedAsync(IReadOnlyCollection<int> employeeIds, int year, bool locked, int userId, string? userName)
        {
            if (employeeIds.Count == 0) return 0;
            using var db = new SqlConnection(_connectionString);
            return await db.ExecuteScalarAsync<int>("dbo.sp_EmployeeCodes_SetLocked", new
            {
                EmployeeIds = System.Text.Json.JsonSerializer.Serialize(employeeIds),
                Year = year, Locked = locked, UserId = userId, UserName = userName,
            }, commandType: CommandType.StoredProcedure);
        }

        public async Task<List<AssignableStaff>> AssignableStaffAsync(string side)
        {
            using var db = new SqlConnection(_connectionString);
            return (await db.QueryAsync<AssignableStaff>("dbo.sp_AssignableStaff", new { Side = side == "DA" ? "DA" : "AM" },
                commandType: CommandType.StoredProcedure)).ToList();
        }
    }
}
