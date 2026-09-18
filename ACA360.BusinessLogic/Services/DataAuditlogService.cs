using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Data;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ACA360.BusinessLogic.Services
{
    public class DataAuditlogService : IDataAuditlogService
    {
        private readonly string _connectionString;
        private readonly ILoggerService _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public DataAuditlogService(IConfiguration config, ILoggerService logger, IHttpContextAccessor httpContextAccessor)
        {
            _connectionString = config.GetConnectionString("DefaultConnection") ?? "";
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<bool> LogChangeAsync(DataAuditLog log)
        {
            try
            {
                using IDbConnection db = new SqlConnection(_connectionString);
                var p = new DynamicParameters();
                p.Add("@Table_Name", log.Table_Name);
                p.Add("@Record_Id", log.Record_Id);
                p.Add("@Action_Type", log.Action_Type);
                p.Add("@Old_Values", log.Old_Values);
                p.Add("@New_Values", log.New_Values);
                p.Add("@User_Id", log.User_Id);
                p.Add("@User_Name", log.User_Name);
                p.Add("@User_Role", log.User_Role);
                p.Add("@IP_Address", log.IP_Address);

                var result = await db.ExecuteAsync("sp_InsertDataAuditlog", p, commandType: CommandType.StoredProcedure);
                return result > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(LogChangeAsync), nameof(DataAuditlogService), $"Failed to log audit for table {log.Table_Name} record {log.Record_Id}");
                return false;
            }
        }

        public async Task<IEnumerable<DataAuditLog>> GetLogsAsync(AuditLogFilter filter)
        {
            try
            {
                using IDbConnection db = new SqlConnection(_connectionString);
                var p = new DynamicParameters();
                p.Add("@StartDate", filter.StartDate);
                p.Add("@EndDate", filter.EndDate);
                p.Add("@User_Id", filter.User_Id);
                p.Add("@Record_Id", filter.Record_Id);
                p.Add("@Action_Type", filter.Action_Type);
                p.Add("@Table_Name", filter.Table_Name);

                return await db.QueryAsync<DataAuditLog>("sp_SelectDataAuditlog", p, commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetLogsAsync), nameof(DataAuditlogService), "Failed to fetch audit logs from DB");
                return new List<DataAuditLog>();
            }
        }
       
        public async Task<bool> CaptureAndLogChangeAsync(string tableName, string recordId, string actionType, object? oldRecord, object? newRecord)
        {
            try
            {
                var context = _httpContextAccessor.HttpContext;
                var userId = context?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "System";
                var userName = context?.User?.Identity?.Name ?? "Unknown";
                var userRole = context?.User?.FindFirst(ClaimTypes.Role)?.Value ?? "None";
                var ipAddress = context?.Connection.RemoteIpAddress?.ToString();

                var jsonOptions = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

                string? safeOldValues = null;
                string? safeNewValues = null;

                // ==========================================
                // THE FIX: BULLETPROOF ARRAY DETECTION
                // ==========================================
                // Check if the incoming data is a List/Array (and not just a normal string)
                bool isBulkArray = (oldRecord is System.Collections.IEnumerable && !(oldRecord is string)) ||
                                   (newRecord is System.Collections.IEnumerable && !(newRecord is string));

                if (actionType == "BULK_UPSERT" || isBulkArray)
                {
                    // It is an Array (like our TVP upload). 
                    // Skip the single-object Delta algorithm and serialize it directly!
                    safeOldValues = oldRecord != null ? JsonSerializer.Serialize(oldRecord, jsonOptions) : null;
                    safeNewValues = newRecord != null ? JsonSerializer.Serialize(newRecord, jsonOptions) : null;
                }
                else
                {
                    // It is a single object (like an Employee Edit). 
                    // Run the normal field-by-field Delta algorithm.
                    var deltas = GetJsonDeltas(oldRecord, newRecord, jsonOptions);
                    safeOldValues = deltas.OldDiff;
                    safeNewValues = deltas.NewDiff;
                }

                // Optional: Skip logging if an update occurred but nothing actually changed
                if (actionType == "UPDATE" && string.IsNullOrEmpty(safeOldValues) && string.IsNullOrEmpty(safeNewValues))
                {

                    return true;
                }

                var auditLog = new DataAuditLog
                {
                    Table_Name = tableName,
                    Record_Id = recordId,
                    Action_Type = actionType,
                    Old_Values = safeOldValues,
                    New_Values = safeNewValues,
                    User_Id = userId,
                    User_Name = userName,
                    User_Role = userRole,
                    IP_Address = ipAddress
                };

                return await LogChangeAsync(auditLog);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(CaptureAndLogChangeAsync), nameof(DataAuditlogService), $"Failed to log audit for {tableName}");
                return false;
            }
        }


        private (string? OldDiff, string? NewDiff) GetJsonDeltas(object? oldRecord, object? newRecord, JsonSerializerOptions options)
        {
            if (oldRecord == null && newRecord == null) return (null, null);
            if (oldRecord == null) return (null, JsonSerializer.Serialize(newRecord, options));
            if (newRecord == null) return (JsonSerializer.Serialize(oldRecord, options), null);

            // Convert both objects into manageable JsonNodes
            var oldNode = JsonNode.Parse(JsonSerializer.Serialize(oldRecord, options)) as JsonObject;
            var newNode = JsonNode.Parse(JsonSerializer.Serialize(newRecord, options)) as JsonObject;

            var oldDiff = new JsonObject();
            var newDiff = new JsonObject();

            CompareJsonObjects(oldNode, newNode, oldDiff, newDiff);

            if (!oldDiff.Any() && !newDiff.Any()) return (null, null);

            return (oldDiff.ToJsonString(), newDiff.ToJsonString());
        }

        // 1. Smart Object Comparer
        private void CompareJsonObjects(JsonObject? oldObj, JsonObject? newObj, JsonObject oldDiff, JsonObject newDiff)
        {
            if (oldObj == null || newObj == null) return;

            foreach (var prop in newObj.ToDictionary())
            {
                var propName = prop.Key;
                var newVal = prop.Value;

                if (oldObj.TryGetPropertyValue(propName, out var oldVal))
                {
                    // If values are perfectly identical, skip
                    if (AreNodesEqual(oldVal, newVal))
                        continue;

                    // If the property is a nested Array (Like "Benefits"), process it row-by-row
                    if (oldVal is JsonArray oldArray && newVal is JsonArray newArray)
                    {
                        var (arrOld, arrNew) = CompareJsonArrays(oldArray, newArray);
                        if (arrOld != null && arrNew != null)
                        {
                            oldDiff[propName] = arrOld;
                            newDiff[propName] = arrNew;
                        }
                    }
                    else // Standard property that changed
                    {
                        oldDiff[propName] = oldVal != null ? JsonNode.Parse(oldVal.ToJsonString()) : null;
                        newDiff[propName] = newVal != null ? JsonNode.Parse(newVal.ToJsonString()) : null;
                    }
                }
                else // Brand new property added
                {
                    newDiff[propName] = newVal != null ? JsonNode.Parse(newVal.ToJsonString()) : null;
                }
            }
        }

        // 2. Smart Array Comparer (handles the row-by-row logic)
        private (JsonArray? OldArr, JsonArray? NewArr) CompareJsonArrays(JsonArray oldArray, JsonArray newArray)
        {
            var finalOld = new JsonArray();
            var finalNew = new JsonArray();
            int maxCount = Math.Max(oldArray.Count, newArray.Count);

            for (int i = 0; i < maxCount; i++)
            {
                var oldItem = i < oldArray.Count ? oldArray[i] : null;
                var newItem = i < newArray.Count ? newArray[i] : null;

                if (oldItem is JsonObject oldObjItem && newItem is JsonObject newObjItem)
                {
                    var diffOld = new JsonObject();
                    var diffNew = new JsonObject();

                    CompareJsonObjects(oldObjItem, newObjItem, diffOld, diffNew);

                    if (diffOld.Any() || diffNew.Any())
                    {
                        diffOld["_RowIndex"] = i;
                        diffNew["_RowIndex"] = i;
                        finalOld.Add(diffOld);
                        finalNew.Add(diffNew);
                    }
                }
                else
                {
                    // If they are plain strings/numbers in an array
                    if (!AreNodesEqual(oldItem, newItem))
                    {
                        finalOld.Add(oldItem != null ? JsonNode.Parse(oldItem.ToJsonString()) : null);
                        finalNew.Add(newItem != null ? JsonNode.Parse(newItem.ToJsonString()) : null);
                    }
                }
            }

            if (finalOld.Count == 0 && finalNew.Count == 0) return (null, null);
            return (finalOld, finalNew);
        }

        // 3. Smart Equality Checker (Ignores trailing zeros: 108.50 == 108.5)
        private bool AreNodesEqual(JsonNode? node1, JsonNode? node2)
        {
            if (node1 == null && node2 == null) return true;
            if (node1 == null || node2 == null) return false;

            if (node1 is JsonValue val1 && node2 is JsonValue val2)
            {
                // Mathematically compare numbers to ignore JSON formatting differences
                if (val1.TryGetValue<decimal>(out var dec1) && val2.TryGetValue<decimal>(out var dec2))
                {
                    return dec1 == dec2;
                }
                // Safely compare DateTimes to ignore formatting differences
                if (val1.TryGetValue<DateTime>(out var dt1) && val2.TryGetValue<DateTime>(out var dt2))
                {
                    return dt1 == dt2;
                }
            }

            // Fallback to string comparison for texts, bools, etc.
            return node1.ToJsonString() == node2.ToJsonString();
        }



        public async Task<IEnumerable<AuditLogModel>> GetAuditLogsAsync(string categoryType, string recordId = null)
        {
            using (IDbConnection db = new SqlConnection(_connectionString))
            {
                var parameters = new DynamicParameters();
                parameters.Add("@CategoryType", categoryType);
                parameters.Add("@RecordId", recordId);

                return await db.QueryAsync<AuditLogModel>(
                    "sp_GetCommonAuditLogs",
                    parameters,
                    commandType: CommandType.StoredProcedure);
            }
        }
    }
}