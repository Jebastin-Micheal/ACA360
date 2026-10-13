using ACA360.Core.Models;
using Dapper;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ACA360.Repositories.Implementations
{
    public partial class DataProcessingRepository
    {
        public async Task<ImportAnalysisPage> GetImportAnalysisRowsAsync(
            ImportAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            var validationError = request.Validate();
            if (validationError != null) throw new System.ArgumentException(validationError, nameof(request));
            using var db = Connection;
            using var results = await db.QueryMultipleAsync(new CommandDefinition(
                "dbo.sp_ImportAnalysis_GetRows", new
                {
                    request.FileLogId, request.Entity, request.ImportMode, request.Start, request.Length,
                    request.Search, request.SortColumn, request.SortDirection,
                    request.ImpactFilter, request.ValidationFilter
                }, commandType: CommandType.StoredProcedure, commandTimeout: 60,
                cancellationToken: cancellationToken));
            var page = await results.ReadSingleAsync<ImportAnalysisPage>();
            page.Rows = (await results.ReadAsync())
                .Select(row => (IDictionary<string, object?>)row).ToList();
            return page;
        }

        public async Task<ImportAnalysisSummary> GetImportAnalysisSummaryAsync(
            int fileLogId, string importMode, CancellationToken cancellationToken = default)
        {
            using var db = Connection;
            using var results = await db.QueryMultipleAsync(new CommandDefinition(
                "dbo.sp_ImportAnalysis_GetSummary", new { FileLogId = fileLogId, ImportMode = importMode },
                commandType: CommandType.StoredProcedure, commandTimeout: 60,
                cancellationToken: cancellationToken));
            return new ImportAnalysisSummary
            {
                Entities = (await results.ReadAsync<ImportAnalysisEntitySummary>()).ToList(),
                RelatedRemovals = (await results.ReadAsync<ImportAnalysisRemovalSummary>()).ToList()
            };
        }

        /// <summary>What an import of this file would change (script 057). A null page size returns every change.</summary>
        public async Task<ImportChanges> GetImportChangesAsync(
            int fileLogId, string? kind, string? search, int pageIndex, int? pageSize, CancellationToken cancellationToken = default)
        {
            using var db = Connection;
            using var results = await db.QueryMultipleAsync(new CommandDefinition(
                "dbo.sp_ImportAnalysis_Changes",
                new { FileLogId = fileLogId, Kind = kind, Search = search, PageIndex = pageIndex, PageSize = pageSize },
                commandType: CommandType.StoredProcedure, commandTimeout: 120, cancellationToken: cancellationToken));
            return new ImportChanges
            {
                FileLogId = fileLogId,
                Kinds = (await results.ReadAsync<ImportChangeKind>()).ToList(),
                Items = (await results.ReadAsync<ImportChange>()).ToList(),
                Kind = kind,
                Search = search,
                PageIndex = pageIndex,
                PageSize = pageSize ?? int.MaxValue
            };
        }
    }
}
