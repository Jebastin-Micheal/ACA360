using ACA360.Core.Constants;
using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using ACA360.Repositories.Interfaces;
using ACA360.Repositories;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using ACA360.BusinessLogic.Interfaces;
using ACA360.Web.Helpers;
using ACA360.Web.Filters;

namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector + "," + UserRoles.DASupervisor + "," + UserRoles.DASupervisor + "," + UserRoles.AMSupervisor + "," + UserRoles.Broker + "," + UserRoles.DataAnalyst + "," + UserRoles.Employer + "," + UserRoles.StaffAC + "," + UserRoles.StaffAU + "," + UserRoles.AccountManager)]
    public class FormGenerationController : BaseController
    {
        private readonly IFormGenerationService _genService;
        private readonly IBackgroundJobClient _backgroundJob;
        private readonly IPdfService _pdfService;
        private readonly IACALogicService _acaLogicService;
        private readonly IFilingYearService _filingyearservice;
        private readonly ILoggerService _logger;
        private readonly IEmployeeService _employeeService;

        public FormGenerationController(
            IFormGenerationService genService,
            IBackgroundJobClient backgroundJob,
            IEmployeeService employeeService,
            IPdfService pdfService,
            IFilingYearService filingYearService,
            IACALogicService acaLogicService,
            ILoggerService logger)
        {
            _genService = genService;
            _backgroundJob = backgroundJob;
            _pdfService = pdfService;
            _acaLogicService = acaLogicService;
            _filingyearservice = filingYearService;
            _logger = logger;
            _employeeService = employeeService;
        }

        [HttpGet]
        public async Task<IActionResult> Index(
            int year = 0,
            string filterStatus = "All",
            string sortColumn = "Name",
            string sortOrder = "Asc",
            string searchTerm = "")
        {
            try
            {
                if (year == 0) year = Convert.ToInt32(GetCurrentFilingYear());

                var userId = GetCurrentUserId();
                var role = User.FindFirstValue(ClaimTypes.Role);

                var data = await _genService.GetGenerationDashboardAsync(userId, role, year, filterStatus, sortColumn, sortOrder, searchTerm);

                var model = new FormGenerationDashboardViewModel
                {
                    SelectedYear = year,
                    FilterStatus = filterStatus,
                    SortColumn = sortColumn,
                    SortOrder = sortOrder,
                    SearchTerm = searchTerm,
                    Employers = data
                };

                var years = await _filingyearservice.GetAllFilingYears();
                ViewBag.Years = years.Select(y => new SelectListItem { Text = y.FilingYear.ToString(), Value = y.FilingYear.ToString() }).ToList();

                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Index), "FormGenerationController", "Error occurred while loading the Form Generation Dashboard");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading the dashboard.");
            }
        }

        [HttpGet]
        public async Task<IActionResult> SearchEmployers(int year, string filterStatus, string searchTerm)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var role = User.FindFirstValue(ClaimTypes.Role);

                var data = await _genService.GetGenerationDashboardAsync(
                    userId, role, year, filterStatus, "Name", "Asc", searchTerm);

                return PartialView("_EmployerCardsPartial", data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(SearchEmployers), "FormGenerationController", $"Error occurred while searching employers. Term: {searchTerm}");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while searching employers.");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult StartBatchGeneration([FromBody] BatchProcessingRequest request)
        {
            try
            {
                if (request == null || request.EmployerIds == null || !request.EmployerIds.Any())
                {
                    return BadRequest(new { success = false, message = "Invalid request or missing Employer IDs." });
                }

                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                foreach (var empId in request.EmployerIds)
                {
                    _backgroundJob.Enqueue<IPdfService>(service =>
                        service.ProcessBatchAndNotifyAsync(empId, request.Year, userId, request.FormType));
                }

                return Json(new { success = true, message = "Jobs queued! You will be notified when ready." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(StartBatchGeneration), "FormGenerationController", "Error occurred while starting batch generation");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred while queuing jobs." });
            }
        }

        public async Task<IActionResult> GetEmployeeList([FromQuery] EmployeeFilterRequest filter, [FromQuery] List<int> employerId, string year)
        {
            try
            {
                var allEmployees = new List<dynamic>();

                if (employerId != null && employerId.Any())
                {
                    if (filter == null) filter = new EmployeeFilterRequest();

                    filter.PageIndex = 1;
                    filter.PageSize = 1000;
                    filter.FilingYear = year;

                   
                    var paginationEntity = new PaginationEntity
                    {
                        PageIndex = filter.PageIndex,
                        PageSize = filter.PageSize,
                        Search = "",
                        SortColumn = 0,
                        SortOrder = "asc",
                        fillingYear = year
                    };

                    
                    var claim = User.FindFirstValue("UserId");
                    int? currentUserId = int.TryParse(claim, out int parsedId) ? parsedId : null;

                    foreach (var id in employerId)
                    {
                        
                        var employerList = await _employeeService.GetAll_Employer_by_employer_Async(id);
                        var empName = employerList.FirstOrDefault()?.Name ?? $"Employer {id}";

                        
                        filter.Employer_IDs = new List<long> { id };

                        
                        var (records, _) = await _employeeService.GetEmployeesAsync(
                            id.ToString(),
                            year,
                            paginationEntity,
                            currentUserId,
                            filter);

                        if (records != null && records.Any())
                        {
                            
                            var empIds = records.Where(e => e.Id.HasValue).Select(e => e.Id.Value).ToList();

                            
                            var cityZipData = await _genService.GetEmployeeCityAndZipAsync(empIds);

                            
                            var mappedEmployees = records.Where(e => e.Id.HasValue).Select(emp => {

                                
                                var (cityVal, zipVal) = cityZipData.ContainsKey(emp.Id.Value) ? cityZipData[emp.Id.Value] : ("", "");

                                return new
                                {
                                    id = emp.Id.Value,
                                    firstName = emp.FirstName,
                                    lastName = emp.LastName,
                                    ssn = emp.SSN,

                                   
                                    city = string.IsNullOrWhiteSpace(cityVal) ? "" : cityVal,
                                    zip = string.IsNullOrWhiteSpace(zipVal) ? "" : zipVal,

                                    employerName = empName
                                };
                            });

                            allEmployees.AddRange(mappedEmployees);
                        }
                    }
                }

                return Json(allEmployees);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetEmployeeList), "FormGenerationController", "Error occurred while retrieving employee list");
                return StatusCode(StatusCodes.Status500InternalServerError, "Failed to retrieve employee list.");
            }
        }

        [HttpGet]
        [RequireEmployerScope]
        public async Task<IActionResult> GeneratePreview(int employerId, int year, string formType, int? selectedEmployeeId, bool suppressSSN = false)
        {
            try
            {
                // ==========================================================
                //  Allow PDF to load inside modal iframe
                // ==========================================================
                Response.Headers.Remove("X-Frame-Options");
                Response.Headers.Append("X-Frame-Options", "SAMEORIGIN");

                byte[] pdfBytes;
                string fileName;

                // ==========================================================
                // 1. HANDLE 1094 (EMPLOYER) FORMS FIRST (NO EMPLOYEE NEEDED)
                // ==========================================================
                if (formType == "1094-C" || formType == "1094-B")
                {
                    if (formType == "1094-C")
                    {
                        pdfBytes = await _pdfService.Generate1094CForEmployerAsync(employerId, year);
                        fileName = $"Preview_1094C_Employer_{employerId}.pdf";
                    }
                    else
                    {
                        pdfBytes = await _pdfService.Generate1094BForEmployerAsync(employerId, year);
                        fileName = $"Preview_1094B_Employer_{employerId}.pdf";
                    }

                    Response.Headers.Append("Content-Disposition", new System.Net.Mime.ContentDisposition
                    {
                        FileName = fileName,
                        Inline = true
                    }.ToString());

                    return File(pdfBytes, "application/pdf");
                }

                // ==========================================================
                // 2. HANDLE 1095 (EMPLOYEE) FORMS
                // ==========================================================
                int targetEmployeeId = 0;
                string empLastName = "Preview";

                if (selectedEmployeeId.HasValue && selectedEmployeeId.Value > 0)
                {
                    targetEmployeeId = selectedEmployeeId.Value;
                    empLastName = "Employee_" + targetEmployeeId;
                }
                else
                {
                    // Fetch a sample employee ONLY for 1095 forms
                    var codes = await _acaLogicService.GetEmployeeCodesListAsync(employerId, year);
                    var sample = codes.FirstOrDefault();
                    if (sample == null) return NotFound("No calculated codes found for preview.");

                    targetEmployeeId = sample.EmployeeId;
                    empLastName = sample.LastName;
                }

                // Changed '==' to '.Contains()' to support combined options like "1094-C_1095-C"
                if (formType.Contains("-C"))
                {
                    pdfBytes = await _pdfService.Generate1095CForEmployeeAsync(targetEmployeeId, year, suppressSSN); // Added parameter
                    fileName = $"Preview_1095C_{empLastName}.pdf";
                }
                else if (formType.Contains("-B"))
                {
                    pdfBytes = await _pdfService.Generate1095BForEmployeeAsync(targetEmployeeId, year, suppressSSN); // Added parameter
                    fileName = $"Preview_1095B_{empLastName}.pdf";
                }
                else
                {
                    return BadRequest("Invalid Form Type Selected");
                }

                Response.Headers.Append("Content-Disposition", new System.Net.Mime.ContentDisposition
                {
                    FileName = fileName,
                    Inline = true
                }.ToString());

                return File(pdfBytes, "application/pdf");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GeneratePreview), "FormGenerationController", $"Preview generation failed for employer {employerId}, form type {formType}");
                return StatusCode(StatusCodes.Status500InternalServerError, $"An unexpected error occurred generating preview.");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetRiskAnalysis(int employerId, int year)
        {
            try
            {
                var data = await _acaLogicService.GetPenaltyRiskAsync(employerId, year);
                return Json(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetRiskAnalysis), "FormGenerationController", $"Error fetching risk analysis for employer {employerId}, year {year}");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "Failed to load risk data." });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireEmployerScope]
        public async Task<IActionResult> AutoFixPlanStart(int employerId, int year, string month)
        {
            try
            {
                int count = await _acaLogicService.AutoFixPlanStartMonthAsync(employerId, year, month);
                return Json(new { success = true, message = $"Fixed {count} records successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(AutoFixPlanStart), "FormGenerationController", $"AutoFix failed for employer {employerId}, year {year}, month {month}");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred during AutoFix." });
            }
        }

        [HttpGet]
        [RequireEmployerScope]
        public async Task<IActionResult> GetRiskDetails(int employerId, int year)
        {
            try
            {
                var details = await _acaLogicService.GetAtRiskEmployeesAsync(employerId, year);
                return Json(new { success = true, data = details });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetRiskDetails), "FormGenerationController", $"Error fetching risk details for employer {employerId}, year {year}");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "Failed to fetch risk details." });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Consumes("application/json")]
        public IActionResult GenerateSpecificBatch([FromBody] MultiBatchRequest model)
        {
            try
            {
                if (model == null || model.EmployerIds == null || !model.EmployerIds.Any())
                {
                    return BadRequest(new { success = false, message = "Invalid Request: Missing employer details." });
                }

                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);


                _backgroundJob.Enqueue<IPdfService>(x => x.GenerateMultiEmployerBatchAsync(
                    model.EmployerIds,
                    model.EmployeeIds,
                    model.Year,
                    userId,
                    model.FormType,
                    model.SuppressSSN));

                return Json(new { success = true, message = "Multi-Employer batch queued for Single Zip!" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GenerateSpecificBatch), "FormGenerationController", "Error queueing specific batch generation");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred while queueing the batch job." });
            }
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerateBatchPreview([FromBody] MultiBatchRequest model)
        {
            try
            {
                if (model == null || model.EmployerIds == null || !model.EmployerIds.Any())
                {
                    return BadRequest("Invalid Request: Missing employer details.");
                }

                var pdfBytes = await _pdfService.GenerateEmployeesPreviewAsync(
                    model.EmployeeIds,
                    model.EmployerIds,
                    model.Year,
                    model.FormType,
                    model.SuppressSSN);

                Response.Headers.Append("Content-Disposition", new System.Net.Mime.ContentDisposition
                {
                    Inline = true,
                    FileName = "BatchPreview.pdf"
                }.ToString());

                return File(pdfBytes, "application/pdf");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GenerateBatchPreview), "FormGenerationController", "Error generating batch preview");
                return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred generating batch preview.");
            }
        }

        private async Task<string> DetermineFormTypeForEmployer(int employerId, int year)
        {
            return await Task.FromResult("1095-C");
        }
    }
}
