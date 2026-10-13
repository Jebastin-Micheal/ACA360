using System;
using System.Collections.Generic;
using System.Linq;

namespace ACA360.Core.Models
{
    public sealed class ImportAnalysisRequest
    {
        public int FileLogId { get; set; }
        public string Entity { get; set; } = "Employer";
        public string ImportMode { get; set; } = "Merge";
        public int Draw { get; set; }
        public int Start { get; set; }
        public int Length { get; set; } = 10;
        public string? Search { get; set; }
        public string SortColumn { get; set; } = "rowNumber";
        public string SortDirection { get; set; } = "asc";
        public string ImpactFilter { get; set; } = "All";
        public string ValidationFilter { get; set; } = "All";

        private static readonly string[] RowColumns = { "rowNumber", "actionType", "validationStatus" };

        public string? Validate()
        {
            var sheet = ImportAnalysisCatalog.Find(Entity);
            if (FileLogId <= 0 || sheet == null) return "Select a valid file and sheet.";
            if (!ImportAnalysisCatalog.IsMode(ImportMode)) return "Invalid import mode.";
            if (Draw < 0 || Start < 0 || Length is < 1 or > 100) return "Page size must be between 1 and 100.";

            // The web app's JSON converter upper-cases every string, other callers do not, so
            // compare without case and pass one canonical spelling on (sort column = catalog key).
            var direction = SortDirection?.ToUpperInvariant();
            if (direction is not ("ASC" or "DESC")) return "Invalid sort direction.";
            var sortColumn = RowColumns.FirstOrDefault(c => string.Equals(c, SortColumn, StringComparison.OrdinalIgnoreCase))
                ?? sheet.Columns.FirstOrDefault(c => string.Equals(c.Key, SortColumn, StringComparison.OrdinalIgnoreCase))?.Key;
            if (sortColumn == null) return "Invalid sort column.";
            var impact = ImpactFilter?.ToUpperInvariant();
            if (impact is not ("ALL" or "INSERT" or "UPDATE" or "NOCHANGE" or "REMOVE" or "EXCLUDED"))
                return "Invalid change filter.";
            var validation = ValidationFilter?.ToUpperInvariant();
            if (validation is not ("ALL" or "CLEAN" or "WARNING" or "ERROR" or "INFORMATION"))
                return "Invalid validation filter.";
            if (Search?.Length > 200) return "Search must be 200 characters or fewer.";
            Entity = sheet.Entity;
            SortDirection = direction;
            SortColumn = sortColumn;
            ImpactFilter = impact;
            ValidationFilter = validation;
            Search = Search?.Trim();
            return null;
        }
    }

    public sealed class ImportAnalysisPage
    {
        public int RecordsTotal { get; set; }
        public int RecordsFiltered { get; set; }
        public IReadOnlyList<IDictionary<string, object?>> Rows { get; set; } = Array.Empty<IDictionary<string, object?>>();
    }

    public sealed class ImportAnalysisEntitySummary
    {
        public string EntityType { get; set; } = "";
        // Total is uploaded rows. Removed live rows are separate and do not inflate file quality.
        public int Total { get; set; }
        public int Inserts { get; set; }
        public int Updates { get; set; }
        public int Skips { get; set; }
        public int Removes { get; set; }
        public int Excluded { get; set; }
        public int CleanRows { get; set; }
        public int WarningRows { get; set; }
        public int ErrorRows { get; set; }
        public int InformationRows { get; set; }
    }

    public sealed class ImportAnalysisRemovalSummary
    {
        public string EntityType { get; set; } = "";
        public int Removes { get; set; }
    }

    public sealed class ImportAnalysisSummary
    {
        public IReadOnlyList<ImportAnalysisEntitySummary> Entities { get; set; } = Array.Empty<ImportAnalysisEntitySummary>();
        public IReadOnlyList<ImportAnalysisRemovalSummary> RelatedRemovals { get; set; } = Array.Empty<ImportAnalysisRemovalSummary>();
    }

    // ── "What changed" (script 057): the file compared with what the system holds ──
    public sealed class ImportChangeKind
    {
        public string Kind { get; set; } = "";
        public string Label { get; set; } = "";
        public int Changes { get; set; }
    }

    public sealed class ImportChange
    {
        public string Kind { get; set; } = "";
        public string Entity { get; set; } = "";
        public int? RowNumber { get; set; }
        public string? Name { get; set; }
        public string? Field { get; set; }
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
        public string? Note { get; set; }
        public int TotalCount { get; set; }
    }

    public sealed class ImportChanges
    {
        public int FileLogId { get; set; }
        public IReadOnlyList<ImportChangeKind> Kinds { get; set; } = Array.Empty<ImportChangeKind>();
        public IReadOnlyList<ImportChange> Items { get; set; } = Array.Empty<ImportChange>();
        public string? Kind { get; set; }
        public string? Search { get; set; }
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 25;
        public int TotalCount => Items.Count == 0 ? 0 : Items[0].TotalCount;
        public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)Math.Max(1, PageSize)));
    }
}
