using System.Data;
using ACA360.Core.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace ACA360.BusinessLogic.Services
{
    /// <summary>Employee change history (script 068) and the employer timeline (script 069).</summary>
    public interface IChangeHistoryService
    {
        Task<List<EmployeeHistoryRow>> EmployeeHistoryAsync(long employeeId, int top = 500);
        Task<List<EmployerTimelineEvent>> EmployerTimelineAsync(long employerId, int top = 300);
    }

    public class ChangeHistoryService : IChangeHistoryService
    {
        private readonly string _connectionString;

        public ChangeHistoryService(string connectionString) => _connectionString = connectionString;

        public async Task<List<EmployeeHistoryRow>> EmployeeHistoryAsync(long employeeId, int top = 500)
        {
            using var db = new SqlConnection(_connectionString);
            return (await db.QueryAsync<EmployeeHistoryRow>("dbo.sp_EmployeeChangeHistory", new { EmployeeId = employeeId, Top = top },
                commandType: CommandType.StoredProcedure)).ToList();
        }

        public async Task<List<EmployerTimelineEvent>> EmployerTimelineAsync(long employerId, int top = 300)
        {
            using var db = new SqlConnection(_connectionString);
            return (await db.QueryAsync<EmployerTimelineEvent>("dbo.sp_EmployerTimeline", new { EmployerId = employerId, Top = top },
                commandType: CommandType.StoredProcedure)).ToList();
        }
    }
}
