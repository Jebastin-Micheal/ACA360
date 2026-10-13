using System.Data;
using ACA360.Core.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace ACA360.BusinessLogic.Services
{
    /// <summary>Rule what-if (script 067) and the duplicate SSN review (script 066).</summary>
    public interface IDataQualityService
    {
        /// <summary>Runs a rule, saved or not, over the most recent files still in staging. Nothing is stored.</summary>
        Task<RuleWhatIfResult> RuleWhatIfAsync(int userId, ValidationRule rule, int maxFiles = 20);
        Task<List<DuplicateSsnRow>> DuplicateSsnsAsync(int userId, int year);
    }

    public class DataQualityService : IDataQualityService
    {
        private readonly string _connectionString;

        public DataQualityService(string connectionString) => _connectionString = connectionString;

        public async Task<RuleWhatIfResult> RuleWhatIfAsync(int userId, ValidationRule rule, int maxFiles = 20)
        {
            using var db = new SqlConnection(_connectionString);
            using var grid = await db.QueryMultipleAsync("dbo.sp_Validation_RuleWhatIf", new
            {
                UserId = userId,
                ExistingRuleId = rule.RuleId > 0 ? rule.RuleId : (int?)null,
                rule.TargetTable,
                rule.TargetColumn,
                rule.ValidationType,
                rule.RuleParameter1,
                rule.RuleParameter2,
                MaxFiles = maxFiles,
            }, commandType: CommandType.StoredProcedure, commandTimeout: 300);
            return new RuleWhatIfResult
            {
                Problems = (await grid.ReadAsync<string>()).ToList(),
                Files = (await grid.ReadAsync<RuleWhatIfFile>()).ToList(),
                Samples = (await grid.ReadAsync<RuleWhatIfSample>()).ToList(),
            };
        }

        public async Task<List<DuplicateSsnRow>> DuplicateSsnsAsync(int userId, int year)
        {
            using var db = new SqlConnection(_connectionString);
            return (await db.QueryAsync<DuplicateSsnRow>("dbo.sp_DuplicateSsn_Report", new { UserId = userId, Year = year },
                commandType: CommandType.StoredProcedure, commandTimeout: 120)).ToList();
        }
    }
}
