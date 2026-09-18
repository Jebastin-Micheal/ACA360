using ACA360.BusinessLogic.Interfaces;
using ACA360.BusinessLogic.Services;
using ACA360.Core.Constants;
using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using ACA360.Repositories.Interfaces;
using ACA360.Web.Helpers;
using ACA360.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.InternalTeam)]
    public class RulesController : BaseController
    {
        private readonly IRuleService _ruleService;
        private readonly ILoggerService _logger;
        private readonly IExportService _exportService;

        public RulesController(IRuleService ruleService, ILoggerService logger, IExportService exportService)
        {
            _ruleService = ruleService;
            _logger = logger;
            _exportService = exportService;
        }

        /// <summary>
        /// Retrieves the main index view for the Validation Rules section.
        /// Loads a paginated and sortable list of all active validation rules.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Index(int pageIndex = 1, int PageSize = 10, string search = "", int sortColumn = 0, string sortOrder = "asc")
        {
            try
            {
                // Construct pagination entity to filter and sort database records
                PaginationEntity paginationEntity = new PaginationEntity
                {
                    PageIndex = pageIndex,
                    PageSize = PageSize,
                    Search = search,
                    SortColumn = sortColumn,
                    SortOrder = sortOrder
                };

                // Fetch rules and metadata from the service layer
                var (rules, metadata) = await _ruleService.GetRuleList(paginationEntity);

                // Build the view model to bind data to the view
                var viewModel = new RulesViewModel
                {
                    Rules = rules,
                    Metadata = metadata,
                    Search = search,
                    SortColumn = sortColumn,
                    SortOrder = sortOrder
                };

                return View(viewModel);
            }
            catch (Exception ex)
            {
                // Log the exception securely
                _logger.LogError(ex, nameof(Index), nameof(RulesController), $"Error loading Rules list for pageIndex: {pageIndex}, search: {search}", HttpContext.Connection.RemoteIpAddress?.ToString());

                // Return generic 500 error page or status
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading the rules index.");
            }
        }

        /// <summary>
        /// Retrieves a partial view of the validation rules list for AJAX datatable updates.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> ValidationRuleListPartial(int pageIndex, string search, int sortColumn, string sortOrder, int PageSize)
        {
            try
            {
                // Prepare pagination details based on AJAX request
                PaginationEntity paginationEntity = new PaginationEntity
                {
                    PageIndex = pageIndex,
                    PageSize = PageSize,
                    Search = search,
                    SortColumn = sortColumn,
                    SortOrder = sortOrder
                };

                // Retrieve filtered list of rules
                var (rules, metadata) = await _ruleService.GetRuleList(paginationEntity);

                // Construct view model for the partial rendering
                var viewModel = new RulesViewModel
                {
                    Rules = rules,
                    Metadata = metadata,
                    Search = search,
                    SortColumn = sortColumn,
                    SortOrder = sortOrder
                };

                return PartialView("_ValidationRuleListPartial", viewModel);
            }
            catch (Exception ex)
            {
                // Log failure and return 500 status code to gracefully fail the AJAX request
                _logger.LogError(ex, nameof(ValidationRuleListPartial), nameof(RulesController), $"Error loading Rule list for pageIndex: {pageIndex}, search: {search}", HttpContext.Connection.RemoteIpAddress?.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading the rules list.");
            }
        }

        /// <summary>
        /// Renders a partial view containing a blank form for creating a new validation rule.
        /// </summary>
        [HttpGet]
        public IActionResult Add_New()
        {
            try
            {
                // Populate required dropdown lists for the form UI
                PopulateDropdowns();

                // Initialize a new rule default to active
                var newRule = new ValidationRule { IsActive = true };

                return PartialView("_RuleFormPartial", newRule);
            }
            catch (Exception ex)
            {
                // Log and return 500 on form load failure
                _logger.LogError(ex, nameof(Add_New), nameof(RulesController), "Error loading the new rule form.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while generating the new rule form.");
            }
        }

        /// <summary>
        /// Retrieves an existing rule by its ID and populates the edit form modal.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetRuleById(int id)
        {
            try
            {
                // Validation: ensure ID is valid
                if (id <= 0)
                {
                    return BadRequest("Invalid Rule Request");
                }

                // Retrieve the specific rule from the database
                var rule = await _ruleService.GetRuleByIdAsync(id);
                if (rule == null)
                {
                    return NotFound("Rule not found");
                }

                // Populate required dropdown data before rendering the partial
                PopulateDropdowns();

                return PartialView("_RuleFormPartial", rule);
            }
            catch (Exception ex)
            {
                // Log and return 500 internal server error
                _logger.LogError(ex, nameof(GetRuleById), nameof(RulesController), $"Error fetching rule ID: {id}");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while fetching the rule details.");
            }
        }

        /// <summary>
        /// Processes the submission of a new validation rule and saves it to the database.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector)]
        public async Task<IActionResult> Create(ValidationRule rule)
        {
            try
            {
                // Verify model integrity before saving
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                // Execute the save operation
                await _ruleService.AddRuleAsync(rule);

                // Redirect to the index page upon successful creation
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                // Log failure to persist the new rule
                _logger.LogError(ex, nameof(Create), nameof(RulesController), "Error creating new validation rule.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while creating the rule.");
            }
        }

        /// <summary>
        /// Processes the submission of an existing rule's edits and updates it in the database.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector)]
        public async Task<IActionResult> Edit(ValidationRule rule)
        {
            try
            {
                // Verify model state and reject invalid data
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                // Commit updates to the database via service layer
                await _ruleService.UpdateRuleAsync(rule);

                // Redirect back to the dashboard upon completion
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                // Log the exception securely
                _logger.LogError(ex, nameof(Edit), nameof(RulesController), $"Error updating rule ID: {rule?.RuleId}");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while updating the rule.");
            }
        }

        /// <summary>
        /// Performs a deletion operation for a specific validation rule by its ID.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector)]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                // Ensure the provided ID is valid
                if (id <= 0)
                {
                    return BadRequest(new { success = false, message = "Invalid rule ID." });
                }

                // Execute the deletion
                await _ruleService.DeleteRuleAsync(id);

                return Json(new { success = true, message = "Rule deleted successfully!" });
            }
            catch (Exception ex)
            {
                // Log deletion error and return structured JSON 500 error
                _logger.LogError(ex, nameof(Delete), nameof(RulesController), $"Error deleting rule ID: {id}");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred while deleting the rule." });
            }
        }

        /// <summary>
        /// Cascading dropdown API: Retrieves the list of valid columns based on a selected target staging table.
        /// </summary>
        [HttpGet]
        public IActionResult GetColumnsForTable(string tableName)
        {
            try
            {
                // Validate table name payload
                if (string.IsNullOrWhiteSpace(tableName))
                {
                    return BadRequest("Table name is required.");
                }

                var columns = new List<string>();

                // Build mapping logic returning valid columns based on table selected in the UI
                if (tableName == "Staging_Employers")
                {
                    columns.AddRange(new[] {
                        "PrimaryEIN", "AffiliatedEIN", "EmployerName", "ForeignAddressInd", "Address", "Address2", "City",
                        "StateOrProvince", "Zip", "Country", "Phone", "Contact", "Title", "OriginCode", "SHOPIdentifier", "Notes"
                    });
                }
                else if (tableName == "Staging_Plans")
                {
                    columns.AddRange(new[] {
                        "PrimaryEIN", "PlanName", "PlanType", "OfferedToSpouse", "OfferedToDependents", "WaitingPeriodNumberOfDays",
                        "EligibleFirstOfTheMonth", "FundingType", "PlanRenewalMonth", "PlanTerminatesOnDateOfTermination",
                        "MinimumValue", "BandingType", "PremiumCap"
                    });
                }
                else if (tableName == "Staging_Premiums")
                {
                    columns.AddRange(new[] {
                        "PrimaryEIN", "PlanName", "BandingType", "Start", "End", "StartDate", "EndDate", "EEMonthlyContribution"
                    });
                }
                else if (tableName == "Staging_Employees")
                {
                    columns.AddRange(new[] {
                        "PrimaryEIN", "EINAssociatedWithEE", "EmployeeLegalFirstName", "EmployeeMiddleInitial", "EmployeeLegalLastName",
                        "EmployeeSuffix", "EmployeeSSN", "EmployeeBirthdate", "Status", "Expatriate", "StatusStartDate",
                        "StatusEndDate", "HireDate", "TerminationDate", "W2IncomeOrAnnualSalary", "AdditionalIncome", "HourlyRate",
                        "EmailAddress", "ForeignAddress", "Address1", "Address2", "City", "StateOrProvince", "Zip", "Country",
                        "LowestCostPlanOffered", "DateEligibleForCoverage", "CoverageElected", "CoverageStartDate", "CoverageEndDate",
                        "UnionEmployee", "DateEmployerFirstPaysToUnionBenefits", "DateEmployerLastPaysToUnionBenefits",
                        "COBRABenefitsElected", "COBRACoverageEffectiveDate", "COBRACoverageEndDate", "RetireeBenefitsElected",
                        "RetireePlanStartDate", "RetireePlanEndDate"
                    });
                }
                else if (tableName == "Staging_Dependents")
                {
                    columns.AddRange(new[] {
                        "PrimaryEIN", "EINAssociatedWithEE", "EmployeeSSN", "DependentLegalFirstName", "DependentMiddleInitial",
                        "DependentLegalLastName", "DependentSuffix", "DependentSSN", "DependentBirthdate",
                        "DependentCoverageStartDate", "DependentCoverageEndDate"
                    });
                }

                // Return the dynamic array back to the frontend AJAX request
                return Json(columns);
            }
            catch (Exception ex)
            {
                // Log and gracefully fail
                _logger.LogError(ex, nameof(GetColumnsForTable), nameof(RulesController), $"Error fetching columns for table {tableName}");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while retrieving columns.");
            }
        }

        // Helper method to prepare dropdown data
        private void PopulateDropdowns()
        {
            ViewBag.TargetTables = new List<SelectListItem>
            {
                new SelectListItem { Text = "Staging Employers", Value = "Staging_Employers" },
                new SelectListItem { Text = "Staging Plans", Value = "Staging_Plans" },
                new SelectListItem { Text = "Staging Premiums", Value = "Staging_Premiums" },
                new SelectListItem { Text = "Staging Employees", Value = "Staging_Employees" },
                new SelectListItem { Text = "Staging Dependents", Value = "Staging_Dependents" }
            };

            ViewBag.Severities = new List<SelectListItem>
            {
                // MAP: "No upload errors" -> "Error"
                new SelectListItem { Text = "No Upload Errors (Block Import)", Value = "Error" },
                // MAP: "Acceptable error" -> "Warning"
                new SelectListItem { Text = "Acceptable Error (Allow Import)", Value = "Warning" },
                new SelectListItem { Text = "Information Only", Value = "Information" }
            };

            ViewBag.ValidationTypes = new List<SelectListItem>
            {
                new SelectListItem { Text = "Is Required", Value = "REQUIRED" },
                new SelectListItem { Text = "Is a Valid Number", Value = "IS_NUMERIC" },
                new SelectListItem { Text = "Is a Valid Date", Value = "IS_DATE" },
                new SelectListItem { Text = "Matches Pattern (Regex)", Value = "REGEX_MATCH" },
                new SelectListItem { Text = "Length Equals", Value = "LENGTH_EQUALS" },
                new SelectListItem { Text = "Is in List", Value = "IN_LIST" },
                new SelectListItem { Text = "Is Greater Than Column", Value = "GREATER_THAN_COLUMN" },
                new SelectListItem { Text = "Exists in Another Table", Value = "EXISTS_IN_TABLE" },
                new SelectListItem { Text = "Required if Other Column is Null", Value = "REQUIRED_IF_NULL" }
            };
        }

        /// <summary>
        /// Generates and exports the complete list of validation rules as a downloadable Excel document.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> ExportRules(string format = "excel", string search = "", string title = "Validation Rules")
        {
            try
            {
                var paginationEntity = new PaginationEntity
                {
                    PageIndex = 1,
                    PageSize = int.MaxValue,
                    Search = search,
                    SortColumn = 0,
                    SortOrder = "asc"
                };

                var (rules, _) = await _ruleService.GetRuleList(paginationEntity);

                return HandleExport(
                    _exportService,
                    format,
                    title: title,
                    fileName: "ValidationRules",
                    columns: new List<ExportColumn>
                    {
                new ExportColumn("RuleName",       "Rule Name"),
                new ExportColumn("TargetTable",    "Target Table"),
                new ExportColumn("ValidationType", "Validation Type"),
                new ExportColumn("ErrorMessage",   "Error Message")
                    },
                    rows: rules.Select(r => new Dictionary<string, object>
                    {
                        ["RuleName"] = r.RuleName ?? "",
                        ["TargetTable"] = r.TargetTable ?? "",
                        ["ValidationType"] = r.ValidationType ?? "",
                        ["ErrorMessage"] = r.ErrorMessage ?? ""
                    }).ToList()
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(ExportRules), nameof(RulesController), "Error exporting rules.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while exporting rules.");
            }
        }

        /// <summary>
        /// Reads an uploaded Excel file, parsing its contents to bulk insert or update validation rules.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector)]
        public async Task<IActionResult> ImportRules(IFormFile importFile)
        {
            try
            {
                // Validate payload file
                if (importFile == null || importFile.Length <= 0)
                {
                    return BadRequest(new { success = false, message = "No valid file uploaded." });
                }

                using (var stream = new MemoryStream())
                {
                    // Pull file payload into local memory
                    await importFile.CopyToAsync(stream);

                    using (var package = new ExcelPackage(stream))
                    {
                        var worksheet = package.Workbook.Worksheets[0];
                        int rowCount = worksheet.Dimension.Rows;

                        // Process Excel structure from row 2 downward
                        for (int row = 2; row <= rowCount; row++)
                        {
                            // Helper delegate to extract cell strings safely
                            string GetVal(int col) => worksheet.Cells[row, col].Value?.ToString()?.Trim();

                            // SKIP invalid rows missing a core Rule Name
                            if (string.IsNullOrWhiteSpace(GetVal(2))) continue;

                            // Map extracted text array into an entity
                            var rule = new ValidationRule
                            {
                                RuleId = int.TryParse(GetVal(1), out int id) ? id : 0,
                                RuleName = GetVal(2),
                                TargetTable = GetVal(3) ?? "Staging_Employees", // Default fallback
                                TargetColumn = GetVal(4) ?? "Unknown",          // Default fallback
                                ValidationType = GetVal(5) ?? "REQUIRED",       // Default fallback

                                RuleParameter1 = GetVal(6),
                                RuleParameter2 = GetVal(7),
                                ErrorMessage = GetVal(8) ?? "Invalid Data",

                                // Bool Fields (Default to true/false logically if empty)
                                IsActive = bool.TryParse(GetVal(9), out bool active) ? active : true,

                                // If Excel payload is empty for Severity, default defensively to "Error"
                                Severity = !string.IsNullOrEmpty(GetVal(10)) ? GetVal(10) : "Error",

                                IsCorrectable = bool.TryParse(GetVal(11), out bool correctable) ? correctable : false,

                                // Map Optional Documentation Text Fields
                                HelpText = GetVal(12),
                                Topic = GetVal(13),
                                AuditTabNote = GetVal(14),
                                AlternateNote = GetVal(15),
                                EmailSummaryNote = GetVal(16),
                                RuleCode = GetVal(17)
                            };

                            // Conditionally branch save mechanism via the configured logic service
                            if (rule.RuleId > 0)
                            {
                                await _ruleService.UpdateRuleAsync(rule);
                            }
                            else
                            {
                                await _ruleService.AddRuleAsync(rule);
                            }
                        }
                    }
                }
                return Json(new { success = true, message = "Rules imported successfully!" });
            }
            catch (Exception ex)
            {
                // Document import parsing or insertion failures and relay standard HTTP error
                _logger.LogError(ex, nameof(ImportRules), nameof(RulesController), "Import Failed");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "Database Error: An unexpected error occurred during import." });
            }
        }
    }
}
