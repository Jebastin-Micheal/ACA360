using ACA360.Core.Models;
using ACA360.Repositories.Interfaces;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace ACA360.Repositories.Implementations
{
    public class UserRepository : IUserRepository
    {
        private readonly string _connectionString;
        public UserRepository(string connectionString) { _connectionString = connectionString; }
        private IDbConnection Connection => new SqlConnection(_connectionString);

        public async Task<List<UserSelectionModel>> GetUsersInRoleAsync(string roleName)
        {
            var users = await Connection.QueryAsync<UserSelectionModel>("sp_GetUsersByRole",
                new { RoleName = roleName },
                commandType: CommandType.StoredProcedure);

            return users.AsList();
        }
        // ACA360.Services/Repository/UserRepository.cs
        public async Task<(List<UserModel>, PaginationViewEntity)> GetUserListAsync(PaginationEntity pagination)
        {
            using var db = Connection;
            var p = new DynamicParameters();
            p.Add("@PageIndex", pagination.PageIndex);
            p.Add("@PageSize", pagination.PageSize);
            // ... add other params

            using var multi = await db.QueryMultipleAsync("usp_User_List", p, commandType: CommandType.StoredProcedure);

            var users = (await multi.ReadAsync<UserModel>()).ToList();
            var meta = await multi.ReadFirstOrDefaultAsync<PaginationViewEntity>(); // Ensure your SP returns matching columns

            return (users, meta);
        }
    }
}