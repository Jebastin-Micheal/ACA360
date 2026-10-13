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

            // 5. Deadlines: the IRS dates in dbo.FilingDeadlines (script 063), else 1 March and 31 March.
            dto.MailingDeadline = new DateTime(year + 1, 3, 1);
            dto.EfilingDeadline = new DateTime(year + 1, 3, 31);
            try
            {
                using var dates = Connection;
                var irs = (await dates.QueryAsync<(string Kind, DateTime DueDate)>(
                    "SELECT Kind, CAST(DueDate AS DATETIME) FROM dbo.FilingDeadlines WHERE IsActive = 1 AND Jurisdiction = 'IRS' AND FilingYear = @year AND Kind IN ('Furnish', 'E-file')",
                    new { year })).ToList();
                foreach (var (kind, due) in irs)
                    if (kind == "Furnish") dto.MailingDeadline = due; else dto.EfilingDeadline = due;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetEmployerDashboardParityAsync), nameof(DashboardService), "Filing deadlines could not be read; using 1 and 31 March.");
            }

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

        public async Task<(List<EmployerManagerLink> Links, List<StaffLoad> Workload)> GetDashboardAccountManagersAsync(int userId, int year)
        {
            using var db = Connection;
            using var multi = await db.QueryMultipleAsync(
                "sp_GetDashboardAccountManagers",
                new { UserId = userId, Year = year },
                commandType: CommandType.StoredProcedure);

            var links = (await multi.ReadAsync<EmployerManagerLink>()).ToList();
            var workload = (await multi.ReadAsync<StaffLoad>()).ToList();
            return (links, workload);
        }

        public async Task<(List<AnalystFileCounts> Counts, List<DashboardQueueFile> Queue)> GetDashboardFileCountsAsync(int year, int? onlyAssignedTo)
        {
            using var db = Connection;
            using var multi = await db.QueryMultipleAsync(
                "sp_GetDashboardFileCounts",
                new { Year = year, OnlyAssignedTo = onlyAssignedTo },
                commandType: CommandType.StoredProcedure);

            var counts = (await multi.ReadAsync<AnalystFileCounts>()).ToList();
            var queue = (await multi.ReadAsync<DashboardQueueFile>()).ToList();
            return (counts, queue);
        }

        public async Task<DashboardSystemStats> GetDashboardSystemStatsAsync()
        {
            using var db = Connection;
            return await db.QueryFirstOrDefaultAsync<DashboardSystemStats>(
                       "sp_GetDashboardSystemStats", commandType: CommandType.StoredProcedure)
                   ?? new DashboardSystemStats();
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