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
    public class SalesRepRepository : ISalesRepRepository
    {
        private readonly string _connectionString;
        private readonly ILogger<SalesRepRepository> _logger;

        /// <summary>
        /// Purpose: Initializes the SalesRepRepository with configuration and logging.
        /// Input parameters: IConfiguration config, ILogger logger
        /// Output/return value: None
        /// </summary>
        public SalesRepRepository(IConfiguration config, ILogger<SalesRepRepository> logger)
        {
            try
            {
                _connectionString = config.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException(
                    "ConnectionStrings:DefaultConnection is missing from configuration.");
                _logger = logger;
            }
            catch (Exception ex)
            {
                if (logger != null)
                {
                    logger.LogError(ex, "Error occurred during SalesRepRepository initialization.");
                }
                throw;
            }
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        /// <summary>
        /// Purpose: Retrieves a paginated and filtered list of sales representatives.
        /// Input parameters: string search, string sortCol, string sortOrder, int page, int pageSize
        /// Output/return value: Task of (IEnumerable of SalesRepItem List, int TotalCount)
        /// </summary>
        public async Task<(IEnumerable<SalesRepItem> List, int TotalCount)> GetSalesRepListAsync(
            string search, string sortCol, string sortOrder, int page, int pageSize)
        {
            try
            {
                using (var db = Connection)
                {
                    var p = new DynamicParameters();
                    p.Add("@Search", search);
                    p.Add("@SortCol", sortCol);
                    p.Add("@SortOrder", sortOrder);
                    p.Add("@Page", page);
                    p.Add("@PageSize", pageSize);

                    var result = await db.QueryAsync<SalesRepItem>("sp_GetSalesRepList", p, commandType: CommandType.StoredProcedure);
                    int total = result.FirstOrDefault()?.TotalCount ?? 0;
                    return (result, total);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetSalesRepListAsync.");
                throw;
            }
        }

        /// <summary>
        /// Purpose: Retrieves a specific sales representative by their ID.
        /// Input parameters: int id
        /// Output/return value: Task of SalesRepItem
        /// </summary>
        public async Task<SalesRepItem> GetSalesRepByIdAsync(int id)
        {
            try
            {
                using (var db = Connection)
                {
                    return await db.QueryFirstOrDefaultAsync<SalesRepItem>(
                        "sp_GetSalesRepById",
                        new { SalesRepId = id },
                        commandType: CommandType.StoredProcedure);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetSalesRepByIdAsync for Id: {Id}", id);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Adds a new sales representative or updates an existing one.
        /// Input parameters: SalesRepItem salesRep
        /// Output/return value: Task
        /// </summary>
        public async Task AddUpdateSalesRepAsync(SalesRepItem salesRep)
        {
            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                var p = new DynamicParameters();
                p.Add("@SalesRepId", salesRep.SalesRepId);
                p.Add("@SalesRepName", salesRep.SalesRepName);
                p.Add("@IsActive", salesRep.IsActive);

                await db.ExecuteAsync("sp_AddUpdateSalesRep", p, transaction: tx, commandType: CommandType.StoredProcedure);

                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in AddUpdateSalesRepAsync for SalesRepId: {SalesRepId}", salesRep?.SalesRepId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Deletes a specific sales representative by their ID.
        /// Input parameters: int id
        /// Output/return value: Task
        /// </summary>
        public async Task DeleteSalesRepAsync(int id)
        {
            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                await db.ExecuteAsync("sp_DeleteSalesRep", new { SalesRepId = id }, transaction: tx, commandType: CommandType.StoredProcedure);

                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in DeleteSalesRepAsync for Id: {Id}", id);
                throw;
            }
        }
    }
}