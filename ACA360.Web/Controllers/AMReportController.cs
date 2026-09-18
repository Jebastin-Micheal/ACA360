using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Constants;
using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using ACA360.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector + "," + UserRoles.AMSupervisor + "," + UserRoles.AccountManager)]
    public class AMReportController : BaseController
    {
        private readonly IAmReportService _amReportService;
        private readonly ILoggerService _logger;
        private readonly IExportService _exportService;

        public AMReportController(IAmReportService amReportService, ILoggerService logger, IExportService exportService)
        {
            _amReportService = amReportService;
            _logger = logger;
            _exportService = exportService;
        }

        /// <summary>
        /// Renders the AM Report page (menu: Report -> AM Report).
        /// </summary>
        [HttpGet]
        public IActionResult Index()
        {
            try
            {
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Index), nameof(AMReportController), "Error loading the AM Report page.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading the AM Report page.");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetFilingYears()
        {
            var years = await _amReportService.GetFilingYearsAsync();
            return Json(years);
        }

        /// <summary>
        /// Lightweight check to see if the selected Account Manager has any report data
        /// before triggering the actual Excel download.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> HasReportData(int acctManagerId, int? filingYear)
        {
            try
            {
                if (acctManagerId <= 0)
                {
                    return Json(new { hasData = false });
                }

                var data = await _amReportService.GetAmReportAsync(acctManagerId, filingYear);
                return Json(new { hasData = data != null && data.Count > 0 });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(HasReportData), nameof(AMReportController), $"Error checking AM Report data for AcctManagerId: {acctManagerId}");
                return Json(new { hasData = false });
            }
        }

        /// <summary>
        /// Cascading dropdown API: loads the list of Account Managers for the modal.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAccountManagers()
        {
            try
            {
                var managers = await _amReportService.GetAccountManagersAsync();
                return Json(managers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetAccountManagers), nameof(AMReportController), "Error fetching Account Managers list.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while fetching Account Managers.");
            }
        }

        /// <summary>
        /// Generates and exports the AM Report as a downloadable Excel document
        /// using the common export service (same as ExportRules).
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Download(int acctManagerId, string acctManagerName, int? filingYear, string format = "excel")
        {
            try
            {
                if (acctManagerId <= 0)
                {
                    return BadRequest("Invalid Account Manager selected.");
                }

                var data = await _amReportService.GetAmReportAsync(acctManagerId, filingYear);

                return HandleExport(
                    _exportService,
                    format,
                    title: $"AM Report for {acctManagerName}",
                    fileName: $"AM_Report_for_{acctManagerName}",
                    columns: new List<ExportColumn>
                    {
                new ExportColumn("EmployerName",     "Employer Name"),
                new ExportColumn("ServiceName",      "Service Name"),
                new ExportColumn("AccountManager",   "Account Manager"),
                new ExportColumn("DataAnalyst",      "Data Analyst"),
                new ExportColumn("FilingYear",       "Filing Year"),
                new ExportColumn("ProcessStep1095",  "1095 Process Step"),
                new ExportColumn("FollowUpDate",     "1095 Follow Up Date")
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
                _logger.LogError(ex, nameof(Download), nameof(AMReportController), $"Error exporting AM Report for AcctManagerId: {acctManagerId}");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while exporting the AM Report.");
            }
        }
    }
}