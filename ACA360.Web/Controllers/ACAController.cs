using ACA360.BusinessLogic.Interfaces;
using ACA360.BusinessLogic.Services;
using ACA360.Core.Attributes; // For AuthorizePermission
using ACA360.Core.Constants;
using ACA360.Core.Models;
using ACA360.Core.ViewModels;
using ACA360.Repositories;
using ACA360.Web.Helpers;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Threading.Tasks;
using ACA360.Web.Filters;

namespace ACA360.Web.Controllers
{
    /// <summary>
    /// Handles all ACA (Affordable Care Act) related operations including
    /// code review, form generation, penalty risk assessment, and audit timelines.
    /// Access is restricted to authorized roles defined at the controller level.
    /// </summary>
    [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector + "," +
                       UserRoles.DASupervisor + "," + UserRoles.DASupervisor + "," + UserRoles.AMSupervisor + "," +
                       UserRoles.Broker + "," + UserRoles.DataAnalyst + "," + UserRoles.Employer + "," +
                       UserRoles.StaffAC + "," + UserRoles.StaffAU + "," + UserRoles.AccountManager)]
    public class ACAController : BaseController
    {
        private readonly IACALogicService _acaLogicService;
        private readonly IEmployerService _employerService;
        private readonly IPdfService _pdfService;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<ACAController> _logger;
        private readonly string _connectionString;
        private readonly IConfiguration _configuration;
        private readonly IAzureBlobService _azureBlobService;

        /// <summary>
        /// Initializes the ACAController with all required service dependencies.
        /// </summary>
        public ACAController(
IACALogicService acaLogicService,
IEmployerService employerService,
IPdfService pdfService,
IWebHostEnvironment environment,
ILogger<ACAController> logger,
IConfiguration configuration,
IAzureBlobService azureBlobService)
        {
            _acaLogicService = acaLogicService;
            _employerService = employerService;
            _pdfService = pdfService;
            _environment = environment;
            _logger = logger;
            _configuration = configuration;
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _azureBlobService = azureBlobService;
        }

        // ─────────────────────────────────────────────────────────────────────
        // 1. CODE REVIEW  (Data Analyst & Account Manager)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Renders the Code Review page for a given employer and tax year.
        /// Populates ViewBag with employer details and the valid IRS line-14 / line-16
        /// offer-of-coverage code sets used by the dropdown menus on the view.
        /// Restricted to users with the "View" permission.
        /// </summary>
        [HttpGet]
        [AuthorizePermission("View")]
        [RequireEmployerScope]
        public async Task<IActionResult> CodeReview(int employerId, int year)
        {
            try
            {
                _logger.LogInformation("CodeReview requested for EmployerId={EmployerId}, Year={Year}.", employerId, year);

                // Validate that the employer exists before proceeding
                var employer = await _employerService.GetEmployerDetailsByIdAsync(employerId.ToString());
                if (employer == null)
                {
                    _logger.LogWarning("CodeReview: Employer not found for EmployerId={EmployerId}.", employerId);
                    return NotFound();
                }

                // Pass employer context and year to the view via ViewBag
                ViewBag.EmployerName = employer.Name;
                ViewBag.EmployerId = employerId;
                ViewBag.Year = year;

                // Populate IRS-defined code dropdown lists for Line 14 and Line 16
                ViewBag.Line14Codes = new List<string>
                {
                    "1A", "1B", "1C", "1D", "1E", "1F", "1G", "1H",
                    "1J", "1K", "1L", "1M", "1N", "1O", "1P", "1Q",
                    "1R", "1S", "1T", "1U"
                };
                ViewBag.Line16Codes = new List<string>
                {
                    "2A", "2B", "2C", "2D", "2E", "2F", "2G", "2H"
                };

                return View();
            }
            catch (Exception ex)
            {
                // Log unexpected errors and return a 500 so the caller gets a clear signal
                _logger.LogError(ex, "Unhandled error in CodeReview for EmployerId={EmployerId}, Year={Year}.", employerId, year);
                return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while loading the Code Review page.");
            }
        }

        /// <summary>
        /// AJAX endpoint that returns the 1095-C line-14 / line-16 code assignments
        /// for every employee of the given employer for the specified tax year.
        /// The response includes FirstName, LastName, and SSN for each record.
        /// </summary>
        [HttpGet]
        [RequireEmployerScope]
        public async Task<IActionResult> GetCodeReviewData(int employerId, int year)
        {
            try
            {
                _logger.LogInformation("GetCodeReviewData requested for EmployerId={EmployerId}, Year={Year}.", employerId, year);

                // Fetch the full list of employee code assignments from the service layer
                var codes = await _acaLogicService.GetEmployeeCodesListAsync(employerId, year);

                // Wrap in a "data" envelope for DataTables / grid compatibility
                return Json(new { data = codes });
            }
            catch (Exception ex)
            {
                // Return 500 JSON so the front-end grid can surface a user-friendly error
                _logger.LogError(ex, "Error retrieving code review data for EmployerId={EmployerId}, Year={Year}.", employerId, year);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { success = false, message = "An error occurred while retrieving code review data." });
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // 2. FORM GENERATION  (Account Manager only)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Renders the Form Generation page, which allows Account Managers,
        /// Super Admins, and Admins to trigger and download employer ACA filings.
        /// </summary>
        [HttpGet]
        [Authorize(Roles = "Account Manager, SuperAdmin, Admin")]
        [RequireEmployerScope]
        public async Task<IActionResult> FormGeneration(int employerId, int year)
        {
            try
            {
                _logger.LogInformation("FormGeneration page requested for EmployerId={EmployerId}, Year={Year}.", employerId, year);

                // Fetch employer details to display the employer name in the page header
                var employer = await _employerService.GetEmployerDetailsByIdAsync(employerId.ToString());
                if (employer == null)
                {
                    _logger.LogWarning("FormGeneration: Employer not found for EmployerId={EmployerId}.", employerId);
                    return NotFound();
                }

                ViewBag.EmployerName = employer.Name;
                ViewBag.EmployerId = employerId;
                ViewBag.Year = year;

                return View();
            }
            catch (Exception ex)
            {
                // Unexpected error loading the form generation page
                _logger.LogError(ex, "Error loading FormGeneration for EmployerId={EmployerId}, Year={Year}.", employerId, year);
                return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while loading the Form Generation page.");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // 3. PENALTY RISK DASHBOARD
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Renders the Penalty Risk dashboard for an employer and tax year.
        /// Displays Type A (4980H(a)) and Type B (4980H(b)) ACA penalty exposure,
        /// including per-employee detail rows sorted by estimated penalty descending.
        /// </summary>
        [HttpGet]
        [RequireEmployerScope]
        public async Task<IActionResult> PenaltyRisk(int employerId, int year)
        {
            try
            {
                _logger.LogInformation("PenaltyRisk page requested for EmployerId={EmployerId}, Year={Year}.", employerId, year);

                // Step 1: Retrieve pre-calculated penalty risk figures from the data store
                var riskData = await _acaLogicService.GetPenaltyRiskAsync(employerId, year);

                // Step 2: Fetch the employer name for the page header
                var employerName = await _employerService.GetEmployerNameAsync(employerId);

                // Step 3: Map the raw service result to the strongly-typed view model,
                //         splitting into Type A and Type B risk detail collections
                var model = new PenaltyDashboardViewModel
                {
                    EmployerId = employerId,
                    EmployerName = employerName,
                    TaxYear = year,

                    // Type A (4980H(a)) – failure to offer MEC to ≥95 % of full-time employees
                    TypeA_Triggered = riskData.TypeA_Triggered,
                    TotalFullTimeEmployees = riskData.TotalFullTimeEmployees,
                    ReliefCount = riskData.ReliefCount,
                    TypeA_MonthlyRate = riskData.TypeA_MonthlyRate,
                    TypeA_TotalExposure = riskData.TypeA_TotalExposure,
                    TypeA_ApplicableMonths=riskData.TypeA_ApplicableMonths,
                    // Type B (4980H(b)) – offer made but coverage was unaffordable / not MV
                    TypeB_TotalViolations = riskData.TypeB_TotalViolations,
                    TypeB_MonthlyRate = riskData.TypeB_MonthlyRate,
                    TypeB_TotalExposure = riskData.TypeB_TotalExposure,

                    // Type B detail rows – sorted by highest estimated penalty first for triage
                    RiskDetails = riskData.AtRiskEmployees
                        .Select(e => new PenaltyDetailRow
                        {
                            EmployeeId = e.EmployeeId,
                            EmployeeName = e.FullName,
                            SSN = e.SSN,
                            BadMonthCount = e.BadMonthCount,
                            BadMonthsList = e.BadMonthsList, // e.g. "Jan, Mar, Dec"
                            EstimatedPenalty = e.EstimatedPenalty
                        })
                        .OrderByDescending(x => x.EstimatedPenalty)
                        .ToList(),

                    // Type A detail rows – employees missing a 1H (no-offer) code
                    TypeA_RiskDetails = riskData.TypeA_AtRiskEmployees
                        .Select(e => new PenaltyDetailRow
                        {
                            EmployeeId = e.EmployeeId,
                            EmployeeName = e.FullName,
                            SSN = e.SSN,
                            BadMonthCount = e.BadMonthCount,
                            BadMonthsList = "No Offer (1H) Found",
                            EstimatedPenalty = 0
                        })
                        .ToList()
                };

                return View(model);
            }
            catch (Exception ex)
            {
                // Log the full exception so developers can trace data mapping failures
                _logger.LogError(ex, "Error loading PenaltyRisk dashboard for EmployerId={EmployerId}, Year={Year}.", employerId, year);
                return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while loading the Penalty Risk dashboard.");
            }
        }

        /// <summary>
        /// Triggers a full two-step recalculation of ACA penalty risk for the employer:
        ///   1. Refreshes compliance flags (incorporates any manual code corrections).
        ///   2. Recalculates financial penalty exposure based on the updated flags.
        /// Redirects back to the Penalty Risk dashboard on completion.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireEmployerScope]
        public async Task<IActionResult> RecalculateRisk(int employerId, int year)
        {
            try
            {
                _logger.LogInformation("RecalculateRisk triggered for EmployerId={EmployerId}, Year={Year}.", employerId, year);

                // Step 1: Refresh compliance flags – this picks up any manually locked code changes
                await _acaLogicService.RecalculateFlagsAsync(employerId, year);

                // Step 2: Recompute the dollar-value penalty exposure based on the refreshed flags
                await _acaLogicService.CalculatePenaltyRiskAsync(employerId, year);

                // Step 3: Redirect to the updated dashboard
                return RedirectToAction("PenaltyRisk", new { employerId, year });
            }
            catch (Exception ex)
            {
                // A failure here leaves penalty figures stale; surface a clear 500
                _logger.LogError(ex, "Error during RecalculateRisk for EmployerId={EmployerId}, Year={Year}.", employerId, year);
                return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while recalculating penalty risk.");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // 4. FORM DOWNLOAD
        // ─────────────────────────────────────────────────────────────────────

        //[HttpPost]
        //[Authorize(Roles = "Account Manager, SuperAdmin, Admin")]
        //[ValidateAntiForgeryToken]
        //public async Task<IActionResult> GenerateForms(int employerId, int year)
        //{
        //    try
        //    {
        //        // 1. Generate the File
        //        byte[] fileBytes = await _pdfService.GenerateEmployerBatchAsync(employerId, year);

        //        // 2. Define a Secure Path (Not accessible via public URL)
        //        // We create a 'GeneratedReports' folder in the app root
        //        string folderPath = Path.Combine(_environment.ContentRootPath, "GeneratedReports");
        //        if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

        //        // 3. Save File with Deterministic Name
        //        string fileName = $"Filing_{year}_{employerId}.zip";
        //        string fullPath = Path.Combine(folderPath, fileName);

        //        await System.IO.File.WriteAllBytesAsync(fullPath, fileBytes);

        //        return Json(new { success = true, message = "Forms generated and saved successfully." });
        //    }
        //    catch (System.Exception ex)
        //    {
        //        return Json(new { success = false, message = "Error: " + ex.Message });
        //    }
        //}

        /// <summary>
        /// Streams a previously generated ACA filing ZIP archive to the browser.
        /// The file is identified by a deterministic name derived from the employer ID
        /// and tax year. Returns 404 if the file has not yet been generated.
        /// </summary>
        [HttpGet]
        [RequireEmployerScope]
        public async Task<IActionResult> DownloadForms(int employerId, int year)
        {
            try
            {
                _logger.LogInformation("DownloadForms requested for EmployerId={EmployerId}, Year={Year}.", employerId, year);

                // 1. Look up the actual generated file name from batch history.
                using var db = new SqlConnection(_connectionString);

                var fileName = await db.QueryFirstOrDefaultAsync<string>(
                    @"SELECT TOP 1 h.FileName
      FROM GenerateMultiEmployerBatchHistoryEmployers e
      JOIN GenerateMultiEmployerBatchHistory h ON e.BatchHistoryId = h.Id
      WHERE e.EmployerId = @EmployerId AND e.TaxYear = @Year
      ORDER BY h.GeneratedDate DESC",
                    new { EmployerId = employerId, Year = year });

                if (fileName == null)
                {
                    _logger.LogWarning("DownloadForms: No batch history found for EmployerId={EmployerId}, Year={Year}.", employerId, year);
                    return NotFound(new { success = false, message = "No generated batch found for this employer/year." });
                }

                // 2. Use the existing, already-working blob service — no raw
                //    BlobServiceClient, no connection string handling here at all.
                string generatedReportsFolder = _configuration["AzureBlob:GeneratedReports"] ?? "GeneratedReports";

                bool exists = await _azureBlobService.FileExistsAsync(generatedReportsFolder, fileName);
                if (!exists)
                {
                    _logger.LogWarning("DownloadForms: DB has a record but blob is missing. Folder={Folder}, FileName={FileName}.", generatedReportsFolder, fileName);
                    return NotFound(new { success = false, message = "File not found on server." });
                }

                var bytes = await _azureBlobService.DownloadFileAsync(generatedReportsFolder, fileName);

                if (bytes == null)
                {
                    // Exists check passed but download returned null — treat as
                    // a transient/storage-layer issue rather than "not found".
                    _logger.LogError("DownloadForms: FileExistsAsync returned true but DownloadFileAsync returned null. Folder={Folder}, FileName={FileName}.", generatedReportsFolder, fileName);
                    return StatusCode(StatusCodes.Status500InternalServerError,
                        new { success = false, message = "File could not be retrieved. Please try again." });
                }

                return File(bytes, "application/zip", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during DownloadForms for EmployerId={EmployerId}, Year={Year}.", employerId, year);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { success = false, message = "An error occurred while downloading the forms." });
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // 5. CODE GENERATION ENGINE
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Triggers the 1095-C code calculation engine for all employees of the
        /// given employer for the specified tax year ("Time Machine" logic).
        /// Restricted to users with the "Edit" permission.
        /// Redirects to the Employer Dashboard on completion (success or failure).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizePermission("Edit")]
        [RequireEmployerScope]
        public async Task<IActionResult> GenerateCodes(int employerId, int year)
        {
            try
            {
                _logger.LogInformation("GenerateCodes triggered for EmployerId={EmployerId}, Year={Year}.", employerId, year);

                // Run the "Time Machine" calculation engine for every employee;
                // this derives Line 14, Line 15, and Line 16 codes from enrollment data
                await _acaLogicService.GenerateCodesForEmployerAsync(employerId, year);

                TempData["Success"] = "1095-C Codes have been successfully calculated for all employees.";
            }
            catch (Exception ex)
            {
                // Surface the error to the user via TempData while logging the full detail
                _logger.LogError(ex, "Error during GenerateCodes for EmployerId={EmployerId}, Year={Year}.", employerId, year);
                TempData["Error"] = "Error generating codes: " + ex.Message;
            }

            // Always redirect back to the Employer Dashboard regardless of outcome
            return RedirectToAction("Dashboard", "Employer", new { id = employerId, year });
        }

        // ─────────────────────────────────────────────────────────────────────
        // 6. MANUAL CODE SAVE
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Persists a manually reviewed and corrected set of 1095-C codes for a
        /// single employee record. Setting IsLocked = true prevents the automation
        /// engine from overwriting this record during future GenerateCodes runs.
        /// Restricted to users with the "Edit" permission.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizePermission("Edit")]
        public async Task<IActionResult> SaveManualCodes(EmployeeCode model)
        {
            try
            {
                // Validate that the submitted model contains the required fields
                if (!ModelState.IsValid)
                {
                    _logger.LogWarning("SaveManualCodes: Invalid model state for EmployeeCode.");
                    return BadRequest(new { success = false, message = "Invalid data submitted." });
                }

                _logger.LogInformation("SaveManualCodes called for EmployeeId={EmployeeId}.", model.EmployeeId);

                // Lock the record to prevent future automated overwrites
                //model.IsLocked = true;

                // Capture the current user for audit trail purposes
                string userId = GetCurrentUserId();

                // Persist the manually corrected codes via the service layer
                await _acaLogicService.UpdateManualCodesAsync(model, userId);

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                // Return a JSON 500 so the front-end inline editor can show a failure message
                _logger.LogError(ex, "Error in SaveManualCodes for EmployeeId={EmployeeId}.", model?.EmployeeId);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { success = false, message = "An error occurred while saving manual codes." });
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // 7. SAFE HARBOR APPLICATION
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Applies a selected IRS safe harbor code to all eligible months for a
        /// given employee in the current filing year. Safe harbors (e.g., W-2,
        /// FPL, Rate of Pay) can reduce or eliminate Type B penalty exposure.
        /// </summary>
        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApplySafeHarbor(int employeeId, string safeHarborCode, bool isLocked)
        {
            try
            {
                _logger.LogInformation("ApplySafeHarbor called for EmployeeId={EmployeeId}, SafeHarborCode={SafeHarborCode},IsLocked={IsLocked}..",
                    employeeId, safeHarborCode, isLocked);

                // Validate that a safe harbor code was actually supplied
                if (string.IsNullOrWhiteSpace(safeHarborCode))
                {
                    _logger.LogWarning("ApplySafeHarbor: SafeHarborCode is null or empty for EmployeeId={EmployeeId}.", employeeId);
                    return BadRequest(new { success = false, message = "Safe harbor code is required." });
                }

                // Resolve the current filing year and the acting user for audit purposes
                string fillingYear = GetCurrentFilingYear();
                string userId = GetCurrentUserId();

                // Apply the safe harbor code via the service layer
                await _acaLogicService.ApplySafeHarborAsync(employeeId, fillingYear, safeHarborCode, isLocked, userId);

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                // A failure here means the safe harbor was not recorded; return 500 JSON
                _logger.LogError(ex, "Error in ApplySafeHarbor for EmployeeId={EmployeeId}, SafeHarborCode={SafeHarborCode}.",
                    employeeId, safeHarborCode);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { success = false, message = "An error occurred while applying the safe harbor code." });
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // 8. AUDIT TIMELINE
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns a chronological audit timeline for a specific employee and tax year,
        /// showing all code changes, manual overrides, and safe harbor applications.
        /// Used to power the audit history drawer / modal on the Code Review page.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetEmployeeAuditTimeline(int employeeId, int year)
        {
            try
            {
                _logger.LogInformation("GetEmployeeAuditTimeline requested for EmployeeId={EmployeeId}, Year={Year}.",
                    employeeId, year);

                // Retrieve the ordered list of audit events from the service layer
                var data = await _acaLogicService.GetAuditTimelineAsync(employeeId, year);

                return Json(data);
            }
            catch (Exception ex)
            {
                // Return 500 JSON so the front-end timeline component can display an error state
                _logger.LogError(ex, "Error retrieving audit timeline for EmployeeId={EmployeeId}, Year={Year}.",
                    employeeId, year);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { success = false, message = "An error occurred while retrieving the audit timeline." });
            }
        }
    }
}
