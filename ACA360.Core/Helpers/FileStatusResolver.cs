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
        public const int ReadyForApproval = 240;
        public const int AMRejected = 241;
        public const int QueuedForImport = 250;
        public const int Importing = 251;
        public const int Complete = 260;
        public const int ImportFailed = 261;
        public const int DARejected = 290;
        public const int OnHold = 298;
        public const int Superseded = 299;
    }

    // ── Resolved display state — drives the entire row UI ────────────────────
    public class FileDisplayState
    {
        public string Label { get; set; } = "";
        public string Color { get; set; } = "secondary";
        public string Icon { get; set; } = "bx-file";
        public bool IsWorking { get; set; }
        public bool IsTerminal { get; init; }
        public bool HasFixableErrors { get; init; }
        public bool IsOnHold { get; init; }
        public string RowClass { get; init; } = "";
        public string Tooltip { get; init; } = "";
        public int PipelineStep { get; init; } = -1;  // 0-6; -1 = error/hold
    }

    public static class FileStatusResolver
    {
        public static FileDisplayState Resolve(int vs, int ws)
        {
            // On Hold / Awaiting Client
            if (ws == WS.OnHold || ws == WS.AwaitingClientInput)
                return new()
                {
                    Label = ws == WS.OnHold ? "On Hold" : "Awaiting Client",
                    Color = "secondary",
                    Icon = "bx-pause-circle",
                    IsOnHold = true,
                    PipelineStep = -1,
                    RowClass = "table-secondary border-start border-2 border-secondary",
                    Tooltip = "Processing paused. Awaiting manual intervention."
                };

            // Superseded
            if (ws == WS.Superseded)
                return new()
                {
                    Label = "Superseded",
                    Color = "secondary",
                    Icon = "bx-archive",
                    IsTerminal = true,
                    Tooltip = "This file was replaced by a newer upload."
                };

            // Active Hangfire: importing
            if (ws == WS.Importing || ws == WS.QueuedForImport)
                return new()
                {
                    Label = "Importing",
                    Color = "primary",
                    Icon = "bx-data",
                    IsWorking = true,
                    PipelineStep = 5,
                    Tooltip = "Data is being merged into production tables."
                };

            // Active Hangfire: Pre-Processing or Validating
            // Step 0: Initial Pre-Processing (Tier 1)
            if (ws == WS.PreProcessing || vs == VS.PreProcessing)
                return new()
                {
                    Label = "Initial Checks",
                    Color = "info",
                    Icon = "bx-cog",
                    IsWorking = true,
                    PipelineStep = 0, // Maps to "Pre-Process"
                    Tooltip = "Running Tier 1 structure validation."
                };

            // Step 1: DA Claimed (but deep processing hasn't started yet)
            if (ws == WS.DAReviewing)
                return new()
                {
                    Label = "Claimed",
                    Color = "info",
                    Icon = "bx-user-check",
                    IsWorking = true,
                    PipelineStep = 1, // Maps to "Assign"
                    Tooltip = "DA claimed the file. Waiting for deep processing."
                };

            // Step 2: Active Hangfire Deep Processing (Tier 2)
            if (ws == WS.Processing || vs == VS.Validating)
                return new()
                {
                    Label = "Processing",
                    Color = "info",
                    Icon = "bx-cog",
                    IsWorking = true,
                    PipelineStep = 2, // Maps to "Validate"
                    Tooltip = "Running Tier 2 ACA validation logic."
                };

            // Terminal success
            if (ws == WS.Complete || vs == VS.Imported)
                return new()
                {
                    Label = "Imported",
                    Color = "success",
                    Icon = "bx-check-double",
                    IsTerminal = true,
                    PipelineStep = 6,
                    Tooltip = "Data successfully merged into live tables."
                };

            // Awaiting AM approval
            if (ws == WS.ReadyForApproval)
                return new()
                {
                    Label = "Awaiting Approval",
                    Color = "info",
                    Icon = "bx-time",
                    PipelineStep = 4,
                    RowClass = "table-info border-start border-2 border-info",
                    Tooltip = "Validation passed. Account Manager must approve before import."
                };

            // Terminal Rejections (AM, DA, or System)
            if (ws is WS.AMRejected or WS.DARejected or WS.SystemRejected)
                return new()
                {
                    Label = "Rejected",
                    Color = "secondary",
                    Icon = "bx-block",
                    IsTerminal = true,
                    RowClass = "table-secondary border-start border-2 border-secondary",
                    Tooltip = "File was rejected. A new upload is required."
                };

            // Pending assignment
            if (ws == WS.PendingAssignment)
                return new()
                {
                    Label = "Pending Assignment",
                    Color = "warning",
                    Icon = "bx-user-plus",
                    PipelineStep = 1,
                    RowClass = "table-warning border-start border-2 border-warning",
                    Tooltip = "Passed structure check. Waiting for a DA to accept."
                };

            // Mapping Required (New State)
            if (vs == VS.MappingRequired)
                return new()
                {
                    Label = "Mapping Required",
                    Color = "warning",
                    Icon = "bx-map-pin",
                    HasFixableErrors = true,
                    PipelineStep = 2,
                    RowClass = "table-warning border-start border-2 border-warning",
                    Tooltip = "Column headers do not match template perfectly. DA must map them."
                };

            // Fixable data errors / Partial
            if (vs is VS.DataError or VS.Partial || ws == WS.NeedsCorrection)
                return new()
                {
                    Label = "Data Errors",
                    Color = "danger",
                    Icon = "bx-error-circle",
                    HasFixableErrors = true,
                    PipelineStep = 3,
                    RowClass = "table-danger border-start border-2 border-danger",
                    Tooltip = "Critical row-level errors found. Use Error Triage to fix."
                };

            // Structure error
            if (vs == VS.StructureError)
                return new()
                {
                    Label = "Invalid File",
                    Color = "danger",
                    Icon = "bx-x-circle",
                    IsTerminal = true,
                    RowClass = "table-danger border-start border-2 border-danger",
                    Tooltip = "Missing required columns or wrong template. Please re-upload."
                };

            // System / import failure
            if (vs == VS.Failed || ws == WS.ImportFailed)
                return new()
                {
                    Label = "System Error",
                    Color = "danger",
                    Icon = "bx-error-alt",
                    IsTerminal = true,
                    RowClass = "table-danger border-start border-2 border-danger",
                    Tooltip = "Unhandled system error. Check Hangfire logs."
                };

            // Non-fatal warnings
            if (vs == VS.ValidWithWarnings)
                return new()
                {
                    Label = "Warnings",
                    Color = "warning",
                    Icon = "bx-error",
                    PipelineStep = 3,
                    Tooltip = "Non-fatal warnings present. File is importable."
                };

            // Fully clean
            if (vs == VS.Clean)
                return new()
                {
                    Label = "Clean",
                    Color = "success",
                    Icon = "bx-check-circle",
                    PipelineStep = 3,
                    Tooltip = "Perfect data. Ready for import."
                };

            return new() { Label = "Unknown", Color = "secondary", Icon = "bx-question-mark" };
        }
    }
}