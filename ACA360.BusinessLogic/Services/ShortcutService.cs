using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using Dapper;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Services
{
    public class ShortcutService : IShortcutService
    {
        private readonly IDbConnection Connection;
        private readonly ILoggerService _logger;

        public ShortcutService(IDbConnection connection, ILoggerService logger)
        {
            Connection = connection;
            _logger = logger;
        }

        public async Task<List<UserShortcutItem>> GetUserShortcuts(long userId)
        {
            try
            {
                using var db = Connection;
                var list = await db.QueryAsync<UserShortcutItem>(
                    "sp_GetUserShortcuts",
                    new { UserId = userId },
                    commandType: CommandType.StoredProcedure);
                return list.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetUserShortcuts", "Service", $"Failed to retrieve shortcuts for User ID: {userId}", "DB");
                throw;
            }
        }

        public async Task<List<UserShortcutItem>> GetAvailableShortcutMenus(long userId)
        {
            try
            {
                using var db = Connection;
                var list = await db.QueryAsync<UserShortcutItem>(
                    "sp_GetAvailableShortcutMenus",
                    new { UserId = userId },
                    commandType: CommandType.StoredProcedure);
                return list.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetAvailableShortcutMenus", "Service", $"Failed to retrieve available menus for User ID: {userId}", "DB");
                throw;
            }
        }

        public async Task<bool> AddUserShortcut(long userId, int menuId)
        {
            try
            {
                using var db = Connection;
                var success = await db.QueryFirstOrDefaultAsync<int>(
                    "sp_AddUserShortcut",
                    new { UserId = userId, MenuId = menuId },
                    commandType: CommandType.StoredProcedure);
                return success == 1;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AddUserShortcut", "Service", $"Failed to add shortcut MenuId {menuId} for User ID: {userId}", "DB");
                throw;
            }
        }

        public async Task RemoveUserShortcut(long userId, int menuId)
        {
            try
            {
                using var db = Connection;
                await db.ExecuteAsync(
                    "sp_RemoveUserShortcut",
                    new { UserId = userId, MenuId = menuId },
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RemoveUserShortcut", "Service", $"Failed to remove shortcut MenuId {menuId} for User ID: {userId}", "DB");
                throw;
            }
        }

        public async Task ReorderUserShortcuts(long userId, List<int> orderedMenuIds)
        {
            try
            {
                using var db = Connection;
                var csv = string.Join(",", orderedMenuIds);
                await db.ExecuteAsync(
                    "sp_ReorderUserShortcuts",
                    new { UserId = userId, MenuIdsCsv = csv },
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ReorderUserShortcuts", "Service", $"Failed to reorder shortcuts for User ID: {userId}", "DB");
                throw;
            }
        }
    }
}
