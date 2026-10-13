using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using ACA360.BusinessLogic.Interfaces;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Services
{
    public class UISettingsService : IUISettingsService
    {
        private readonly string _connectionString;
        private readonly ILoggerService _logger;

        public UISettingsService(string connectionString, ILoggerService logger)
        {
            _connectionString = connectionString;
            _logger = logger;
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        public async Task<UISettingsModel?> GetSettings()
        {
            try
            {
                using var db = Connection;

                // Dapper automatically maps the columns to the UISettingsModel properties
                var settings = await db.QueryFirstOrDefaultAsync<UISettingsModel>(
                    "sp_GetUISettings",
                    commandType: CommandType.StoredProcedure);

                return settings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetSettings", "Service", "Failed to fetch UI settings", "DB");
                throw;
            }
        }

        public async Task SaveSettings(UISettingsModel settings, string user)
        {
            try
            {
                using var db = Connection;

                // Creating an anonymous object for parameters. 
                // Dapper automatically handles null values converting to DBNull behind the scenes.
                var parameters = new
                {
                    settings.Theme,
                    settings.Skin,
                    settings.HeaderType,
                    settings.NavigationLike,
                    settings.PrimaryColor,
                    settings.SemiDark,
                    settings.IsCollapsed,
                    settings.ContentLayout,
                    settings.SidenavHeader,
                    settings.Rtl,
                    ModifiedBy = user ?? "system"
                };

                await db.ExecuteAsync(
                    "sp_SaveUISettings",
                    parameters,
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SaveSettings", "Service", "Failed to save UI settings", "DB");
                throw;
            }
        }
    }
}