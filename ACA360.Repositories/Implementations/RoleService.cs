using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using ACA360.Repositories.Interfaces;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace ACA360.Repositories.Implementations
{
    public class RoleService : IRoleService
    {
        private readonly string _connectionString;
        private readonly ILoggerService _logger;
        private readonly IWebHostEnvironment _environment;

        /// <summary>
        /// Purpose: Initializes a new instance of the RoleService class.
        /// Input parameters: string connectionString, ILoggerService logger, IWebHostEnvironment environment
        /// Output/return value: None
        /// </summary>
        public RoleService(string connectionString, ILoggerService logger, IWebHostEnvironment environment)
        {
            try
            {
                _connectionString = connectionString;
                _logger = logger;
                _environment = environment;
            }
            catch (Exception ex)
            {
                if (logger != null)
                {
                    logger.LogError(ex, "RoleService", "Constructor", "Error occurred during initialization", "System");
                }
                throw;
            }
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        /// <summary>
        /// Purpose: Retrieves a paginated and filtered list of roles.
        /// Input parameters: PaginationEntity paginationEntity
        /// Output/return value: Tuple containing a List of Roles and PaginationViewEntity metadata
        /// </summary>
        public async Task<(List<Role> Roles, PaginationViewEntity PageInfo)> GetRoleList(PaginationEntity paginationEntity)
        {
            var roleList = new List<Role>();
            PaginationViewEntity paginationMeta = null;

            try
            {
                using var db = Connection;

                var parameters = new
                {
                    paginationEntity.PageIndex,
                    paginationEntity.PageSize,
                    Search = paginationEntity.Search,
                    paginationEntity.SortColumn,
                    SortOrder = paginationEntity.SortOrder ?? "asc"
                };

                using var multi = await db.QueryMultipleAsync(
                    "sp_Role_List",
                    parameters,
                    commandType: CommandType.StoredProcedure);

                // Using dynamic here because SQL returns "CreatedDate" but the model uses "DateModified"
                var roleRecords = await multi.ReadAsync<dynamic>();
                foreach (var r in roleRecords)
                {
                    roleList.Add(new Role
                    {
                        Id = r.ID ?? 0,
                        RoleName = r.RoleName,
                        Description = r.Description,
                        IsActive = r.IsActive ?? false,
                        DateModified = r.CreatedDate
                    });
                }

                if (!multi.IsConsumed)
                {
                    int totalItems = await multi.ReadSingleOrDefaultAsync<int>();
                    int pageSize = paginationEntity.PageSize;
                    int currentPage = paginationEntity.PageIndex;

                    paginationMeta = new PaginationViewEntity
                    {
                        TotalItems = totalItems,
                        CurrentPage = currentPage,
                        PageSize = pageSize,
                        TotalPages = (int)Math.Ceiling((double)totalItems / pageSize),
                        StartPage = (currentPage - 1) * pageSize + 1,
                        EndPage = Math.Min((currentPage - 1) * pageSize + pageSize, totalItems),
                        SortColumn = paginationEntity.SortColumn,
                        SortOrder = paginationEntity.SortOrder,
                        RecordCount = totalItems,
                        PageNumber = currentPage,
                        TotalCount = totalItems
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetRoleList", "RoleService", "Failed to retrieve Role list", "DB");
                throw;
            }

            return (roleList, paginationMeta);
        }

        /// <summary>
        /// Purpose: Retrieves form data for a specific role, including associated actions.
        /// Input parameters: int? id
        /// Output/return value: Task of RoleFormDataDto
        /// </summary>
        public async Task<RoleFormDataDto> GetRoleFormData(int? id)
        {
            var model = new RoleFormDataDto
            {
                Role = new Role(),
                SelectedActionIds = new List<int>()
            };

            try
            {
                using var db = Connection;

                using var multi = await db.QueryMultipleAsync(
                    "sp_GetRoleFormData",
                    new { RoleId = id },
                    commandType: CommandType.StoredProcedure);

                var roleRecord = await multi.ReadFirstOrDefaultAsync<Role>();
                if (roleRecord != null)
                {
                    model.Role = roleRecord;
                }

                if (!multi.IsConsumed)
                {
                    // Directly map the second result set to a list of integers
                    var actionIds = await multi.ReadAsync<int>();
                    model.SelectedActionIds = actionIds.ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetRoleFormData", "RoleService", "Failed to retrieve Role form data", "DB");
                throw;
            }

            return model;
        }

        /// <summary>
        /// Purpose: Adds a new role or updates an existing role.
        /// Input parameters: Role Role
        /// Output/return value: Task
        /// </summary>
        public async Task AddOrUpdateRole(Role Role)
        {
            try
            {
                using var db = Connection;

                var parameters = new
                {
                    Role.RoleName,
                    Role.Description,
                    Role.Id,
                    Role.IsActive
                };

                await db.ExecuteAsync(
                    "sp_Action_Role",
                    parameters,
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AddOrUpdateRole", "RoleService", "Failed to insert/update Role", "DB");
                throw;
            }
        }

        /// <summary>
        /// Purpose: Deletes a specific role based on its ID.
        /// Input parameters: int id
        /// Output/return value: Task
        /// </summary>
        public async Task DeleteRole(int id)
        {
            try
            {
                using var db = Connection;
                await db.ExecuteAsync(
                    "sp_Role_Delete",
                    new { Id = id },
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteRole", "RoleService", "Failed to delete Role", "DB");
                throw;
            }
        }
    }
}