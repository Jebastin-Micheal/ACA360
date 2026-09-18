using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Data;

namespace ACA360.BusinessLogic.Services
{
    public class DataAnalystReportService : IDataAnalystReportService
    {
        private readonly string _connectionString;
        private readonly ILogger<DataAnalystReportService> _logger;
        private readonly IMemoryCache _cache;

        private const string DaListCacheKey = "DAReport_DataAnalystList";
        private const string YearsCacheKey = "DAReport_FilingYears";

        public DataAnalystReportService(IConfiguration config, ILogger<DataAnalystReportService> logger, IMemoryCache cache)
        {
            _connectionString = config.GetConnectionString("DefaultConnection");
            _logger = logger;
            _cache = cache;
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        public async Task<List<DataAnalystModel>> GetDataAnalystsAsync()
        {
            if (_cache.TryGetValue(DaListCacheKey, out List<DataAnalystModel> cachedList))
                return cachedList;

            try
            {
                using var db = Connection;
                const string sql = @"
                    SELECT sa.Staff_ID AS StaffId,
                           (sa.Staff_FName + ' ' + sa.Staff_LName) AS StaffName
                    FROM StaffAccounts sa
                    INNER JOIN tbl_User u ON u.Ref_ID = sa.Staff_ID
                    INNER JOIN tbl_Roles r ON r.ID = u.Role_ID
                    WHERE r.RoleName IN ('Data Analyst', 'DA Supervisor')
                    ORDER BY StaffName";

                var result = (await db.QueryAsync<DataAnalystModel>(sql)).ToList();

                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetSlidingExpiration(TimeSpan.FromMinutes(30))
                    .SetAbsoluteExpiration(TimeSpan.FromHours(2));

                _cache.Set(DaListCacheKey, result, cacheOptions);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while fetching Data Analysts list.");
                throw;
            }
        }

        public async Task<List<int>> GetFilingYearsAsync()
        {
            if (_cache.TryGetValue(YearsCacheKey, out List<int> cachedYears))
                return cachedYears;

            try
            {
                using var db = Connection;
                const string sql = "SELECT filingYear FROM FilingYear ORDER BY filingYear DESC";
                var result = (await db.QueryAsync<int>(sql)).ToList();

                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetSlidingExpiration(TimeSpan.FromMinutes(30))
                    .SetAbsoluteExpiration(TimeSpan.FromHours(2));

                _cache.Set(YearsCacheKey, result, cacheOptions);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while fetching Filing Years list.");
                throw;
            }
        }

        public async Task<List<AmReportModel>> GetDataAnalystReportAsync(int dataAnalystId, int? filingYear)
        {
            try
            {
                using var db = Connection;
                var result = await db.QueryAsync<AmReportModel>(
                    "sp_getDataAnalyst_report",
                    new { DataAnalystId = dataAnalystId, FilingYear = filingYear },
                    commandType: CommandType.StoredProcedure);
                return result.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while fetching Data Analyst Report for DataAnalystId: {DataAnalystId}", dataAnalystId);
                throw;
            }
        }
    }
}