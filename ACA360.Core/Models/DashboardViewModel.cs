namespace ACA360.Core.Models
{
    public class DashboardViewModel
        {
            // --- EXISTING PROPERTIES (KEPT) ---
            public string? UserRole { get; set; }
            public string? UserName { get; set; }
            public string? Profile_Name { get; set; }
            public int TotalEmployers { get; set; }
            public int FilesProcessedToday { get; set; }
            public int FailedUploadsToday { get; set; }
            public int ActiveUsersCount { get; set; }
            public List<UploadedFileLog>? RecentFiles { get; set; } 
            public int PendingValidations { get; set; }
            public int PenaltyRiskClients { get; set; }
            public int FilesInQueue { get; set; }
            public int ClientsOnTrack { get; set; }
            public int ClientsMissingData { get; set; }
            public int ClientsAtRisk { get; set; }
            public int Stage_DataCollection { get; set; }
            public int Stage_CodesGenerated { get; set; }
            public int Stage_Finalized { get; set; }
            public decimal TotalPortfolioRisk { get; set; }
            public List<IrsRejectionDto>? AmRecentRejections { get; set; } 
            public int AmAcceptedCount { get; set; }
            public int AmRejectedCount { get; set; }
            public Employer360ViewModel? Employer360 { get; set; } 
           public List<UploadedFileLog>? DataAnalystQueue { get; set; } 
            public int MissingDataClients { get; set; }
            public List<AmClientPortfolioDto>? AmPortfolio { get; set; } 
            public SuperAdminDashboardData? SuperAdminData { get; set; }
            public DataAnalystDashboardData? AnalystData { get; set; }

            // --- NEW PROPERTIES FOR ADVANCED DASHBOARDS ---
            public DirectorDashboardData? DirectorData { get; set; } 
            public SupervisorDashboardData? SupervisorData { get; set; }  // Shared for DA/AM Supervisors
            public AccountingDashboardData? AccountingData { get; set; } 
            public AuditDashboardData? AuditData { get; set; } 
            public BrokerDashboardData? BrokerAdvancedData { get; set; } 
    }
    public class SuperAdminDashboardData
    {
        // Tenant Stats
        public int TotalEmployers { get; set; }
        public int NewEmployersThisMonth { get; set; }

        // File Stats
        public int TotalFiles { get; set; }
        public int FilesThisMonth { get; set; }
        public int FilesLastMonth { get; set; }

        // IRS Stats
        public int TotalFilings { get; set; }
        public int AcceptedFilings { get; set; }
        public int RejectedFilings { get; set; }

        // User Stats
        public int TotalUsers { get; set; }
        public int AdminsCount { get; set; }

        // Feed
        public List<SystemEventDto>? RecentEvents { get; set; } = new List<SystemEventDto>();

        // Calculated Properties
        public double AcceptanceRate => TotalFilings == 0 ? 0 : (double)AcceptedFilings / TotalFilings * 100;
        public int FileGrowth => FilesLastMonth == 0 ? 100 : ((FilesThisMonth - FilesLastMonth) / FilesLastMonth) * 100;
    }
    public class SystemEventDto
    {
        public int EventId { get; set; }
        public string? SourceName { get; set; } // Employer Name
        public string? EventType { get; set; }
        public string? Message { get; set; }
        public DateTime EventDate { get; set; }
        public string? Severity { get; set; } // "danger", "warning"
    }
    // If you need IrsRejectionDto explicitly, it maps to SystemEventDto in the dashboard logic
    public class IrsRejectionDto
    {
        public int SubmissionId { get; set; }
        public string? EmployerName { get; set; }
        public int TaxYear { get; set; }
        public string? StatusMessage { get; set; }
        public DateTime GeneratedDate { get; set; }
    }

    public class DataAnalystDashboardData
    {
        // Counters
        public int PendingTriage { get; set; }   // "Inbox" — errors still to clear
        public int ReadyToImport { get; set; }   // was ReadyForApproval; the AM step is gone
        public int CompletedToday { get; set; }  // "Velocity"

        // Chart Data
        public List<ChartDataPoint>? StatusDistribution { get; set; } = new List<ChartDataPoint>();
    }

    public class ChartDataPoint
    {
        public string? Label { get; set; }
        public int Value { get; set; }
    }
    public class DirectorDashboardData
    {
        // 1. Top Level Counts
        public int PastDueClients { get; set; }
        public int TotalEINs { get; set; }          // Main + Affiliates
        public int TotalUniqueClients { get; set; } // Main Clients only
        public int FormsMailedCount { get; set; }
        public int FormsEfiledCount { get; set; }

        // 2. Operational Data
        public int IrsAccepted { get; set; }
        public int IrsRejected { get; set; }
        public int IrsProcessing { get; set; }

        // 3. Process Pipeline (For the Funnel/Bar Chart)
        // Key = Step Name (e.g., "Data Collection"), Value = Count of Clients
        public Dictionary<string, int>? ProcessStepDistribution { get; set; } = new Dictionary<string, int>();

        // 4. Detailed Table Data
        public List<ClientProcessStatusRow>? ClientStatusList { get; set; } = new List<ClientProcessStatusRow>();

        // 5. Alerts
        public List<string> CriticalAlerts { get; set; } = new List<string>();
    }
    public class ClientProcessStatusRow
{
    public int EmployerId { get; set; }        // <--- NEW: For Clickable Link
    public string? EmployerName { get; set; }
    public string? AccountManagerName { get; set; } // <--- NEW: For "By AM" grouping
    public bool IsAffiliate { get; set; }
    public string? CurrentStep { get; set; }
    public string? StepColor { get; set; }
    public DateTime DueDate { get; set; }
    public bool IsPastDue { get; set; }
}
    public class SupervisorDashboardData
        {
            // ==========================================
            // 1. SHARED / COMMON STATS
            // ==========================================
            public int PastDueClients { get; set; }
            public int TotalUniqueClients { get; set; } // Main Clients (HQ)
            public int TotalEINs { get; set; }          // Total Workload (Main + Affiliates)

            // ==========================================
            // 2. DA SUPERVISOR SPECIFIC (New Features)
            // ==========================================

            // Operational Stats
            public int FormsMailedCount { get; set; }
            public int FormsEfiledCount { get; set; }

            // File Ingestion Health (Replaces simple "FilesInTriage")
            public int FilesAccepted { get; set; }
            public int FilesRejected { get; set; }
            public int FilesProcessing { get; set; }       // Maps to "FilesInTriage" logically
            public int FilesCriticalErrors { get; set; }
            public int FilesStuck48Hrs { get; set; }       // Legacy metric, keeping if needed

            // Charts & Tables (The new visual requirements)
            public Dictionary<string, int>? ProcessStepDistribution { get; set; } 
            public List<ClientProcessStatusRow>? ClientStatusList { get; set; } 
            // ==========================================
            // 3. AM SUPERVISOR SPECIFIC (Legacy/Future)
            // ==========================================
            public int ClientsAtRisk { get; set; }
            public int RenewalsSigned { get; set; }

            // Staff Workload (Shared Concept)
            // Useful for both if you want to show "Files per DA" or "Clients per AM"
            public List<StaffLoad>? StaffWorkload { get; set; } 
        }

    public class AccountingDashboardData
    {
        public decimal TotalUnbilledRevenue { get; set; }
        public int ContractsExpiringSoon { get; set; }
        public List<InvoiceItem> UnbilledClients { get; set; } = new List<InvoiceItem>();
    }

    public class AuditDashboardData
    {
        public List<string> LiveAuditStream { get; set; } = new List<string>();
        public int AnomaliesDetected { get; set; }
    }

    public class BrokerDashboardData
    {
        public int ComplianceScore { get; set; } // 0-100
        public List<AmClientPortfolioDto>? ClientLeaderboard { get; set; }
    }

    // --- HELPER CLASSES ---
    public class StaffLoad { public string? Name { get; set; } public int FileCount { get; set; } }
    public class InvoiceItem { public string? Client { get; set; } public decimal Amount { get; set; } }
}