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
    public class ServiceRepository : IServiceRepository
    {
        private readonly string _connectionString;
        private readonly ILogger<ServiceRepository> _logger;

        /// <summary>
        /// Purpose: Initializes the ServiceRepository with configuration and logging.
        /// Input parameters: IConfiguration config, ILogger logger
        /// Output/return value: None
        /// </summary>
        public ServiceRepository(IConfiguration config, ILogger<ServiceRepository> logger)
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
                    logger.LogError(ex, "Error occurred during ServiceRepository initialization.");
                }
                throw;
            }
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        /// <summary>
        /// Purpose: Retrieves a paginated and filtered list of tracker services.
        /// Input parameters: string search, int? taxYear, string sortCol, string sortOrder, int page, int pageSize
        /// Output/return value: Task of (IEnumerable of ServiceItem List, int TotalCount)
        /// </summary>
        public async Task<(IEnumerable<ServiceItem> List, int TotalCount)> GetServiceListAsync(
            string search, string sortCol, string sortOrder, int page, int pageSize)
        {
            try
            {
                using (var db = Connection)
                {
                    var p = new DynamicParameters();
                    p.Add("@SearchText", search);
                    p.Add("@SortColumn", sortCol);
                    p.Add("@SortOrder", sortOrder);
                    p.Add("@PageNumber", page);
                    p.Add("@PageSize", pageSize);

                    var result = await db.QueryAsync<ServiceItem>("sp_Tracker_GetServiceList", p, commandType: CommandType.StoredProcedure);
                    int total = result.FirstOrDefault()?.TotalCount ?? 0;
                    return (result, total);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetServiceListAsync.");
                throw;
            }
        }

        /// <summary>
        /// Purpose: Retrieves a specific service catalog item by its ID.
        /// Input parameters: int id
        /// Output/return value: Task of ServiceItem
        /// </summary>
        public async Task<ServiceItem> GetServiceByIdAsync(int id)
        {
            try
            {
                using (var db = Connection)
                {
                    return await db.QueryFirstOrDefaultAsync<ServiceItem>(
                        "sp_Tracker_GetServiceById",
                        new { Id = id },
                        commandType: CommandType.StoredProcedure);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetServiceByIdAsync for Id: {Id}", id);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Adds a new service item or updates an existing one.
        /// Input parameters: ServiceItem service
        /// Output/return value: Task
        /// </summary>
        public async Task AddUpdateServiceAsync(ServiceItem service)
        {
            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                var p = new DynamicParameters();
                p.Add("@ServiceId", service.ServiceId);
                p.Add("@ServiceName", service.ServiceName);
                p.Add("@Description", service.Description);
                p.Add("@IsActive", service.IsActive);

                await db.ExecuteAsync("sp_Tracker_ManageService", p, transaction: tx, commandType: CommandType.StoredProcedure);

                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in AddUpdateServiceAsync for ServiceId: {ServiceId}", service?.ServiceId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Deletes a specific service item by its ID.
        /// Input parameters: int id
        /// Output/return value: Task
        /// </summary>
        public async Task DeleteServiceAsync(int id)
        {
            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                await db.ExecuteAsync("sp_Tracker_DeleteService", new { ServiceId = id }, transaction: tx, commandType: CommandType.StoredProcedure);

                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in DeleteServiceAsync for Id: {Id}", id);
                throw;
            }
        }
    }
}