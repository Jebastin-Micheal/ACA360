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
        /// <summary>A list page's saved search, sort, page size, columns and filters (JSON), or null.</summary>
        Task<string?> GetGridStateAsync(string userId, string gridKey);
        /// <summary>Saves it; null removes it.</summary>
        Task SetGridStateAsync(string userId, string gridKey, string? stateJson);
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

        // Grid states live beside the view modes: one "grid_states" preference per user, a JSON object
        // of grid key -> that grid's state.
        private async Task<Dictionary<string, JsonElement>> GridStatesAsync(string userId)
        {
            string? json = await _db.QueryFirstOrDefaultAsync<string?>("usp_GetUserPreference",
                new { UserId = userId, PreferenceType = "grid_states" }, commandType: CommandType.StoredProcedure);
            if (string.IsNullOrEmpty(json)) return new Dictionary<string, JsonElement>();
            try { return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? new Dictionary<string, JsonElement>(); }
            catch (JsonException) { return new Dictionary<string, JsonElement>(); }
        }

        public async Task<string?> GetGridStateAsync(string userId, string gridKey)
        {
            var states = await GridStatesAsync(userId);
            return states.TryGetValue(gridKey, out var state) ? state.GetRawText() : null;
        }

        public async Task SetGridStateAsync(string userId, string gridKey, string? stateJson)
        {
            var states = await GridStatesAsync(userId);
            if (stateJson == null) states.Remove(gridKey);
            else states[gridKey] = JsonDocument.Parse(stateJson).RootElement.Clone();
            await _db.ExecuteAsync("usp_SaveUserPreference",
                new { UserId = userId, PreferenceType = "grid_states", PreferenceValue = JsonSerializer.Serialize(states) },
                commandType: CommandType.StoredProcedure);
        }
    }
}