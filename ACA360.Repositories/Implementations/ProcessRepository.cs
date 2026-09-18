using ACA360.Core.Models.Tracker;
using ACA360.Repositories.Interfaces;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace ACA360.Repositories.Implementations
{
    public class ProcessRepository : IProcessRepository
    {
        private readonly string _connectionString;
        private readonly ILogger<ProcessRepository> _logger;

        /// <summary>
        /// Purpose: Initializes the ProcessRepository with configuration and logging.
        /// Input parameters: IConfiguration config, ILogger logger
        /// Output/return value: None
        /// </summary>
        public ProcessRepository(IConfiguration config, ILogger<ProcessRepository> logger)
        {
            try
            {
                _connectionString = config.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException(
                    "ConnectionStrings:DefaultConnection is missing from configuration."); ;
                _logger = logger;
            }
            catch (Exception ex)
            {
                if (logger != null)
                {
                    logger.LogError(ex, "Error occurred during initialization of ProcessRepository.");
                }
                throw;
            }
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        /// <summary>
        /// Purpose: Retrieves a list of active tracker categories.
        /// Input parameters: None
        /// Output/return value: Task of IEnumerable of TrackerCategory
        /// </summary>
        public async Task<IEnumerable<TrackerCategory>> GetCategoriesAsync()
        {
            try
            {
                using (var db = Connection)
                {
                    return await db.QueryAsync<TrackerCategory>(
                        "sp_Tracker_GetCategories",
                        commandType: CommandType.StoredProcedure);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetCategoriesAsync.");
                throw;
            }
        }

        /// <summary>
        /// Purpose: Retrieves a paginated and filtered list of tracker processes.
        /// Input parameters: string search, int? categoryId, string sortCol, string sortOrder, int page, int pageSize
        /// Output/return value: Task of (IEnumerable of TrackerProcess List, int TotalCount)
        /// </summary>
        public async Task<(IEnumerable<TrackerProcess> List, int TotalCount)> GetProcessListAsync(
            string search, int? categoryId, string sortCol, string sortOrder, int page, int pageSize)
        {
            try
            {
                using (var db = Connection)
                {
                    var p = new DynamicParameters();
                    p.Add("@SearchText", search);
                    p.Add("@CategoryId", categoryId);
                    p.Add("@SortColumn", sortCol);
                    p.Add("@SortOrder", sortOrder);
                    p.Add("@PageNumber", page);
                    p.Add("@PageSize", pageSize);

                    var result = await db.QueryAsync<TrackerProcess>("sp_Tracker_GetProcessList", p, commandType: CommandType.StoredProcedure);

                    int total = result.FirstOrDefault()?.TotalCount ?? 0;
                    return (result, total);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetProcessListAsync.");
                throw;
            }
        }

        /// <summary>
        /// Purpose: Retrieves a specific tracker process by its ID.
        /// Input parameters: int id
        /// Output/return value: Task of TrackerProcess
        /// </summary>
        public async Task<TrackerProcess> GetProcessByIdAsync(int id)
        {
            try
            {
                using (var db = Connection)
                {
                    return await db.QueryFirstOrDefaultAsync<TrackerProcess>(
                        "sp_Tracker_GetProcessById",
                        new { Id = id },
                        commandType: CommandType.StoredProcedure);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetProcessByIdAsync for Id: {Id}", id);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Adds a new tracker process or updates an existing one.
        /// Input parameters: TrackerProcess process
        /// Output/return value: Task
        /// </summary>
        public async Task AddUpdateProcessAsync(TrackerProcess process)
        {
            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                var p = new DynamicParameters();
                p.Add("@ProcessId", process.ProcessId);
                p.Add("@ProcessName", process.ProcessName);
                p.Add("@CategoryId", process.CategoryId);
                p.Add("@FollowUpDays", process.FollowUpDays);
                p.Add("@DisplayOrder", process.DisplayOrder);
                p.Add("@WebName", process.WebName);
                p.Add("@IsDoNotDisplay", process.IsDoNotDisplay);
                p.Add("@CompletionPercentage", process.CompletionPercentage);

                await db.ExecuteAsync("sp_Tracker_ManageProcess", p, transaction: tx, commandType: CommandType.StoredProcedure);

                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in AddUpdateProcessAsync for ProcessId: {ProcessId}", process?.ProcessId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Deletes a specific tracker process by its ID.
        /// Input parameters: int id
        /// Output/return value: Task
        /// </summary>
        public async Task DeleteProcessAsync(int id)
        {
            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                await db.ExecuteAsync("sp_Tracker_DeleteProcess", new { ProcessId = id }, transaction: tx, commandType: CommandType.StoredProcedure);

                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in DeleteProcessAsync for Id: {Id}", id);
                throw;
            }
        }
    }
}