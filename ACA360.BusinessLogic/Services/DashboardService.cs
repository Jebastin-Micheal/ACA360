using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Models;
using ACA360.Core.Models.Dashboard;
using ACA360.Logging.Interfaces;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;

namespace ACA360.BusinessLogic.Services
{
    public class DashboardService : IDashboardService
    {
        private readonly string _connectionString;
        private readonly ILoggerService _logger;
        private readonly IEmployerService _employer;

        /// <summary>
        /// Purpose: Initializes the DashboardService with necessary dependencies.
        /// Input parameters: string connectionString, ILoggerService logger, IEmployerService employer
        /// Output/return value: None
        /// </summary>
        public DashboardService(string connectionString, ILoggerService logger, IEmployerService employer)
        {
            try
            {
                _connectionString = connectionString;
                _logger = logger;
                _employer = employer;
            }
            catch (Exception ex)
            {
                if (logger != null)
                {
                    // Fallback log if initialization fails
                    logger.LogError(ex, nameof(DashboardService), nameof(DashboardService), "Error initializing DashboardService.");
                }
                throw;
            }
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        // ─────────────────────────────────────────────────────
        // COMMON STATS
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Purpose: Retrieves common dashboard statistics for a given user.
        /// Input parameters: string userId
        /// Output/return value: Task of DashboardStatsModel
        /// </summary>
        public async Task<DashboardStatsModel> GetDashboardStatsAsync(string userId)
        {
            try
            {
                if (!int.TryParse(userId, out int userIdInt))
                    return new DashboardStatsModel();

                using var db = Connection;
                return await db.QuerySingleOrDefaultAsync<DashboardStatsModel>(
                    "sp_GetDashboardStats",
                    new { UserId = userIdInt },          // INT — matches SP param
                    commandType: CommandType.StoredProcedure)
                    ?? new DashboardStatsModel();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetDashboardStatsAsync), nameof(DashboardService), "Error occurred in GetDashboardStatsAsync for UserId: {UserId}", userId);
                throw;
            }
        }

        // ─────────────────────────────────────────────────────
        // SUPER ADMIN
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Purpose: Retrieves advanced dashboard statistics and recent system events for Super Admins.
        /// Input parameters: None
        /// Output/return value: Task of SuperAdminDashboardData
        /// </summary>
        public async Task<SuperAdminDashboardData> GetAdvancedSuperAdminStatsAsync()
        {
            try
            {
                using var db = Connection;
                using var multi = await db.QueryMultipleAsync(
                    "sp_GetAdvancedSuperAdminStats",
                    commandType: CommandType.StoredProcedure);

                var events = (await multi.ReadAsync<SystemEventDto>()).ToList();
                var stats = await multi.ReadFirstAsync<dynamic>();

                return new SuperAdminDashboardData
                {
                    TotalEmployers = (int)stats.TotalEmployers,
                    NewEmployersThisMonth = (int)stats.NewEmployersThisMonth,
                    TotalFiles = (int)stats.TotalFiles,
                    FilesThisMonth = (int)stats.FilesThisMonth,
                    FilesLastMonth = (int)stats.FilesLastMonth,
                    TotalFilings = (int)stats.TotalFilings,
                    AcceptedFilings = (int)stats.AcceptedFilings,
                    RejectedFilings = (int)stats.RejectedFilings,
                    TotalUsers = (int)stats.TotalUsers,
                    AdminsCount = (int)stats.AdminsCount,
                    RecentEvents = events
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetAdvancedSuperAdminStatsAsync), nameof(DashboardService), "Error occurred in GetAdvancedSuperAdminStatsAsync.");
                throw;
            }
        }

        // ─────────────────────────────────────────────────────
        // ACA DIRECTOR
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Purpose: Calculates and retrieves director-level statistics and client status lists.
        /// Input parameters: string userId
        /// Output/return value: Task of DirectorDashboardData
        /// </summary>
        public async Task<DirectorDashboardData> GetDirectorStatsAsync(string userId)
        {
            try
            {
                var data = new DirectorDashboardData();
                var allEmployers = await _employer.GetAllEmployersAsync(userId);

                data.TotalUniqueClients = allEmployers.Count(x => x.CompanyId == null || x.CompanyId == 0);
                data.TotalEINs = allEmployers.Count();
                data.FormsMailedCount = allEmployers.Count(x => x.IsMailed);
                data.FormsEfiledCount = allEmployers.Count(x => x.IsEfiled);
                data.PastDueClients = allEmployers.Count(x => x.StepDueDate < DateTime.Now
                                                              && x.StepDueDate != default);

                data.ProcessStepDistribution = allEmployers
                    .Where(x => !string.IsNullOrEmpty(x.CurrentStepName))
                    .GroupBy(x => x.CurrentStepName!)
                    .ToDictionary(g => g.Key, g => g.Count());

                data.ClientStatusList = allEmployers.Select(x => new ClientProcessStatusRow
                {
                    EmployerId = int.TryParse(x.Id, out int eid) ? eid : 0,  // Id is string
                    EmployerName = x.Name ?? string.Empty,
                    IsAffiliate = x.CompanyId > 0,
                    CurrentStep = x.CurrentStepName ?? "Unknown",
                    StepColor = MapStepToColor(x.CurrentStepName),
                    DueDate = x.StepDueDate != default
                                             ? x.StepDueDate
                                             : DateTime.Now.AddMonths(3),
                    IsPastDue = x.StepDueDate != default && x.StepDueDate < DateTime.Now,
                    AccountManagerName = null   // not on Employer model; populate via separate query if needed
                }).ToList();

                data.CriticalAlerts = new List<string>();
                if (data.IrsRejected > 5)
                    data.CriticalAlerts.Add($"High volume of IRS rejections detected ({data.IrsRejected}).");
                if (data.PastDueClients > 10)
                    data.CriticalAlerts.Add($"{data.PastDueClients} clients are past the filing deadline.");

                return data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetDirectorStatsAsync), nameof(DashboardService), "Error occurred in GetDirectorStatsAsync for UserId: {UserId}", userId);
                throw;
            }
        }

        // ─────────────────────────────────────────────────────
        // DATA ANALYST
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Purpose: Retrieves statistics and status distributions specifically for Data Analysts.
        /// Input parameters: string userId
        /// Output/return value: Task of DataAnalystDashboardData
        /// </summary>
        public async Task<DataAnalystDashboardData> GetDataAnalystStatsAsync(string userId)
        {
            try
            {
                if (!int.TryParse(userId, out int userIdInt))
                    return new DataAnalystDashboardData();

                using var db = Connection;
                using var multi = await db.QueryMultipleAsync(
                    "sp_GetDataAnalystStats",
                    new { UserId = userIdInt },
                    commandType: CommandType.StoredProcedure);

                var chartData = (await multi.ReadAsync<ChartDataPoint>()).ToList();
                var stats = await multi.ReadFirstAsync<dynamic>();

                return new DataAnalystDashboardData
                {
                    PendingTriage = (int)stats.PendingTriage,
                    ReadyToImport = (int)stats.ReadyToImport,
                    CompletedToday = (int)stats.CompletedToday,
                    StatusDistribution = chartData
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetDataAnalystStatsAsync), nameof(DashboardService), "Error occurred in GetDataAnalystStatsAsync for UserId: {UserId}", userId);
                throw;
            }
        }

        // ─────────────────────────────────────────────────────
        // SUPERVISOR
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Purpose: Retrieves comprehensive supervisor stats, workload distribution, and process charts.
        /// Input parameters: string userId, string role
        /// Output/return value: Task of SupervisorDashboardData
        /// </summary>
        public async Task<SupervisorDashboardData> GetSupervisorStatsAsync(string userId, string role)
        {
            try
            {
                if (!int.TryParse(userId, out int userIdInt))
                    return new SupervisorDashboardData();

                using var db = Connection;
                using var multi = await db.QueryMultipleAsync(
                    "sp_GetSupervisorStats",
                    new { UserId = userIdInt, Role = role, Year = DateTime.Now.Year },
                    commandType: CommandType.StoredProcedure);

                // RS1: scalars mapped directly onto SupervisorDashboardData
                var data = await multi.ReadSingleOrDefaultAsync<SupervisorDashboardData>()
                           ?? new SupervisorDashboardData();

                // RS2: process step chart { StepName, ClientCount }
                var chartRows = await multi.ReadAsync<dynamic>();
                data.ProcessStepDistribution = chartRows.ToDictionary(
                    row => (string?)row.StepName ?? "Unknown",
                    row => (int?)row.ClientCount ?? 0
                );

                // RS3: client status table
                var tableRows = await multi.ReadAsync<ClientProcessStatusRow>();
                data.ClientStatusList = tableRows.ToList();

                // RS4: staff workload (StaffLoad: Name, FileCount)
                var staffRows = await multi.ReadAsync<StaffLoad>();
                data.StaffWorkload = staffRows.ToList();

                return data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetSupervisorStatsAsync), nameof(DashboardService), "Error occurred in GetSupervisorStatsAsync for UserId: {UserId}, Role: {Role}", userId);
                throw;
            }
        }

        // ─────────────────────────────────────────────────────
        // ACCOUNTING STAFF
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Purpose: Retrieves accounting-specific dashboard stats including unbilled clients and revenue.
        /// Input parameters: None
        /// Output/return value: Task of AccountingDashboardData
        /// </summary>
        public async Task<AccountingDashboardData> GetAccountingStatsAsync()
        {
            try
            {
                using var db = Connection;
                using var result = await db.QueryMultipleAsync(
                    "sp_GetAccountingStats",
                    commandType: CommandType.StoredProcedure);

                var stats = await result.ReadSingleOrDefaultAsync<AccountingDashboardData>();
                var invoices = (await result.ReadAsync<InvoiceItem>()).ToList();

                if (stats != null)
                    stats.UnbilledClients = invoices;

                return stats ?? new AccountingDashboardData();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetAccountingStatsAsync), nameof(DashboardService), "Error occurred in GetAccountingStatsAsync.");
                throw;
            }
        }

        // ─────────────────────────────────────────────────────
        // AUDIT STAFF
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Purpose: Retrieves live audit streams and anomaly detection stats for Audit Staff.
        /// Input parameters: None
        /// Output/return value: Task of AuditDashboardData
        /// </summary>
        public async Task<AuditDashboardData> GetAuditStatsAsync()
        {
            try
            {
                using var db = Connection;
                using var result = await db.QueryMultipleAsync(
                    "sp_GetAuditStats",
                    commandType: CommandType.StoredProcedure);

                var stats = await result.ReadSingleOrDefaultAsync<AuditDashboardData>();
                var logs = (await result.ReadAsync<string>()).ToList();

                if (stats != null)
                    stats.LiveAuditStream = logs;

                return stats ?? new AuditDashboardData();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetAuditStatsAsync), nameof(DashboardService), "Error occurred in GetAuditStatsAsync.");
                throw;
            }
        }

        // ─────────────────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Purpose: Maps a processing step name to a corresponding UI color class.
        /// Input parameters: string? step
        /// Output/return value: string
        /// </summary>
        private string MapStepToColor(string? step)
        {
            try
            {
                return step switch
                {
                    "Data Collection" => "secondary",
                    "Validation" => "warning",
                    "Triage" => "danger",
                    "Codes Generated" => "info",
                    "Filed" => "success",
                    "Accepted" => "success",
                    _ => "secondary"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(MapStepToColor), nameof(DashboardService), "Error occurred in MapStepToColor.");
                throw;
            }
        }

        public async Task<EmployerDashboardDto> GetEmployerDashboardParityAsync(int employerId, int year, int userId = 0)
        {
            var dto = new EmployerDashboardDto();

            using (var db = Connection)
            {
                // 1. Fetch snapshot counts from sp_GetEmployerDashboardCounts
                var countsResult = await db.QueryFirstOrDefaultAsync<EmployerDashboardDto>(
                    "[dbo].[sp_GetEmployerDashboardCounts]",
                    new { EmployerId = employerId, Year = year },
                    commandType: CommandType.StoredProcedure);

                if (countsResult != null)
                {
                    dto.TotalEmployees = countsResult.TotalEmployees;   // ADDED
                    dto.TotalFullTime = countsResult.TotalFullTime;    // ADDED
                    dto.TotalPartTime = countsResult.TotalPartTime;    // ADDED
                    dto.FtReceivingForms = countsResult.FtReceivingForms;
                    dto.PtReceivingForms = countsResult.PtReceivingForms;
                    dto.TotalEnrolled = countsResult.TotalEnrolled;
                    dto.TotalWaived = countsResult.TotalWaived;
                    dto.TotalCobra = countsResult.TotalCobra;
                    dto.TotalRetiree = countsResult.TotalRetiree;
                    dto.TotalUnion = countsResult.TotalUnion;
                    dto.MostRecentHire = countsResult.MostRecentHire;
                    dto.MostRecentTermination = countsResult.MostRecentTermination;
                }

                // 2. Fetch monthly trend splits for Chart.js graphics
                var monthlyResult = await db.QueryAsync<MonthlyDashboardRow>(
                    "[dbo].[sp_GetEmployerDashboardMonthly]",
                    new { EmployerId = employerId, Year = year },
                    commandType: CommandType.StoredProcedure);

                if (monthlyResult != null)
                {
                    dto.Monthly = monthlyResult.ToList();
                }

                // 3. RECENT PROCESS STEP TRACKER LOGIC
                // We order by Id DESC or an Updated/Created date to grab the most recent process event in history
                // TODO: [FILING-WORKFLOW WEBNAME INTEGRATION] The dashboard is client-facing, so the
                // Filing Process Workflow must show the process step's WebName (the customer-friendly
                // label) instead of the internal ProcessName stored on Tracker_EmployerService.
                // Falls back to the internal name when WebName is blank/unmapped, so nothing shows empty.
                // NOTE: Tracker_Process.IsDoNotDisplay marks steps that should be hidden from the web -
                // not applied here yet; decide whether such steps should render a generic status instead.
                const string processSql = @"
            SELECT TOP 1
                COALESCE(NULLIF(LTRIM(RTRIM(tp.WebName)), ''), esi.ProcessStep1095) AS CurrentProcessStep,
                COALESCE(esi.NextFollowUp1095, esi.SpecialDateDeadline) AS ProcessDueDate,
                ISNULL(tp.CompletionPercentage, 0) AS ProcessCompletePercent
            FROM Tracker_EmployerService esi
            LEFT JOIN Tracker_Process tp ON tp.ProcessName = esi.ProcessStep1095
            WHERE esi.EmployerId = @EmployerId AND esi.PlanYear = @Year AND esi.IsDeleted = 0
            ORDER BY esi.EmployerServiceId DESC";

                var processRow = await db.QueryFirstOrDefaultAsync<dynamic>(processSql, new { EmployerId = employerId, Year = year });

                if (processRow != null)
                {
                    dto.CurrentProcessStep = processRow.CurrentProcessStep ?? "No Active Process Step";
                    // ProcessDueDate columns are nvarchar in the DB; parse defensively so a non-date/empty value doesn't throw.
                    dto.ProcessDueDate = DateTime.TryParse(Convert.ToString(processRow.ProcessDueDate), out DateTime parsedDue)
                        ? parsedDue
                        : (DateTime?)null;
                    dto.ProcessCompletePercent = Convert.ToInt32(processRow.ProcessCompletePercent);
                }
                else
                {
                    dto.CurrentProcessStep = "No Active Process Step";
                    dto.ProcessCompletePercent = 0;
                }

                // 4. Message Summary Counts (per-user; tbl_UserNotifications has no EmployerId, only UserId)
                const string msgSql = @"
            SELECT
                SUM(CASE WHEN IsRead = 1 THEN 1 ELSE 0 END) AS MessagesRead,
                SUM(CASE WHEN IsRead = 0 THEN 1 ELSE 0 END) AS MessagesUnread
            FROM tbl_UserNotifications
            WHERE UserId = @UserId";

                var msgResult = await db.QueryFirstOrDefaultAsync<dynamic>(msgSql, new { UserId = userId });
                if (msgResult != null)
                {
                    dto.MessagesRead = msgResult.MessagesRead ?? 0;
                    dto.MessagesUnread = msgResult.MessagesUnread ?? 0;
                }
            }

            // 5. Compute Deadlines mathematically
            dto.MailingDeadline = new DateTime(year + 1, 3, 1);
            dto.EfilingDeadline = new DateTime(year + 1, 3, 31);

            return dto;
        }

        /// <summary>
        /// Purpose: Saves the signed-in user's chosen dashboard widget order AND which
        /// widgets they've hidden. One row per user (not per-employer) - this is a global
        /// preference, not tied to whichever employer they happen to be viewing.
        /// Input parameters: int userId, DashboardLayoutDto layout (widget keys in display
        /// order, plus the widget keys currently hidden)
        /// Output/return value: Task
        /// </summary>
        public async Task SaveDashboardLayoutAsync(int userId, DashboardLayoutDto layout, string dashboardKey)
        {
            using var db = Connection;

            var json = JsonSerializer.Serialize(layout ?? new DashboardLayoutDto());

            await db.ExecuteAsync(
                "sp_SaveUserDashboardLayout",
                new { UserId = userId, WidgetOrder = json,dashboardKey=dashboardKey },
                commandType: CommandType.StoredProcedure);
        }
        public async Task<dynamic?> GetPenaltySummaryAsync(int employerId, int year)
        {
            using var db = Connection;
            return await db.QueryFirstOrDefaultAsync(
                "sp_GetEmployerPenaltyCard",
                new { EmployerId = employerId, Year = year },
                commandType: CommandType.StoredProcedure);
        }
        public async Task<int[]> GetCoverageMapAsync(int employerId, int year)
        {
            using var db = Connection;
            var rows = await db.QueryAsync<(int MonthNum, int PercentComplete)>(
                "sp_GetEmployerCoverageMap",
                new { EmployerId = employerId, Year = year },
                commandType: CommandType.StoredProcedure);

            var map = new int[12];
            foreach (var r in rows)
                if (r.MonthNum >= 1 && r.MonthNum <= 12) map[r.MonthNum - 1] = r.PercentComplete;
            return map;
        }
        /// <summary>
        /// Purpose: Retrieves the signed-in user's saved dashboard widget order and hidden
        /// widgets, if any.
        /// Input parameters: int userId
        /// Output/return value: Task of DashboardLayoutDto - both lists empty if the user
        /// has never saved a layout yet (the frontend then just keeps the default, with
        /// nothing hidden).
        /// </summary>
        public async Task<DashboardLayoutDto> GetDashboardLayoutAsync(int userId, string dashboardKey)
        {
            using var db = Connection;

            var json = await db.ExecuteScalarAsync<string>(
                "sp_GetUserDashboardLayout",
                new { UserId = userId, dashboardKey = dashboardKey },
                commandType: CommandType.StoredProcedure);

            if (string.IsNullOrWhiteSpace(json)) return new DashboardLayoutDto();

            try
            {
                return JsonSerializer.Deserialize<DashboardLayoutDto>(json) ?? new DashboardLayoutDto();
            }
            catch
            {
                // Corrupt/unparsable saved value - treat it the same as "nothing saved yet"
                // rather than failing the whole dashboard load over a layout preference.
                return new DashboardLayoutDto();
            }
        }
        public async Task<PortfolioCountsDto> GetPortfolioCountsAsync(int userId, int year)
        {
            using var db = Connection;
            return await db.QueryFirstOrDefaultAsync<PortfolioCountsDto>(
                       "sp_GetPortfolioDashboardCounts",
                       new { UserId = userId, Year = year },
                       commandType: CommandType.StoredProcedure)
                   ?? new PortfolioCountsDto();
        }

        public async Task<List<PortfolioProcessRow>> GetPortfolioProcessStepsAsync(int userId, int year, int top = 25)
        {
            using var db = Connection;
            var rows = await db.QueryAsync<PortfolioProcessRow>(
                "sp_GetPortfolioProcessSteps",
                new { UserId = userId, Year = year, Top = top },
                commandType: CommandType.StoredProcedure);
            return rows.ToList();
        }

        public async Task<List<PlanSummaryRow>> GetPlanSummaryAsync(int employerId, int year)
        {
            using var db = Connection;
            var rows = await db.QueryAsync<PlanSummaryRow>(
                "sp_GetEmployerPlanSummary",
                new { EmployerId = employerId, Year = year },
                commandType: CommandType.StoredProcedure);
            return rows.ToList();
        }
    }
}