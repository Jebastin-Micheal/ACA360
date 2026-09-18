using ACA360.Core.Models;
using ACA360.Repositories.Interfaces;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Repositories.Implementations
{
    public class EmployerAssignmentService : IEmployerAssignmentService
    {
        private readonly string _connectionString;
        public EmployerAssignmentService(string connectionString) { _connectionString = connectionString; }
        private IDbConnection Connection => new SqlConnection(_connectionString);

        public async Task AssignManagerToEmployerAsync(string employerEIN, long accountManagerUserId)
        {
            await Connection.ExecuteAsync("sp_AssignManagerToEmployer",
                new { EmployerEIN = employerEIN, AccountManagerUserId = accountManagerUserId },
                commandType: CommandType.StoredProcedure);
        }

        public async Task<EmployerAccountManager> GetCurrentAssignmentForEmployerAsync(string employerEIN)
        {
            return await Connection.QuerySingleOrDefaultAsync<EmployerAccountManager>("sp_GetCurrentAssignmentForEmployer",
                new { EmployerEIN = employerEIN },
                commandType: CommandType.StoredProcedure);
        }
    }
}
