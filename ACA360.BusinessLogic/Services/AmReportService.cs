using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Services

{
    public class AmReportService : IAmReportService
    {
        private readonly string _connectionString;
        private readonly ILogger<AmReportService> _logger;

        public AmReportService(IConfiguration config, ILogger<AmReportService> logger)
        {
            _connectionString = config.GetConnectionString("DefaultConnection");
            _logger = logger;
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        public async Task<List<AccountManagerModel>> GetAccountManagersAsync()
        {
            try
            {
                using var db = Connection;

                const string sql = @"
            SELECT sa.Staff_ID AS StaffId,
                   (sa.Staff_FName + ' ' + sa.Staff_LName) AS StaffName
            FROM StaffAccounts sa
            INNER JOIN tbl_User u ON u.Ref_ID = sa.Staff_ID
            INNER JOIN tbl_Roles r ON r.ID = u.Role_ID
            WHERE r.RoleName IN ('Account Manager', 'Account Manager Supervisor')
            ORDER BY StaffName";

                var result = await db.QueryAsync<AccountManagerModel>(sql);
                return result.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while fetching Account Managers list.");
                throw;
            }
        }

        public async Task<List<int>> GetFilingYearsAsync()
        {
            using var db = Connection;
            const string sql = "SELECT filingYear FROM FilingYear ORDER BY filingYear DESC";
            var result = await db.QueryAsync<int>(sql);
            return result.ToList();
        }

        public async Task<List<AmReportModel>> GetAmReportAsync(int acctManagerId, int? filingYear)
        {
            try
            {
                using var db = Connection;

                var result = await db.QueryAsync<AmReportModel>(
                    "sp_getAccMan_report",
                    new { AcctManagerId = acctManagerId, FilingYear = filingYear },
                    commandType: CommandType.StoredProcedure);

                return result.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while fetching AM Report for AcctManagerId: {AcctManagerId}", acctManagerId);
                throw;
            }
        }
    }
}