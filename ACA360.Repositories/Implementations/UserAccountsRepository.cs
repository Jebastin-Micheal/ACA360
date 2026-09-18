using ACA360.Core.Models;
using ACA360.Data.Helpers;
using ACA360.Logging.Interfaces;
using ACA360.Repositories.Interfaces;
using Dapper;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace ACA360.Repositories.Implementations
{
    public class UserAccountsRepository : IUserAccountsService
    {
        private readonly string _connectionString;
        private readonly ILoggerService _logger;

        public UserAccountsRepository(IConfiguration configuration, ILoggerService logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException(
                    "ConnectionStrings:DefaultConnection is missing from configuration."); ;
            _logger = logger;
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        /// <summary>
        /// Retrieves a paginated list of user accounts along with role statistics.
        /// </summary>
        /// <param name="paginationEntity">Pagination, sorting, and search parameters.</param>
        /// <param name="roleId">Role filter criteria.</param>
        /// <param name="statusId">Status filter criteria.</param>
        /// <returns>A tuple containing the account list, pagination metadata, and role summary statistics.</returns>
        public async Task<(List<UserAccounts> UserAccountss, PaginationViewEntity PageInfo, List<RoleSummary> RoleStats)> GetUserAccountsList(PaginationEntity paginationEntity, int roleId, int statusId)
        {
            try
            {
                using (var db = Connection)
                {
                    var parameters = new
                    {
                        paginationEntity.PageIndex,
                        paginationEntity.PageSize,
                        Search = paginationEntity.Search ?? (object)DBNull.Value,
                        paginationEntity.SortColumn,
                        SortOrder = paginationEntity.SortOrder ?? "asc",
                        RoleId = roleId,
                        StatusId = statusId
                    };

                    using (var multi = await db.QueryMultipleAsync("sp_UserAccountsList", parameters, commandType: CommandType.StoredProcedure))
                    {
                        var UserAccountsList = (await multi.ReadAsync<UserAccounts>()).ToList();
                        int totalItems = await multi.ReadSingleAsync<int>();

                        int pageSize = paginationEntity.PageSize;
                        int currentPage = paginationEntity.PageIndex;

                        var paginationMeta = new PaginationViewEntity
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

                        var roleSummary = (await multi.ReadAsync<RoleSummary>()).ToList();

                        return (UserAccountsList, paginationMeta, roleSummary);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetUserAccountsList), nameof(UserAccountsRepository), "An error occurred while retrieving the user accounts list.");
                throw;
            }
        }

        /// <summary>
        /// Retrieves a specific user account by its unique ID.
        /// </summary>
        /// <param name="id">The user account identifier.</param>
        /// <param name="roleId">The optional role identifier.</param>
        /// <returns>The UserAccounts object.</returns>
        public async Task<UserAccounts> GetUserAccountsByIdAsync(long id, int? roleId = null)
        {
            try
            {
                using (var db = Connection)
                {
                    return await db.QuerySingleOrDefaultAsync<UserAccounts>(
                        "sp_GetUserAccountsById",
                        new { UserAccounts_ID = id, UserAccounts_Role = roleId ?? 0 },
                        commandType: CommandType.StoredProcedure);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetUserAccountsByIdAsync), nameof(UserAccountsRepository), $"An error occurred while retrieving user account ID {id}.");
                throw;
            }
        }

        /// <summary>
        /// Adds a new user account to the system.
        /// </summary>
        /// <param name="UserAccounts">The user account entity to create.</param>
        /// <param name="profilePictureFileName">The filename of the uploaded profile picture.</param>
        public async Task AddUserAccountsAsync(UserAccounts UserAccounts, string profilePictureFileName = null)
        {
            try
            {
                using (var db = Connection)
                {
                    var parameters = new
                    {
                        UserAccounts.UserAccounts_FName,
                        UserAccounts.UserAccounts_LName,
                        UserAccounts.UserAccounts_Addr1,
                        UserAccounts.UserAccounts_Addr2,
                        UserAccounts.UserAccounts_City,
                        UserAccounts.UserAccounts_State,
                        UserAccounts.UserAccounts_Zip,
                        UserAccounts.UserAccounts_Phone,
                        UserAccounts.UserAccounts_Email,
                        UserAccounts.UserAccounts_Password,
                        UserAccounts.UserAccounts_Status,
                        UserAccounts.UserAccounts_Role,
                        ProfilePicture = profilePictureFileName,
                        UserAccounts.IsMFA,
                        UserAccounts.createdby
                    };

                    await db.ExecuteAsync("sp_CreateUserAccounts", parameters, commandType: CommandType.StoredProcedure);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(AddUserAccountsAsync), nameof(UserAccountsRepository), "An error occurred while creating a new user account.");
                throw;
            }
        }

        /// <summary>
        /// Updates an existing user account in the system.
        /// </summary>
        /// <param name="UserAccounts">The updated user account entity.</param>
        /// <param name="profilePictureFileName">The updated filename of the profile picture.</param>
        public async Task UpdateUserAccountsAsync(UserAccounts UserAccounts, string profilePictureFileName = null)
        {
            try
            {
                using (var db = Connection)
                {
                    var parameters = new
                    {
                        UserAccounts.UserAccounts_ID,
                        UserAccounts.UserAccounts_FName,
                        UserAccounts.UserAccounts_LName,
                        UserAccounts.UserAccounts_Addr1,
                        UserAccounts.UserAccounts_Addr2,
                        UserAccounts.UserAccounts_City,
                        UserAccounts.UserAccounts_State,
                        UserAccounts.UserAccounts_Zip,
                        UserAccounts.UserAccounts_Phone,
                        UserAccounts.UserAccounts_Email,
                        UserAccounts.UserAccounts_Password,
                        UserAccounts.UserAccounts_Status,
                        UserAccounts.UserAccounts_Role,
                        ProfilePicture = profilePictureFileName,
                        UserAccounts.IsMFA,
                        UserAccounts.modifiedby
                    };

                    await db.ExecuteAsync("sp_UpdateUserAccounts", parameters, commandType: CommandType.StoredProcedure);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(UpdateUserAccountsAsync), nameof(UserAccountsRepository), $"An error occurred while updating user account ID {UserAccounts?.UserAccounts_ID}.");
                throw;
            }
        }

        /// <summary>
        /// Deletes a specific user account from the system.
        /// </summary>
        /// <param name="id">The unique identifier of the user account.</param>
        /// <param name="roleId">The role identifier connected to the user.</param>
        public async Task DeleteUserAccountsAsync(long id, int roleId)
        {
            try
            {
                using (var db = Connection)
                {
                    await db.ExecuteAsync("sp_DeleteUserAccounts",
                        new { UserAccounts_ID = id, UserAccounts_Role = roleId },
                        commandType: CommandType.StoredProcedure);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(DeleteUserAccountsAsync), nameof(UserAccountsRepository), $"An error occurred while deleting user account ID {id}.");
                throw;
            }
        }

        /// <summary>
        /// Retrieves a list of all roles formatted as dropdown select list items.
        /// </summary>
        /// <returns>A list of SelectListItem containing active roles.</returns>
        public async Task<List<SelectListItem>> GetAll_Roles()
        {
            try
            {
                using (var db = Connection)
                {
                    var roles = await db.QueryAsync<RoleDto>(
                        "sp_drop_dwon_Role",
                        commandType: CommandType.StoredProcedure);

                    return roles.Select(c => new SelectListItem
                    {
                        Text = c.RoleName,
                        Value = c.Id.ToString()
                    }).ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetAll_Roles), nameof(UserAccountsRepository), "An error occurred while retrieving the roles list.");
                return new List<SelectListItem>(); // Return empty list on error to avoid breaking the UI
            }
        }



        public async Task<List<UserSelectionModel>> GetUsersInRoleAsync(string roleName)
        {
            var users = await Connection.QueryAsync<UserSelectionModel>("sp_GetUsersByRole",
                new { RoleName = roleName },
                commandType: CommandType.StoredProcedure);

            return users.AsList();
        }

        public async Task<IEnumerable<StateModel>> GetAllStatesAsync()
        {
            try
            {
                using (var db = Connection)
                {
                    var result = await db.QueryAsync<StateModel>(
                        "sp_GetAllStates",
                        commandType: CommandType.StoredProcedure);

                    return result ?? new List<StateModel>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    nameof(GetAllStatesAsync),
                    nameof(UserAccountsRepository),
                    "Error loading states.");

                throw;
            }
        }
    }
}