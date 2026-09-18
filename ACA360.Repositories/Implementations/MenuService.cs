using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using ACA360.Repositories.Interfaces;
using Dapper;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace ACA360.Repositories.Implementations
{
    public class MenuService : IMenuService
    {
        private readonly string _connectionString;
        private readonly ILoggerService _logger;

        /// <summary>
        /// Purpose: Initializes a new instance of the MenuService class.
        /// Input parameters: string connectionString, ILoggerService logger
        /// Output/return value: None
        /// </summary>
        public MenuService(string connectionString, ILoggerService logger)
        {
            try
            {
                _connectionString = connectionString;
                _logger = logger;
            }
            catch (Exception ex)
            {
                if (logger != null)
                {
                    logger.LogError(ex, "MenuService", "Constructor", "Error occurred during initialization", "System");
                }
                throw;
            }
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        /// <summary>
        /// Purpose: Retrieves a paginated list of menus.
        /// Input parameters: PaginationEntity paginationEntity
        /// Output/return value: Tuple containing a List of Menus and PaginationViewEntity metadata
        /// </summary>
        public async Task<(List<Menu> Menus, PaginationViewEntity PageInfo)> GetMenuList(PaginationEntity paginationEntity)
        {
            var menuList = new List<Menu>();
            PaginationViewEntity paginationMeta = new PaginationViewEntity();

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
                    "sp_MenuList",
                    parameters,
                    commandType: CommandType.StoredProcedure);

                // Dapper handles the explicit mapping automatically based on column names
                menuList = (await multi.ReadAsync<Menu>()).ToList();

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
                _logger.LogError(ex, "GetMenuList", "Service", "Failed to retrieve menu list", "DB");
                throw;
            }

            return (menuList, paginationMeta);
        }

        /// <summary>
        /// Purpose: Retrieves menu form data by menu ID, including selected actions.
        /// Input parameters: int? id
        /// Output/return value: MenuFormDataDto containing Menu details and associated action IDs
        /// </summary>
        public async Task<MenuFormDataDto> GetMenuFormData(int? id)
        {
            var model = new MenuFormDataDto
            {
                Menu = new Menu(),
                SelectedActionIds = new List<int>()
            };

            try
            {
                using var db = Connection;

                using var multi = await db.QueryMultipleAsync(
                    "sp_GetMenuFormData",
                    new { MenuId = id },
                    commandType: CommandType.StoredProcedure);

                var menuRecord = await multi.ReadFirstOrDefaultAsync<Menu>();
                if (menuRecord != null)
                {
                    model.Menu = menuRecord;
                }

                if (!multi.IsConsumed)
                {
                    // Assuming the second result set returns a column that maps directly to integers
                    var actionIds = await multi.ReadAsync<int>();
                    model.SelectedActionIds = actionIds.ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetMenuFormData", "Service", "Failed to retrieve menu form data", "DB");
                throw;
            }

            return model;
        }

        /// <summary>
        /// Purpose: Adds a new menu or updates an existing one, along with role and action associations.
        /// Input parameters: Menu menu, int roleId, List<int> actionIds
        /// Output/return value: Task
        /// </summary>
        public async Task AddOrUpdateMenu(Menu menu, int roleId, List<int> actionIds)
        {
            try
            {
                DataTable actionIdTable = new DataTable();
                actionIdTable.Columns.Add("ActionId", typeof(int));
                foreach (var id in actionIds)
                    actionIdTable.Rows.Add(id);

                using var db = Connection;

                var parameters = new
                {
                    MenuId = menu.Id,
                    menu.MenuName,
                    menu.Controller,
                    menu.Icon,
                    menu.DisplayOrder,
                    menu.IsActive,
                    menu.ParentMenuId,
                    RoleId = roleId,
                    menu.IsVisible,
                    // Dapper securely handles User-Defined Table Types natively!
                    ActionIds = actionIdTable.AsTableValuedParameter("dbo.ActionIdTableType")
                };

                await db.ExecuteAsync(
                    "sp_Add_EditMenuAndActionsForRole",
                    parameters,
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AddOrUpdateMenu", "Service", "Failed to insert/update menu", "DB");
                throw;
            }
        }

        /// <summary>
        /// Purpose: Performs a soft delete on a menu by its ID.
        /// Input parameters: int id
        /// Output/return value: Task
        /// </summary>
        public async Task SoftDeleteMenu(int id)
        {
            try
            {
                using var db = Connection;
                await db.ExecuteAsync(
                    "sp_Menu_SoftDelete",
                    new { Id = id },
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SoftDeleteMenu", "Service", $"Failed to soft delete menu with ID: {id}", "DB");
                throw;
            }
        }

        /// <summary>
        /// Purpose: Retrieves a list of menus accessible by a specific user.
        /// Input parameters: string userId
        /// Output/return value: List of Menus
        /// </summary>
        public async Task<List<Menu>> GetMenuByUserId(string userId)
        {
            try
            {
                using var db = Connection;
                var menuList = await db.QueryAsync<Menu>(
                    "sp_GetUserMenu",
                    new { UserId = userId },
                    commandType: CommandType.StoredProcedure);

                return menuList.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetMenuByUserId", "Service", $"Failed to retrieve menu list for User ID: {userId}", "DB");
                throw;
            }
        }

        #region Drop_Down

        /// <summary>
        /// Purpose: Retrieves all parent menus for populating a dropdown list.
        /// Input parameters: None
        /// Output/return value: List of SelectListItem representing parent menus
        /// </summary>
        public async Task<List<ParentMenuDto>> GetAll_Parent_Menu()
        {
            using var db = Connection;
            var parentMenus = await db.QueryAsync<ParentMenuDto>(
                "usp_select_Parent_Menu",
                commandType: CommandType.StoredProcedure);

            return parentMenus.ToList();
        }

        /// <summary>
        /// Purpose: Retrieves all menu actions for populating a dropdown list.
        /// Input parameters: None
        /// Output/return value: List of SelectListItem representing menu actions
        /// </summary>
        public async Task<List<SelectListItem>> GetAll_Menu_Action()
        {
            try
            {
                using var db = Connection;

                var actions = await db.QueryAsync<dynamic>(
                    "usp_select_Plan_Menu_Action",
                    commandType: CommandType.StoredProcedure);

                return actions.Select(c => new SelectListItem
                {
                    Text = c.Name?.ToString(),
                    Value = c.ID?.ToString()
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetAll_Menu_Action", "Service", "Failed to retrieve menu actions for dropdown", "DB");
                throw;
            }
        }

        #endregion
    }
}