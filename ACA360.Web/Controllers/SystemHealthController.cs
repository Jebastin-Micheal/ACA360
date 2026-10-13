using System.Data;
using System.Text.RegularExpressions;
using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Constants;
using Dapper;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace ACA360.Web.Controllers
{
    /// <summary>
    /// Administration > System Health (script 062): whether the app is well. Database size and
    /// last backup, errors, slow pages (the slow-request log), e-mail, files stuck part-way, the
    /// background jobs and maintenance mode. Reads only.
    /// </summary>
    [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin)]
    public class SystemHealthController : Controller
    {
        private readonly string _connectionString;
        private readonly ISystemSettingsService _settings;
        private readonly IServiceProvider _services;
        private readonly ILogger<SystemHealthController> _logger;

        public SystemHealthController(IConfiguration configuration, ISystemSettingsService settings, IServiceProvider services,
            ILogger<SystemHealthController> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
            _settings = settings;
            _services = services;
            _logger = logger;
        }

        public sealed record DatabaseInfo(string DatabaseName, decimal? DataMb, decimal? LogMb, DateTime? LastBackup, string? BackupSource);
        public sealed record ErrorCounts(int? LastDay, int LastWeek);
        public sealed record FrequentError(string? CallSite, string? Message, int Times, DateTime Last);
        public sealed record SlowPage(string Page, int Times, int AverageMs, int SlowestMs, DateTime Last, int Users);
        public sealed record EmailStatus(string Status, int Emails, DateTime? Last);
        public sealed record StuckFile(int FileLogId, string? OriginalFileName, string? EmployerName, int? ValidationStatusId, int? WorkflowStatusId, DateTime Since);
        public sealed record JobInfo(bool Available, long Failed, long Processing, long Scheduled, long Enqueued, int Servers,
            List<(string Id, string? Cron, DateTime? LastRun, string? LastState, DateTime? NextRun)> Recurring);

        public sealed class HealthViewModel
        {
            public DatabaseInfo? Database { get; set; }
            public ErrorCounts Errors { get; set; } = new(0, 0);
            public List<FrequentError> FrequentErrors { get; set; } = new();
            public List<SlowPage> SlowPages { get; set; } = new();
            public int SlowRequests { get; set; }
            public List<EmailStatus> Emails { get; set; } = new();
            public List<StuckFile> StuckFiles { get; set; } = new();
            public JobInfo? Jobs { get; set; }
            public bool MaintenanceMode { get; set; }
            public DateTime CheckedAt { get; set; } = DateTime.Now;
        }

        // "Slow request: GET /ErrorTriage/ErrorTriageDashboard/1154 took 4895 ms"
        private static readonly Regex SlowLine = new(@"^Slow request: (?<method>\S+) (?<path>\S*) took (?<ms>\d+) ms", RegexOptions.Compiled);
        private static readonly Regex UserPart = new(@"user (?<user>[^;]+);", RegexOptions.Compiled);

        /// <summary>A page, with ids in the address made generic (/Employer/Dashboard/{id}) so they count together.</summary>
        public static string PageOf(string method, string path) =>
            method + " " + Regex.Replace(path, @"/\d+(?=/|$)", "/{id}");

        public async Task<IActionResult> Index()
        {
            var vm = new HealthViewModel();
            try
            {
                using var db = new SqlConnection(_connectionString);
                using var multi = await db.QueryMultipleAsync("dbo.sp_SystemHealth", commandType: CommandType.StoredProcedure, commandTimeout: 60);
                vm.Database = (await multi.ReadAsync<DatabaseInfo>()).FirstOrDefault();
                vm.Errors = (await multi.ReadAsync<ErrorCounts>()).FirstOrDefault() ?? new(0, 0);
                vm.FrequentErrors = (await multi.ReadAsync<FrequentError>()).ToList();
                var slow = (await multi.ReadAsync<(string Message, string? AdditionalInfo, DateTime LoggedOnDate)>()).ToList();
                vm.Emails = (await multi.ReadAsync<EmailStatus>()).ToList();
                vm.StuckFiles = (await multi.ReadAsync<StuckFile>()).ToList();

                vm.SlowRequests = slow.Count;
                vm.SlowPages = slow
                    .Select(s => (Match: SlowLine.Match(s.Message ?? ""), s.AdditionalInfo, s.LoggedOnDate))
                    .Where(s => s.Match.Success)
                    .GroupBy(s => PageOf(s.Match.Groups["method"].Value, s.Match.Groups["path"].Value))
                    .Select(g => new SlowPage(g.Key, g.Count(),
                        (int)g.Average(s => int.Parse(s.Match.Groups["ms"].Value)),
                        g.Max(s => int.Parse(s.Match.Groups["ms"].Value)),
                        g.Max(s => s.LoggedOnDate),
                        g.Select(s => UserPart.Match(s.AdditionalInfo ?? "").Groups["user"].Value).Where(u => u is not ("" or "-")).Distinct().Count()))
                    .OrderByDescending(p => p.Times).ThenByDescending(p => p.SlowestMs)
                    .Take(15)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "System Health: the database figures could not be read.");
                ViewBag.DatabaseError = "The database figures could not be read: " + ex.Message;
            }

            vm.Jobs = ReadJobs();
            try { vm.MaintenanceMode = await _settings.GetMaintenanceModeAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "System Health: maintenance mode could not be read."); }
            return View(vm);
        }

        // Background jobs, from Hangfire's own storage (the job server may run elsewhere).
        private JobInfo ReadJobs()
        {
            try
            {
                if (_services.GetService(typeof(JobStorage)) is not JobStorage storage)
                    return new JobInfo(false, 0, 0, 0, 0, 0, new());
                var monitor = storage.GetMonitoringApi();
                var stats = monitor.GetStatistics();
                using var connection = storage.GetConnection();
                var recurring = Hangfire.Storage.StorageConnectionExtensions.GetRecurringJobs(connection)
                    .Select(r => (r.Id, (string?)r.Cron, r.LastExecution, r.LastJobState, r.NextExecution))
                    .OrderBy(r => r.Id).ToList();
                return new JobInfo(true, stats.Failed, stats.Processing, stats.Scheduled, stats.Enqueued, (int)stats.Servers, recurring);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "System Health: the background jobs could not be read.");
                return new JobInfo(false, 0, 0, 0, 0, 0, new());
            }
        }
    }
}
