using ACA360.Core.Models;
using Dapper;
using Microsoft.AspNetCore.Identity;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace ACA360.Security.Stores
{
    public class DapperRoleStore : IRoleStore<Role>
    {
        private readonly IDbConnection _connection;

        public DapperRoleStore(IDbConnection connection)
        {
            _connection = connection;
        }

        public async Task<IdentityResult> CreateAsync(Role role, CancellationToken cancellationToken)
        {
            var sql = @"
                INSERT INTO dbo.tbl_Roles (RoleName, Description, IsActive, CreatedDate)
                VALUES (@RoleName, @Description, @IsActive, @DateModified);
                SELECT CAST(SCOPE_IDENTITY() as int);";

            var id = await _connection.QuerySingleOrDefaultAsync<int>(sql, role);
            role.Id = id;
            return IdentityResult.Success;
        }

        public async Task<IdentityResult> DeleteAsync(Role role, CancellationToken cancellationToken)
        {
            var sql = "DELETE FROM dbo.tbl_Roles WHERE ID = @Id";
            await _connection.ExecuteAsync(sql, new { role.Id });
            return IdentityResult.Success;
        }

        public void Dispose()
        {
        }

        public async Task<Role> FindByIdAsync(string roleId, CancellationToken cancellationToken)
        {
            if (!int.TryParse(roleId, out int id)) return null;
            var sql = "SELECT * FROM dbo.tbl_Roles WHERE ID = @Id";
            return await _connection.QuerySingleOrDefaultAsync<Role>(sql, new { Id = id });
        }

        public async Task<Role> FindByNameAsync(string normalizedRoleName, CancellationToken cancellationToken)
        {
            var sql = "SELECT * FROM dbo.tbl_Roles WHERE UPPER(RoleName) = @Name";
            return await _connection.QuerySingleOrDefaultAsync<Role>(sql, new { Name = normalizedRoleName });
        }

        public Task<string> GetNormalizedRoleNameAsync(Role role, CancellationToken cancellationToken)
        {
            return Task.FromResult(role.RoleName?.ToUpper());
        }

        public Task<string> GetRoleIdAsync(Role role, CancellationToken cancellationToken)
        {
            return Task.FromResult(role.Id.ToString());
        }

        public Task<string> GetRoleNameAsync(Role role, CancellationToken cancellationToken)
        {
            return Task.FromResult(role.RoleName);
        }

        public Task SetNormalizedRoleNameAsync(Role role, string normalizedName, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task SetRoleNameAsync(Role role, string roleName, CancellationToken cancellationToken)
        {
            role.RoleName = roleName;
            return Task.CompletedTask;
        }

        public async Task<IdentityResult> UpdateAsync(Role role, CancellationToken cancellationToken)
        {
            var sql = @"
                UPDATE dbo.tbl_Roles SET
                    RoleName = @RoleName,
                    Description = @Description,
                    IsActive = @IsActive
                WHERE ID = @Id";
            await _connection.ExecuteAsync(sql, role);
            return IdentityResult.Success;
        }
    }
}
