using System.Data;
using ACA360.Core.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace ACA360.BusinessLogic.Services
{
    /// <summary>Filing deadlines (dbo.FilingDeadlines, script 063).</summary>
    public interface IFilingDeadlineService
    {
        Task<List<FilingDeadline>> ListAsync(int? filingYear = null);
        Task<List<FilingDeadline>> UpcomingAsync(int top = 5, DateTime? from = null);
        /// <summary>Adds (id 0) or changes a deadline; returns its id, 0 when it no longer exists.</summary>
        Task<int> SaveAsync(FilingDeadline deadline, string? modifiedBy);
        Task<bool> DeleteAsync(int id, string? modifiedBy);
    }

    public class FilingDeadlineService : IFilingDeadlineService
    {
        private readonly string _connectionString;

        public FilingDeadlineService(string connectionString) => _connectionString = connectionString;

        private IDbConnection Connection => new SqlConnection(_connectionString);

        public async Task<List<FilingDeadline>> ListAsync(int? filingYear = null)
        {
            using var db = Connection;
            return (await db.QueryAsync<FilingDeadline>("dbo.sp_FilingDeadlines_List", new { FilingYear = filingYear },
                commandType: CommandType.StoredProcedure)).ToList();
        }

        public async Task<List<FilingDeadline>> UpcomingAsync(int top = 5, DateTime? from = null)
        {
            using var db = Connection;
            return (await db.QueryAsync<FilingDeadline>("dbo.sp_FilingDeadlines_Upcoming", new { From = from?.Date, Top = top },
                commandType: CommandType.StoredProcedure)).ToList();
        }

        public async Task<int> SaveAsync(FilingDeadline d, string? modifiedBy)
        {
            using var db = Connection;
            return await db.ExecuteScalarAsync<int>("dbo.sp_FilingDeadlines_Save", new
            {
                d.FilingDeadlineId, d.FilingYear, d.Jurisdiction, d.Kind, d.Title, DueDate = d.DueDate.Date, d.Notes, ModifiedBy = modifiedBy,
            }, commandType: CommandType.StoredProcedure);
        }

        public async Task<bool> DeleteAsync(int id, string? modifiedBy)
        {
            using var db = Connection;
            return await db.ExecuteScalarAsync<int>("dbo.sp_FilingDeadlines_Delete", new { FilingDeadlineId = id, ModifiedBy = modifiedBy },
                commandType: CommandType.StoredProcedure) == 1;
        }
    }
}
