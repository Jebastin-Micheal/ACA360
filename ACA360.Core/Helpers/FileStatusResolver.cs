namespace ACA360.Core.Helpers
{
    // ── Validation Status IDs (10-99) ─────────────────────
    public static class VS
    {
        public const int Pending = 10;
        public const int PreProcessing = 20;
        public const int Validating = 30;
        public const int StructureError = 40;
        public const int MappingRequired = 41;
        public const int DataError = 50;
        public const int Partial = 60;
        public const int ValidWithWarnings = 70;
        public const int Clean = 80;
        public const int Imported = 90;
        public const int Failed = 99;
    }

    // ── Workflow Status IDs (200-299) ──────────────────────────
    public static class WS
    {
        public const int PreProcessing = 200;
        public const int SystemRejected = 209;
        public const int PendingAssignment = 210;
        public const int DAReviewing = 211;
        public const int Processing = 220;
        public const int NeedsCorrection = 230;
        public const int AwaitingClientInput = 231;
        public const int AMRejected = 241;
        public const int QueuedForImport = 250;
        public const int Importing = 251;
        public const int Complete = 260;
        public const int ImportFailed = 261;
        public const int DARejected = 290;
        public const int OnHold = 298;
        public const int Superseded = 299;
    }

    /// <summary>
    /// Which group a file falls in. The summary cards and the status filter chips
    /// count and filter by these, so they always agree with the row labels.
    /// Mirrored in SQL by dbo.fn_FileStatusBucket — change both together.
    /// </summary>
    public enum FileStatusBucket
    {
        Other,
        Working,
        NeedsAssignment,
        NeedsFixes,
        ReadyToImport,
        ImportFailed,
        Imported,
        OnHold,
        Closed
    }

    // ── Resolved display state — drives the entire row UI ────────────────────
    public class FileDisplayState
    {
        public FileStatusBucket Bucket { get; init; }
        public string Label { get; init; } = "";
        public string Color { get; init; } = "secondary";
        public string Icon { get; init; } = "bx-file";
        public bool IsWorking { get; init; }
        public bool IsTerminal { get; init; }
        public bool HasFixableErrors { get; init; }
        public bool IsOnHold { get; init; }
        /// <summary>Assignment and reject controls are hidden: the file is finished, dead, or importing.</summary>
        public bool IsLocked { get; init; }
        public string RowClass { get; init; } = "";
        public string Tooltip { get; init; } = "";
        /// <summary>Index into <see cref="FileStatusResolver.PipelineSteps"/>; -1 hides the stepper.</summary>
        public int PipelineStep { get; init; } = -1;
    }

    public static class FileStatusResolver
    {
        public static readonly string[] PipelineSteps =
            { "Uploaded", "Structure check", "Assigned", "Validation", "Review & fix", "Imported" };

        private const int StepStructure = 1, StepAssigned = 2, StepValidation = 3, StepReview = 4, StepImported = 5;

        /// <summary>
        /// The one place a file's status is decided. The file grid, the live status
        /// poller and (through dbo.fn_FileStatusBucket) the summary cards and filter
        /// chips all use it. Rule order matters and matches the SQL function.
        /// </summary>
        public static FileDisplayState Resolve(int vs, int ws, bool isAssigned)
        {
            if (ws == WS.OnHold || ws == WS.AwaitingClientInput)
                return new()
                {
                    Bucket = FileStatusBucket.OnHold,
                    Label = ws == WS.OnHold ? "On hold" : "Waiting on client",
                    Color = "secondary",
                    Icon = "bx-pause-circle",
                    IsOnHold = true,
                    RowClass = "table-secondary border-start border-2 border-secondary",
                    Tooltip = ws == WS.OnHold ? "Processing paused by a manager." : "Waiting for information from the client."
                };

            if (ws == WS.Superseded)
                return new()
                {
                    Bucket = FileStatusBucket.Closed,
                    Label = "Replaced",
                    Color = "secondary",
                    Icon = "bx-archive",
                    IsTerminal = true,
                    IsLocked = true,
                    Tooltip = "This file was replaced by a newer upload."
                };

            // A file that failed the structure check is dead whether or not anyone is assigned.
            if (vs == VS.StructureError)
                return new()
                {
                    Bucket = FileStatusBucket.Closed,
                    Label = "Invalid file",
                    Color = "danger",
                    Icon = "bx-x-circle",
                    IsTerminal = true,
                    IsLocked = true,
                    PipelineStep = StepStructure,
                    RowClass = "table-secondary border-start border-2 border-secondary",
                    Tooltip = "Wrong template or missing required columns. Fix the file and upload it again."
                };

            if (ws is WS.SystemRejected or WS.AMRejected or WS.DARejected)
                return new()
                {
                    Bucket = FileStatusBucket.Closed,
                    Label = "Rejected",
                    Color = "secondary",
                    Icon = "bx-block",
                    IsTerminal = true,
                    IsLocked = true,
                    RowClass = "table-secondary border-start border-2 border-secondary",
                    Tooltip = "This file was rejected. A new upload is required."
                };

            if (ws is WS.Importing or WS.QueuedForImport)
                return new()
                {
                    Bucket = FileStatusBucket.Working,
                    Label = "Importing",
                    Color = "primary",
                    Icon = "bx-data",
                    IsWorking = true,
                    IsLocked = true,
                    PipelineStep = StepImported,
                    Tooltip = "Data is being merged into the live tables."
                };

            if (ws == WS.Complete || vs == VS.Imported)
                return new()
                {
                    Bucket = FileStatusBucket.Imported,
                    Label = "Imported",
                    Color = "success",
                    Icon = "bx-check-double",
                    IsTerminal = true,
                    IsLocked = true,
                    PipelineStep = StepImported,
                    Tooltip = "Data successfully merged into the live tables."
                };

            if (ws == WS.ImportFailed)
                return new()
                {
                    Bucket = FileStatusBucket.ImportFailed,
                    Label = "Import failed",
                    Color = "danger",
                    Icon = "bx-error-alt",
                    PipelineStep = StepImported,
                    RowClass = "table-danger border-start border-2 border-danger",
                    Tooltip = "The import did not complete. Open the error details, then try the import again."
                };

            if (ws == WS.PreProcessing || vs == VS.PreProcessing)
                return new()
                {
                    Bucket = FileStatusBucket.Working,
                    Label = "Checking file",
                    Color = "info",
                    Icon = "bx-cog",
                    IsWorking = true,
                    PipelineStep = StepStructure,
                    Tooltip = "Checking the file's sheets and column headers against the template."
                };

            // Nothing below can progress without an analyst.
            if (!isAssigned || ws == WS.PendingAssignment)
                return new()
                {
                    Bucket = FileStatusBucket.NeedsAssignment,
                    Label = "Waiting for analyst",
                    Color = "warning",
                    Icon = "bx-user-plus",
                    PipelineStep = StepAssigned,
                    Tooltip = "The file passed the structure check. Assign a Data Analyst to start validation."
                };

            if (vs == VS.Pending)
                return new()
                {
                    Bucket = FileStatusBucket.Working,
                    Label = "Queued",
                    Color = "info",
                    Icon = "bx-time-five",
                    IsWorking = true,
                    PipelineStep = StepStructure,
                    Tooltip = "Waiting for the next processing step to start."
                };

            if (vs == VS.Validating)
                return new()
                {
                    Bucket = FileStatusBucket.Working,
                    Label = "Validating",
                    Color = "info",
                    Icon = "bx-cog",
                    IsWorking = true,
                    PipelineStep = StepValidation,
                    Tooltip = "Validating every row against the ACA rules."
                };

            if (vs == VS.MappingRequired)
                return new()
                {
                    Bucket = FileStatusBucket.NeedsFixes,
                    Label = "Needs column mapping",
                    Color = "warning",
                    Icon = "bx-map-pin",
                    HasFixableErrors = true,
                    PipelineStep = StepReview,
                    RowClass = "table-warning border-start border-2 border-warning",
                    Tooltip = "Column headers do not match the template exactly. A Data Analyst must map them."
                };

            if (vs is VS.DataError or VS.Partial or VS.Failed || ws == WS.NeedsCorrection)
                return new()
                {
                    Bucket = FileStatusBucket.NeedsFixes,
                    Label = "Needs fixes",
                    Color = "danger",
                    Icon = "bx-error-circle",
                    HasFixableErrors = true,
                    PipelineStep = StepReview,
                    RowClass = "table-danger border-start border-2 border-danger",
                    Tooltip = "Row-level errors were found. Use Fix Errors to correct them."
                };

            if (vs is VS.ValidWithWarnings or VS.Clean)
                return new()
                {
                    Bucket = FileStatusBucket.ReadyToImport,
                    Label = "Ready to import",
                    Color = "info",
                    Icon = "bx-search-alt",
                    PipelineStep = StepReview,
                    Tooltip = vs == VS.Clean
                        ? "Validation passed. Review the file, then import it."
                        : "Validation passed with warnings. Review them, then import the file."
                };

            return new() { Bucket = FileStatusBucket.Other, Label = "Unknown", Color = "secondary", Icon = "bx-question-mark" };
        }
    }
}
