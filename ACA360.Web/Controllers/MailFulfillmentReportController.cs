using ACA360.Logging.Interfaces;
using ACA360.Repositories.Interfaces;
using Microsoft.AspNetCore.Mvc;
using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Constants;
using Microsoft.AspNetCore.Authorization;
namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.InternalTeam)]
    public class MailFulfillmentReportController : Controller
    {
        private readonly IReportService _reportService;
        private readonly ILoggerService _logger;
        private readonly IFilingYearService _filingyearservice;
        private readonly IEmployerService _employerService;

        public MailFulfillmentReportController(IReportService reportService, ILoggerService logger, IFilingYearService filingYearService, IEmployerService employerService)
        {
            _reportService = reportService;
            _logger = logger;
            _filingyearservice = filingYearService;
            _employerService = employerService;
        }
        /// <summary>
        /// Retrieves the assignment management view for a specific staff member based on their role.
        /// Fetches all available employers and the currently assigned employers to build the assignment UI.
        /// </summary>
        /// <param name="id">The unique identifier of the staff member.</param>
        /// <param name="roleId">The role identifier determining the type of staff (e.g., Data Analyst, Account Manager, Broker).</param>
        /// <returns>A partial view containing the assignment management UI and the populated data model.</returns>
        #region ExportMailFulfillment_Report

        public async Task<IActionResult> Index()
        {
            var FilingYear = await _filingyearservice.GetAllFilingYears();
            ViewBag.FilingYear = FilingYear;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExportMailFulfillment_GETDATA()
        {
            try
            {
                var form = Request.Form;
                string? FilingYear = form["FilingYear"];
                string? EmployerTaxId = form["EmployerTaxId"];

                if (string.IsNullOrEmpty(FilingYear) || string.IsNullOrEmpty(EmployerTaxId))
                {
                    return Json(new { data = new List<object>() });
                }

                var resultViewModel = await _reportService.GetExportMailFulfillmentData(EmployerTaxId, FilingYear);

                return Json(new
                {
                    // Send both sets of data
                    employerInfo = resultViewModel.Employers.FirstOrDefault(),
                    data = resultViewModel.Employees
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(ExportMailFulfillment_GETDATA), "ReportController", "Error");
                return Json(new { data = new List<object>() });
            }
        }
        #endregion
    }
}
