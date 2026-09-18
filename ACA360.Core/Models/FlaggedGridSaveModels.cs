using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ACA360.Core.Models
{
    // Remove any OLD nested SaveGridRequest in FlagsController so it cannot shadow this type.
    public sealed class SaveGridRequest
    {
        public int FilingYear { get; set; }
        public List<FlaggedEmployeeEdit> Edits { get; set; } = new();
    }

    public sealed class FlaggedGridSaveResult
    {
        public int Saved { get; set; }
        public int Failed { get; set; }
        public int SavedCells { get; set; }
        public int AuditFailures { get; set; }
        public List<string> Warnings { get; set; } = new();
        public bool FlagRefreshRequired { get; set; }
        // Internal server data: never return employee field values in the response.
        [JsonIgnore]
        public List<FlaggedGridAuditChange> AuditChanges { get; set; } = new();
    }

    public sealed class FlaggedGridAuditChange
    {
        public string TableName { get; set; } = string.Empty;
        public int EmployeeId { get; set; }
        public int EmployeeCodeId { get; set; }
        public int RowId { get; set; }
        public string OldValuesJson { get; set; } = "{}";
        public string NewValuesJson { get; set; } = "{}";
    }
}
