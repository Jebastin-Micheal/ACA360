using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Constants;
using ACA360.Logging.Interfaces;
using ACA360.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ACA360.Core.Models;

namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector + "," + UserRoles.DASupervisor + "," + UserRoles.DataAnalyst)]
    public class DataAnalystReportController : BaseController
    {
        private readonly IDataAnalystReportService _daReportService;
        private readonly ILoggerService _logger;
        private readonly IExportService _exportService;

        public DataAnalystReportController(IDataAnalystReportService daReportService, ILoggerService logger, IExportService exportService)
        {
            _daReportService = daReportService;
            _logger = logger;
            _exportService = exportService;
        }

        [HttpGet]
        public IActionResult Index()
        {
            try
            {
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Index), nameof(DataAnalystReportController), "Error loading the Data Analyst Report page.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading the page.");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetDataAnalysts()
        {
            try
            {
                var analysts = await _daReportService.GetDataAnalystsAsync();
                return Json(analysts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetDataAnalysts), nameof(DataAnalystReportController), "Error fetching Data Analysts list.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetFilingYears()
        {
            try
            {
                var years = await _daReportService.GetFilingYearsAsync();
                return Json(years);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetFilingYears), nameof(DataAnalystReportController), "Error fetching Filing Years list.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
            }
        }

        [HttpGet]
        public async Task<IActionResult> HasReportData(int dataAnalystId, int? filingYear)
        {
            try
            {
                if (dataAnalystId <= 0)
                    return Json(new { hasData = false });

                var data = await _daReportService.GetDataAnalystReportAsync(dataAnalystId, filingYear);
                return Json(new { hasData = data != null && data.Count > 0 });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(HasReportData), nameof(DataAnalystReportController), $"Error checking data for DataAnalystId: {dataAnalystId}");
                return Json(new { hasData = false });
            }
        }

        [HttpGet]
        public async Task<IActionResult> Download(int dataAnalystId, string dataAnalystName, int? filingYear, string format = "excel")
        {
            try
            {
                if (dataAnalystId <= 0)
                    return BadRequest("Invalid Data Analyst selected.");

                var data = await _daReportService.GetDataAnalystReportAsync(dataAnalystId, filingYear);
                int year = filingYear ?? DateTime.Now.Year;

                return HandleExport(
                    _exportService,
                    format,
                    title: $"Data Analyst Report for {dataAnalystName} ({year})",
                    fileName: $"DA_Report_for_{dataAnalystName}_{year}",
                    columns: new List<ExportColumn>
                    {
                        new ExportColumn("EmployerName",    "Employer Name"),
                        new ExportColumn("ServiceName",     "Service Name"),
                        new ExportColumn("AccountManager",  "Account Manager"),
                        new ExportColumn("DataAnalyst",     "Data Analyst"),
                        new ExportColumn("FilingYear",      "Filing Year"),
                        new ExportColumn("ProcessStep1095", "1095 Process Step"),
                        new ExportColumn("FollowUpDate",    "1095 Follow Up Date")
                    },
                    rows: data.Select(r => new Dictionary<string, object>
                    {
                        ["EmployerName"] = r.EmployerName ?? "",
                        ["ServiceName"] = r.ServiceName ?? "",
                        ["AccountManager"] = r.AccountManager ?? "",
                        ["DataAnalyst"] = r.DataAnalyst ?? "",
                        ["FilingYear"] = r.FilingYear?.ToString() ?? "",
                        ["ProcessStep1095"] = r.ProcessStep1095 ?? "",
                        ["FollowUpDate"] = r.FollowUpDate?.ToString("MM/dd/yyyy") ?? ""
                    }).ToList()
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Download), nameof(DataAnalystReportController), $"Error exporting for DataAnalystId: {dataAnalystId}");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while exporting the report.");
            }
        }
    }
}