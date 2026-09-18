using Dapper;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;

namespace ACA360.Repositories.Implementations
{
    public interface IUserPreferenceService
    {
        Task<string?> GetViewModeAsync(string userId, string moduleKey);
        Task SetViewModeAsync(string userId, string moduleKey, string viewMode);
    }

    public class UserPreferenceService : IUserPreferenceService
    {
        private readonly IDbConnection _db;

        public UserPreferenceService(IDbConnection db)
        {
            _db = db;
        }

        public async Task<string?> GetViewModeAsync(string userId, string moduleKey)
        {
            // Call the generic Get SP
            string? json = await _db.QueryFirstOrDefaultAsync<string?>(
                "usp_GetUserPreference",
                new { UserId = userId, PreferenceType = "view_modes" },
                commandType: CommandType.StoredProcedure
            );

            if (string.IsNullOrEmpty(json))
                return null;

            // Convert the JSON string back into a Dictionary
            var viewModes = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

            // Return the specific module's view mode if it exists in the JSON, otherwise return null
            return viewModes != null && viewModes.ContainsKey(moduleKey) ? viewModes[moduleKey] : null;
        }

        public async Task SetViewModeAsync(string userId, string moduleKey, string viewMode)
        {
            // 1. Fetch the existing JSON string using the Get SP
            string? json = await _db.QueryFirstOrDefaultAsync<string?>(
                "usp_GetUserPreference",
                new { UserId = userId, PreferenceType = "view_modes" },
                commandType: CommandType.StoredProcedure
            );

            // 2. Deserialize it, or create a new empty dictionary if it doesn't exist
            var viewModes = string.IsNullOrEmpty(json)
                ? new Dictionary<string, string>()
                : JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();

            // 3. Update or Add the specific module's view mode
            viewModes[moduleKey] = viewMode;

            // 4. Serialize the dictionary back into a JSON string
            string updatedJson = JsonSerializer.Serialize(viewModes);

            // 5. Save the updated JSON back to SQL using the Save SP
            await _db.ExecuteAsync(
                "usp_SaveUserPreference",
                new { UserId = userId, PreferenceType = "view_modes", PreferenceValue = updatedJson },
                commandType: CommandType.StoredProcedure
            );
        }
    }
}