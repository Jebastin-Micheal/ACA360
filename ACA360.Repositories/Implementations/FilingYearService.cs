using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using ACA360.Repositories.Interfaces;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace ACA360.Repositories.Implementations
{
    public class FilingYearService : IFilingYearService
    {
        private readonly string _connectionString;
        private readonly ILoggerService _logger;

        public FilingYearService(string connectionString, ILoggerService logger)
        {
            _connectionString = connectionString;
            _logger = logger;
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        public async Task<List<FilingYearModel>> GetAllFilingYears()
        {
            try
            {
                using var db = Connection;

                var filingYears = await db.QueryAsync<FilingYearModel>(
                    "sp_GetFillingYearList",
                    commandType: CommandType.StoredProcedure);

                return filingYears.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "GetAllFilingYears",
                    "Service",
                    "Failed to fetch FilingYears",
                    "Server or DB"
                );
                throw;
            }
        }
               
    }
}