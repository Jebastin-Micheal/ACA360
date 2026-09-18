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
    public class PermissionService : IPermissionService
    {
        private readonly string _connectionString;
        private readonly ILoggerService _logger;

        public PermissionService(string connectionString, ILoggerService logger)
        {
            _connectionString = connectionString;
            _logger = logger;
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        public async Task<RolePermissionViewModels> GetPermissionsByRoleAsync(int roleId)
        {
            var result = new RolePermissionViewModels
            {
                RoleId = roleId,
                Permissions = new List<PermissionViewModel>()
            };

            try
            {
                using var db = Connection;

                // 1. Get role name
                var roleRecord = await db.QueryFirstOrDefaultAsync<dynamic>(
                    "sp_select_roles",
                    new { RoleId = roleId },
                    commandType: CommandType.StoredProcedure);

                if (roleRecord != null)
                {
                    result.RoleName = roleRecord.RoleName?.ToString();
                }

                // 2. Get permissions
                var permissions = await db.QueryAsync<PermissionViewModel>(
                    "sp_GetPermissionsByRole",
                    new { RoleId = roleId },
                    commandType: CommandType.StoredProcedure);

                if (permissions != null)
                {
                    result.Permissions = permissions.ToList();
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetPermissionsByRoleAsync", "PermissionService",
                    $"Failed to fetch permissions for role {roleId}", "Database");
                throw;
            }
        }

        public async Task UpdatePermissionsAsync(int roleId, List<PermissionViewModel> permissions, string modifiedBy)
        {
            if (permissions == null || !permissions.Any())
                return;

            // Create DataTable
            DataTable permissionTable = new DataTable();
            permissionTable.Columns.Add("MenuId", typeof(int));
            permissionTable.Columns.Add("ActionId", typeof(int));
            permissionTable.Columns.Add("IsEnabled", typeof(bool));

            foreach (var permission in permissions.Where(p => p != null))
            {
                permissionTable.Rows.Add(
                    permission.MenuId,
                    permission.ActionId,
                    permission.IsEnabled
                );
            }

            try
            {
                using var db = Connection;

                var parameters = new DynamicParameters();
                parameters.Add("@RoleId", roleId);
                parameters.Add("@ModifiedBy", modifiedBy);

                // .AsTableValuedParameter() safely passes the DataTable to SQL
                parameters.Add("@Permissions", permissionTable.AsTableValuedParameter());

                // Define the OUTPUT parameter
                parameters.Add("@ErrorMessage", dbType: DbType.String, direction: ParameterDirection.Output, size: -1);

                await db.ExecuteAsync(
                    "sp_UpdateRolePermissions_Bulk",
                    parameters,
                    commandType: CommandType.StoredProcedure);

                // Note: If you ever need to read the output error message, you can retrieve it like this:
                // string errorMessage = parameters.Get<string>("@ErrorMessage");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdatePermissionsAsync", "PermissionService", $"Failed to update permissions for role {roleId}", "Database");
                throw;
            }
        }
    }
}