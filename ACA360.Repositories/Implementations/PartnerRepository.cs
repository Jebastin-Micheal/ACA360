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
using ACA360.Core.Models;

namespace ACA360.Repositories.Implementations
{
    public class PartnerRepository : IPartnerRepository
    {
        private readonly string _connStr;
        private readonly ILogger<PartnerRepository> _logger;

        /// <summary>
        /// Purpose: Initializes the PartnerRepository with database configuration and logging.
        /// Input parameters: IConfiguration config, ILogger logger
        /// Output/return value: None
        /// </summary>
        public PartnerRepository(IConfiguration config, ILogger<PartnerRepository> logger)
        {
            try
            {
                _connStr = config.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException(
                    "ConnectionStrings:DefaultConnection is missing from configuration."); ;
                _logger = logger;
            }
            catch (Exception ex)
            {
                if (logger != null)
                {
                    logger.LogError(ex, "Error initializing PartnerRepository.");
                }
                throw;
            }
        }

        private IDbConnection Db => new SqlConnection(_connStr);

        // --- FIRM METHODS ---
        //public async Task<(IEnumerable<FirmItem>, int)> GetFirmsAsync(string search, string sortCol, string sortOrder, int page, int pageSize)
        //{
        //    using (var db = Db)
        //    {
        //        var p = new { SearchText = search, SortColumn = sortCol, SortOrder = sortOrder, PageNumber = page, PageSize = pageSize };
        //        var res = await db.QueryAsync<FirmItem>("sp_Tracker_GetFirmList", p, commandType: CommandType.StoredProcedure);
        //        return (res, res.FirstOrDefault()?.TotalCount ?? 0);
        //    }
        //}

        /// <summary>
        /// Purpose: Retrieves a paginated and filtered list of firms.
        /// Input parameters: string search, string sortCol, string sortOrder, int page, int pageSize
        /// Output/return value: Tuple containing IEnumerable of FirmItem and total count
        /// </summary>
        public async Task<(IEnumerable<FirmItem> List, int TotalCount)> GetFirmsAsync(string search, string sortCol, string sortOrder, int page, int pageSize)
        {
            try
            {
                using (var db = Db)
                {
                    var p = new
                    {
                        SearchText = search,
                        SortColumn = sortCol,
                        SortOrder = sortOrder,
                        PageNumber = page,
                        PageSize = pageSize
                    };

                    // 1. Query as dynamic to bypass strict mapping for a second
                    var rawData = await db.QueryAsync<dynamic>("sp_Tracker_GetFirmList", p, commandType: CommandType.StoredProcedure);

                    // 2. Manually map to FirmItem to ensure BrokerCount is cast correctly
                    var list = rawData.Select(d => new FirmItem
                    {
                        FirmId = d.FirmId,
                        FirmName = d.FirmName,
                        City = d.City,
                        State = d.State,
                        IsActive = d.IsActive,
                        // FORCE CASTING: This fixes the '0' issue if SQL returns BIGINT
                        BrokerCount = (int)(d.BrokerCount ?? 0),
                        TotalCount = (int)(d.TotalCount ?? 0)
                    }).ToList();

                    return (list, list.FirstOrDefault()?.TotalCount ?? 0);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetFirmsAsync.");
                throw;
            }
        }

        /// <summary>
        /// Purpose: Retrieves a list of all available states.
        /// Input parameters: None
        /// Output/return value: Task of IEnumerable of StateModel
        /// </summary>
        public async Task<IEnumerable<StateModel>> GetAllStatesAsync()
        {
            try
            {
                using (var db = Db)
                {
                    var result = await db.QueryAsync<StateModel>(
                        "sp_GetAllStates",
                        commandType: CommandType.StoredProcedure);

                    // Return empty list if result is null
                    return result ?? new List<StateModel>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetAllStatesAsync.");
                throw;
            }
        }

        /// <summary>
        /// Purpose: Retrieves a specific firm by its ID.
        /// Input parameters: int id
        /// Output/return value: Task of FirmItem
        /// </summary>
        public async Task<FirmItem> GetFirmByIdAsync(int id)
        {
            try
            {
                using (var db = Db)
                {
                    return await db.QueryFirstOrDefaultAsync<FirmItem>(
                        "sp_GetFirmById",
                        new { Id = id },
                        commandType: CommandType.StoredProcedure);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetFirmByIdAsync for Id: {Id}", id);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Saves (inserts or updates) a firm record.
        /// Input parameters: FirmItem firm
        /// Output/return value: Task
        /// </summary>
        public async Task<int> SaveFirmAsync(FirmItem firm)
        {
            using var db = Db;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                var p = new DynamicParameters();
                p.Add("@FirmId", firm.FirmId);
                p.Add("@FirmName", firm.FirmName);
                p.Add("@City", firm.City);
                p.Add("@Address", firm.Address);
                p.Add("@Zip", firm.Zip);
                p.Add("@StateId", firm.StateId);
                p.Add("@State", firm.State);
                p.Add("@IsActive", firm.IsActive);
                p.Add("@NewId", dbType: DbType.Int32, direction: ParameterDirection.Output);
                await db.ExecuteAsync(
                    "sp_Tracker_ManageFirm",
                    p,
                    transaction: tx,
                    commandType: CommandType.StoredProcedure);

                tx.Commit();
                return p.Get<int>("@NewId");
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in SaveFirmAsync for FirmId: {FirmId}", firm?.FirmId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Deletes a firm record by its ID.
        /// Input parameters: int id
        /// Output/return value: Task
        /// </summary>
        public async Task DeleteFirmAsync(int id)
        {
            using var db = Db;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                await db.ExecuteAsync(
                    "sp_Tracker_DeleteFirm",
                    new { FirmId = id },
                    transaction: tx,
                    commandType: CommandType.StoredProcedure);

                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in DeleteFirmAsync for Id: {Id}", id);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Retrieves a simplified dropdown list of active firms.
        /// Input parameters: None
        /// Output/return value: Task of IEnumerable of dynamic
        /// </summary>
        public async Task<IEnumerable<dynamic>> GetFirmDropdownAsync()
        {
            try
            {
                using var db = Db;
                return await db.QueryAsync(
                    "sp_GetFirmDropdown",
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetFirmDropdownAsync.");
                throw;
            }
        }

        // --- BROKER METHODS ---

        /// <summary>
        /// Purpose: Retrieves a paginated and filtered list of brokers.
        /// Input parameters: string search, int? firmId, string sortCol, string sortOrder, int page, int pageSize
        /// Output/return value: Task of Tuple containing IEnumerable of BrokerItem and total count
        /// </summary>
        public async Task<(IEnumerable<BrokerItem>, int)> GetBrokersAsync(string search, int? firmId, string sortCol, string sortOrder, int page, int pageSize)
        {
            try
            {
                using (var db = Db)
                {
                    var p = new { SearchText = search, FirmId = firmId, SortColumn = sortCol, SortOrder = sortOrder, PageNumber = page, PageSize = pageSize };
                    var res = await db.QueryAsync<BrokerItem>("sp_Tracker_GetBrokerList", p, commandType: CommandType.StoredProcedure);
                    return (res, res.FirstOrDefault()?.TotalCount ?? 0);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetBrokersAsync.");
                throw;
            }
        }

        /// <summary>
        /// Purpose: Retrieves a specific broker by their ID.
        /// Input parameters: int id
        /// Output/return value: Task of BrokerItem
        /// </summary>
        public async Task<BrokerItem> GetBrokerByIdAsync(int id)
        {
            try
            {
                using var db = Db;
                return await db.QueryFirstOrDefaultAsync<BrokerItem>(
                    "sp_GetBrokerById",
                    new { Id = id },
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetBrokerByIdAsync for Id: {Id}", id);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Saves (inserts or updates) a broker record.
        /// Input parameters: BrokerItem broker
        /// Output/return value: Task
        /// </summary>
        public async Task<int> SaveBrokerAsync(BrokerItem broker)
        {
            using var db = Db;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                var p = new DynamicParameters();
                p.Add("@BrokerId", broker.BrokerId);
                p.Add("@FirmId", broker.FirmId);
                p.Add("@BrokerName", broker.BrokerName);
                p.Add("@Email", broker.Email);
                p.Add("@Phone", broker.Phone);
                p.Add("@Address", broker.Address);
                p.Add("@City", broker.City);
                p.Add("@StateId", broker.StateId); // Pass the integer ID
                p.Add("@Zip", broker.Zip);
                p.Add("@ConnectUser", broker.ConnectUser);
                p.Add("@IsActive", broker.IsActive);
                p.Add("@NewId", dbType: DbType.Int32, direction: ParameterDirection.Output);

                await db.ExecuteAsync(
                    "sp_Tracker_ManageBroker",
                    p,
                    transaction: tx,
                    commandType: CommandType.StoredProcedure);

                tx.Commit();
                return p.Get<int>("@NewId");
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in SaveBrokerAsync for BrokerId: {BrokerId}", broker?.BrokerId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Deletes a broker record by its ID.
        /// Input parameters: int id
        /// Output/return value: Task
        /// </summary>
        public async Task DeleteBrokerAsync(int id)
        {
            using var db = Db;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                await db.ExecuteAsync(
                    "sp_Tracker_DeleteBroker",
                    new { BrokerId = id },
                    transaction: tx,
                    commandType: CommandType.StoredProcedure);

                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in DeleteBrokerAsync for Id: {Id}", id);
                throw;
            }
        }
    }
}