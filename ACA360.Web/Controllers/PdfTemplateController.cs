using ACA360.Core.Constants;
using ACA360.Core.Models;
using ACA360.Core.Models.ACA360.Core.Models;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ACA360.BusinessLogic.Interfaces;

namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector + "," + UserRoles.DASupervisor + "," + UserRoles.DASupervisor + "," + UserRoles.AMSupervisor + "," + UserRoles.Broker + "," + UserRoles.DataAnalyst + "," + UserRoles.Employer + "," + UserRoles.StaffAC + "," + UserRoles.StaffAU + "," + UserRoles.AccountManager)]
    public class PdfTemplateController : Controller
    {
        private readonly string _connString;              // Database connection string resolved from configuration
        private readonly IWebHostEnvironment _env;        // Web host environment for resolving wwwroot physical paths
        private readonly ISmartPdfService _pdfService;    // Service for PDF field extraction and manipulation
        private readonly ILogger<PdfTemplateController> _logger; // Logger for capturing errors and diagnostics

        /// <summary>
        /// Initializes the PdfTemplateController with required infrastructure and service dependencies.
        /// </summary>
        /// <param name="config">Application configuration used to resolve the database connection string.</param>
        /// <param name="env">Web host environment for constructing physical file paths under wwwroot.</param>
        /// <param name="pdfService">Service responsible for PDF field extraction.</param>
        /// <param name="logger">Logger instance for runtime diagnostics and error reporting.</param>
        public PdfTemplateController(
            IConfiguration config,
            IWebHostEnvironment env,
            ISmartPdfService pdfService,
            ILogger<PdfTemplateController> logger)
        {
            _connString = config.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException(
                    "ConnectionStrings:DefaultConnection is missing from configuration."); ;
            _env = env;
            _pdfService = pdfService;
            _logger = logger;
        }

        // =========================================================
        // 1. LIST ALL TEMPLATES
        // =========================================================

        /// <summary>
        /// Retrieves and displays all PDF templates stored in the database, ordered by tax year descending.
        /// GET: /PdfTemplate/Index
        /// </summary>
        /// <returns>The Index view populated with all PDF templates, or a 500 error response on failure.</returns>
        public async Task<IActionResult> Index()
        {
            try
            {
                // Open a connection and fetch all templates sorted by most recent tax year first
                using var db = new SqlConnection(_connString);
                var templates = await db.QueryAsync<PdfTemplate>("SELECT * FROM PdfTemplates ORDER BY TaxYear DESC");

                return View(templates);
            }
            catch (Exception ex)
            {
                // Log unexpected database or infrastructure failures
                _logger.LogError(ex, "An unexpected error occurred in Index while retrieving PDF templates.");
                return StatusCode(500, "An unexpected error occurred while loading PDF templates.");
            }
        }

        // =========================================================
        // 2. UPLOAD NEW TEMPLATE
        // =========================================================

        /// <summary>
        /// Handles the upload of a new PDF template file, saves it to wwwroot/FormTemplates,
        /// and persists the metadata entry to the database.
        /// POST: /PdfTemplate/Upload
        /// </summary>
        /// <param name="file">The uploaded PDF file from the form.</param>
        /// <param name="taxYear">The tax year this template applies to.</param>
        /// <param name="formType">The form type identifier (e.g., "1095-C", "1094-C").</param>
        /// <returns>Redirects to Index on success, or a 500 response on failure.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upload(IFormFile file, int taxYear, string formType)
        {
            try
            {
                // Guard: reject the request early if no file was provided or the file is empty
                if (file == null || file.Length == 0)
                    return RedirectToAction("Index");

                // Resolve the target folder inside wwwroot where templates are stored
                string relativeFolder = "FormTemplates";
                string absoluteFolder = Path.Combine(_env.WebRootPath, relativeFolder);

                // Ensure the target directory exists before attempting to write the file
                if (!Directory.Exists(absoluteFolder))
                    Directory.CreateDirectory(absoluteFolder);

                // Sanitise the original file name to strip any path traversal characters (security)
                string cleanFileName = Path.GetFileName(file.FileName);

                // Prefix the file name with form type and tax year to ensure uniqueness and traceability
                string uniqueFileName = $"{formType}_{taxYear}_{cleanFileName}";

                // Construct the full physical save path on disk
                string fullSavePath = Path.Combine(absoluteFolder, uniqueFileName);

                // Write the uploaded file stream to disk
                using (var stream = new FileStream(fullSavePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                // Build the web-accessible relative path to store in the database
                string dbRelativePath = $"/FormTemplates/{uniqueFileName}";

                // Persist the template metadata to the database
                using var db = new SqlConnection(_connString);

                string sql = @"INSERT INTO PdfTemplates 
                       (FormType, TaxYear, FileName, FilePath, UploadedBy) 
                       VALUES (@FormType, @TaxYear, @FileName, @FilePath, 1)";

                await db.ExecuteAsync(sql, new
                {
                    FormType = formType,
                    TaxYear = taxYear,
                    FileName = cleanFileName,
                    FilePath = dbRelativePath
                });

                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                // Log failures with contextual details to aid debugging of file or database issues
                _logger.LogError(ex, "An unexpected error occurred in Upload. FormType: {FormType}, TaxYear: {TaxYear}", formType, taxYear);
                return StatusCode(500, "An unexpected error occurred while uploading the PDF template.");
            }
        }

        // =========================================================
        // 3. FIELD MAPPING SCREEN
        // =========================================================

        /// <summary>
        /// Loads the field mapping screen for a given PDF template.
        /// Extracts PDF field names via iTextSharp and joins them with any previously saved mappings.
        /// GET: /PdfTemplate/MapFields?id={id}
        /// </summary>
        /// <param name="id">The unique identifier of the PDF template to map.</param>
        /// <returns>The MapFields view pre-populated with field mappings, or 404/500 on failure.</returns>
        [HttpGet]
        public async Task<IActionResult> MapFields(int id)
        {
            try
            {
                using var db = new SqlConnection(_connString);

                // Fetch the template record by ID — return 404 if it no longer exists
                var template = await db.QuerySingleOrDefaultAsync<PdfTemplate>(
                    "SELECT * FROM PdfTemplates WHERE TemplateId = @Id", new { Id = id });

                if (template == null)
                    return NotFound();

                // Retrieve any previously saved field-to-system-key mappings for this template
                var existingMaps = await db.QueryAsync<PdfFieldMap>(
                    "SELECT * FROM PdfFieldMaps WHERE TemplateId = @Id", new { Id = id });

                // Extract the raw list of field names directly from the PDF file using iTextSharp
                var pdfFields = _pdfService.ExtractFieldNames(template.FilePath);

                // Build the view model by merging PDF fields with existing DB mappings
                // If a field was previously mapped, pre-fill its system key selection
                var model = new PdfMappingViewModel
                {
                    TemplateId = id,
                    TemplateName = template.FileName,
                    FieldList = pdfFields.Select(f => new PdfFieldMapViewModel
                    {
                        PdfFieldName = f,
                        // Pre-fill the mapped system key if this field already has a saved mapping
                        MappedSystemKey = existingMaps.FirstOrDefault(m => m.PdfFieldName == f)?.SystemDataKey ?? ""
                    }).ToList(),

                    // Populate the dropdown menu of available system data keys for the mapping UI
                    SystemDataKeys = GetSystemDataDictionary()
                };

                return View(model);
            }
            catch (Exception ex)
            {
                // Log failure with the template ID for quick correlation with the failing record
                _logger.LogError(ex, "An unexpected error occurred in MapFields. TemplateId: {TemplateId}", id);
                return StatusCode(500, "An unexpected error occurred while loading the field mapping screen.");
            }
        }

        // =========================================================
        // 4. SAVE FIELD MAPPINGS
        // =========================================================

        /// <summary>
        /// Persists the field-to-system-key mappings for a given template within a database transaction.
        /// Wipes existing mappings before inserting the updated set to ensure a clean replace.
        /// POST: /PdfTemplate/SaveMapping
        /// </summary>
        /// <param name="templateId">The unique identifier of the template whose mappings are being saved.</param>
        /// <param name="mappings">The full list of field mappings submitted from the mapping screen.</param>
        /// <returns>Redirects to Index on success, or a 500 response on failure.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveMapping(int templateId, List<PdfFieldMap> mappings)
        {
            try
            {
                using var db = new SqlConnection(_connString);
                await db.OpenAsync();

                // Wrap delete and insert in a transaction to guarantee atomicity — no partial saves
                using var trans = db.BeginTransaction();

                // Remove all existing mappings for this template before inserting the updated set
                await db.ExecuteAsync(
                    "DELETE FROM PdfFieldMaps WHERE TemplateId = @Id",
                    new { Id = templateId },
                    transaction: trans);

                // Insert only mappings where the user has selected a system key (ignore unmapped fields)
                string sql = "INSERT INTO PdfFieldMaps (TemplateId, PdfFieldName, SystemDataKey) VALUES (@TemplateId, @PdfFieldName, @SystemDataKey)";

                foreach (var map in mappings.Where(m => !string.IsNullOrEmpty(m.SystemDataKey)))
                {
                    await db.ExecuteAsync(sql,
                        new { TemplateId = templateId, map.PdfFieldName, map.SystemDataKey },
                        transaction: trans);
                }

                // Commit the transaction only after all inserts succeed
                trans.Commit();

                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                // Log failure with the template ID; the transaction will auto-rollback on disposal
                _logger.LogError(ex, "An unexpected error occurred in SaveMapping. TemplateId: {TemplateId}", templateId);
                return StatusCode(500, "An unexpected error occurred while saving the field mappings.");
            }
        }

        // =========================================================
        // 5. EDIT TEMPLATE
        // =========================================================

        /// <summary>
        /// Loads the edit form for an existing PDF template record.
        /// GET: /PdfTemplate/Edit?id={id}
        /// </summary>
        /// <param name="id">The unique identifier of the template to edit.</param>
        /// <returns>The Edit view pre-populated with the template data, or 404/500 on failure.</returns>
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            try
            {
                // Fetch the template record by ID — return 404 if not found
                using var db = new SqlConnection(_connString);
                var template = await db.QuerySingleOrDefaultAsync<PdfTemplate>(
                    "SELECT * FROM PdfTemplates WHERE TemplateId = @Id", new { Id = id });

                if (template == null)
                    return NotFound();

                return View(template);
            }
            catch (Exception ex)
            {
                // Log failure with the template ID for traceability
                _logger.LogError(ex, "An unexpected error occurred in Edit (GET). TemplateId: {TemplateId}", id);
                return StatusCode(500, "An unexpected error occurred while loading the template for editing.");
            }
        }

        /// <summary>
        /// Persists updates to a PDF template's tax year, form type, and active status.
        /// POST: /PdfTemplate/Edit
        /// </summary>
        /// <param name="model">The updated template data submitted from the edit form.</param>
        /// <returns>Redirects to Index on success, or a 500 response on failure.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(PdfTemplate model)
        {
            try
            {
                // Persist the updated tax year, form type, and active status to the database
                using var db = new SqlConnection(_connString);

                string sql = @"UPDATE PdfTemplates 
                       SET TaxYear = @TaxYear, FormType = @FormType, IsActive = @IsActive 
                       WHERE TemplateId = @TemplateId";

                await db.ExecuteAsync(sql, model);

                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                // Log failure with the template ID for traceability
                _logger.LogError(ex, "An unexpected error occurred in Edit (POST). TemplateId: {TemplateId}", model?.TemplateId);
                return StatusCode(500, "An unexpected error occurred while saving the template changes.");
            }
        }

        // =========================================================
        // 6. DELETE TEMPLATE
        // =========================================================

        /// <summary>
        /// Deletes a PDF template record from the database and removes its associated file from disk.
        /// The DB delete cascades to related PdfFieldMaps records via the foreign key constraint.
        /// POST: /PdfTemplate/Delete
        /// </summary>
        /// <param name="id">The unique identifier of the template to delete.</param>
        /// <returns>Redirects to Index on success, or a 500 response on failure.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                using var db = new SqlConnection(_connString);

                // Fetch the template first to retrieve the file path needed for disk deletion
                var template = await db.QuerySingleOrDefaultAsync<PdfTemplate>(
                    "SELECT * FROM PdfTemplates WHERE TemplateId = @Id", new { Id = id });

                if (template != null)
                {
                    // Remove the database record — FK cascade will also delete related PdfFieldMaps rows
                    await db.ExecuteAsync("DELETE FROM PdfTemplates WHERE TemplateId = @Id", new { Id = id });

                    // Remove the physical file from disk if it still exists
                    if (System.IO.File.Exists(template.FilePath))
                        System.IO.File.Delete(template.FilePath);
                }

                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                // Log failure with the template ID; a partial delete (DB success, file failure) will be visible in logs
                _logger.LogError(ex, "An unexpected error occurred in Delete. TemplateId: {TemplateId}", id);
                return StatusCode(500, "An unexpected error occurred while deleting the template.");
            }
        }

        // =========================================================
        // HELPER: SYSTEM DATA KEY DICTIONARY
        // =========================================================

        /// <summary>
        /// Builds the grouped dropdown list of system data keys available for PDF field mapping.
        /// Covers form header flags, employee/employer info, offer codes (1095-C), covered individuals,
        /// and 1094-C transmittal data — all keyed with dot-notation paths for runtime resolution.
        /// </summary>
        /// <returns>A grouped list of SelectListItems representing all mappable system data keys.</returns>

        private List<SelectListItem> GetSystemDataDictionary()
        {
            var items = new List<SelectListItem>();

            string[] months = { "All", "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

            // --- Form Header Flags ---
            var groupHeader = new SelectListGroup { Name = "Form Header" };

            items.Add(new SelectListItem { Value = "Employeecode.header.void", Text = "VOID Checkbox (EmployeeCode)", Group = groupHeader });
            items.Add(new SelectListItem { Value = "Employeecode.header.iscorrect", Text = "CORRECTED Checkbox (EmployeeCode)", Group = groupHeader });

            items.Add(new SelectListItem { Value = "Employer.isCorrected", Text = "Employer isCorrected", Group = groupHeader });
            // NEW — signature block, shared by all four forms
            items.Add(new SelectListItem { Value = "Header.SignatureName", Text = "Signature / Signer Name", Group = groupHeader });
            items.Add(new SelectListItem { Value = "Header.SignatureTitle", Text = "Signer Title", Group = groupHeader });
            items.Add(new SelectListItem { Value = "Header.SignatureDate", Text = "Signature Date", Group = groupHeader });

            // --- Part I: Employee / Responsible Individual Information ---
            var groupEmployee = new SelectListGroup { Name = "Part I: Employee" };
            items.Add(new SelectListItem { Value = "Employee.FirstName", Text = "First Name", Group = groupEmployee });
            items.Add(new SelectListItem { Value = "Employee.MiddleName", Text = "Middle Name", Group = groupEmployee });
            items.Add(new SelectListItem { Value = "Employee.LastName", Text = "Last Name", Group = groupEmployee });
            items.Add(new SelectListItem { Value = "Employee.SSN", Text = "SSN", Group = groupEmployee });
            items.Add(new SelectListItem { Value = "Employee.DOB", Text = "Date of Birth", Group = groupEmployee });
            items.Add(new SelectListItem { Value = "Employee.Address", Text = "Street Address", Group = groupEmployee });
            items.Add(new SelectListItem { Value = "Employee.City", Text = "City", Group = groupEmployee });
            items.Add(new SelectListItem { Value = "Employee.State", Text = "State", Group = groupEmployee });
            items.Add(new SelectListItem { Value = "Employee.Zip", Text = "Zip Code", Group = groupEmployee });
            items.Add(new SelectListItem { Value = "Employee.Country", Text = "Country", Group = groupEmployee });
            items.Add(new SelectListItem { Value = "Employee.isCorrected", Text = "isCorrected", Group = groupEmployee });
            // NEW — 1095-B Part I Line 8
            items.Add(new SelectListItem { Value = "Coverage.OriginCode", Text = "Origin of Health Coverage Code (A-G)", Group = groupEmployee });

            // --- Part I: Employer / ALE Member / Filer Information ---
            var groupEmployer = new SelectListGroup { Name = "Part I: Employer" };
            items.Add(new SelectListItem { Value = "Employer.Name", Text = "Employer Name", Group = groupEmployer });
            items.Add(new SelectListItem { Value = "Employer.EIN", Text = "Employer EIN", Group = groupEmployer });
            items.Add(new SelectListItem { Value = "Employer.Address", Text = "Street Address", Group = groupEmployer });
            items.Add(new SelectListItem { Value = "Employer.City", Text = "City", Group = groupEmployer });
            items.Add(new SelectListItem { Value = "Employer.State", Text = "State", Group = groupEmployer });
            items.Add(new SelectListItem { Value = "Employer.Zip", Text = "Zip Code", Group = groupEmployer });
            items.Add(new SelectListItem { Value = "Employer.Phone", Text = "Contact Phone", Group = groupEmployer });
            items.Add(new SelectListItem { Value = "Employer.ContactName", Text = "Contact Name", Group = groupEmployer }); // Used on 1094-C / 1094-B

            // NEW — 1095-B Part III: Issuer or Other Coverage Provider
            var groupIssuer = new SelectListGroup { Name = "Part III: Issuer / Coverage Provider (1095-B)" };
            items.Add(new SelectListItem { Value = "Issuer.Name", Text = "Issuer / Provider Name", Group = groupIssuer });
            items.Add(new SelectListItem { Value = "Issuer.EIN", Text = "Issuer EIN", Group = groupIssuer });
            items.Add(new SelectListItem { Value = "Issuer.Phone", Text = "Issuer Contact Phone", Group = groupIssuer });
            items.Add(new SelectListItem { Value = "Issuer.Address", Text = "Issuer Street Address", Group = groupIssuer });
            items.Add(new SelectListItem { Value = "Issuer.City", Text = "Issuer City", Group = groupIssuer });
            items.Add(new SelectListItem { Value = "Issuer.State", Text = "Issuer State", Group = groupIssuer });
            items.Add(new SelectListItem { Value = "Issuer.Zip", Text = "Issuer Zip Code", Group = groupIssuer });

            // --- Part II: Employee Offer & Coverage Codes (1095-C, Lines 14-17) ---
            var groupCodes = new SelectListGroup { Name = "Part II: Employee Offer & Coverage" };
            items.Add(new SelectListItem { Value = "Code.PlanStartMonth", Text = "Plan Start Month", Group = groupCodes });
            items.Add(new SelectListItem { Value = "Code.Age", Text = "Employee Age (Jan 1)", Group = groupCodes });

            foreach (var m in months) items.Add(new SelectListItem { Value = $"Code.{m}.14", Text = $"Line 14 ({m}) - Offer Code", Group = groupCodes });
            foreach (var m in months) items.Add(new SelectListItem { Value = $"Code.{m}.15", Text = $"Line 15 ({m}) - Premium", Group = groupCodes });
            foreach (var m in months) items.Add(new SelectListItem { Value = $"Code.{m}.16", Text = $"Line 16 ({m}) - Safe Harbor", Group = groupCodes });
            // NEW — Line 17, required for ICHRA codes 1L-1U on Line 14. No "All 12 Months" box exists for Line 17 on the real form.
            foreach (var m in months)
            {
                items.Add(new SelectListItem
                {
                    Value = $"Line17.{m.ToUpper()}",
                    Text = $"Line 17 ({m}) - Employer ZIP",
                    Group = groupCodes
                });
            }

            // --- Part III: Covered Individuals (up to 13 dependent rows) ---
            var groupCovered = new SelectListGroup { Name = "Part III: Covered Individuals" };
            for (int i = 1; i <= 13; i++)
            {
                items.Add(new SelectListItem { Value = $"Covered.FirstName.{i}", Text = $"Row {i} - First Name", Group = groupCovered });
                items.Add(new SelectListItem { Value = $"Covered.MiddleName.{i}", Text = $"Row {i} - Middle Name", Group = groupCovered });
                items.Add(new SelectListItem { Value = $"Covered.LastName.{i}", Text = $"Row {i} - Last Name", Group = groupCovered });
                items.Add(new SelectListItem { Value = $"Covered.Name.{i}", Text = $"Row {i} - Full Name", Group = groupCovered }); // Use this alone for 1095-B, which has a single combined name field
                items.Add(new SelectListItem { Value = $"Covered.SSN.{i}", Text = $"Row {i} - SSN", Group = groupCovered });
                items.Add(new SelectListItem { Value = $"Covered.DOB.{i}", Text = $"Row {i} - DOB", Group = groupCovered });
                items.Add(new SelectListItem { Value = $"Covered.All12.{i}", Text = $"Row {i} - All 12 Months", Group = groupCovered });

                foreach (var m in months.Skip(1))
                    items.Add(new SelectListItem { Value = $"Covered.{m}.{i}", Text = $"Row {i} - {m}", Group = groupCovered });
            }

            // --- 1094-C Transmittal Specifics ---
            var group1094 = new SelectListGroup { Name = "1094-C Transmittal Data" };
            items.Add(new SelectListItem { Value = "TotalForms", Text = "Line 18: Total Forms 1095-C Submitted With This 1094-C", Group = group1094 });
            // NEW
            items.Add(new SelectListItem { Value = "Header.AuthoritativeTransmittal", Text = "Line 19: Authoritative Transmittal Checkbox", Group = group1094 });
            items.Add(new SelectListItem { Value = "Count.Total1095C.ALEAggregate", Text = "Line 20: Total Forms 1095-C Filed for ALE Member", Group = group1094 });
            items.Add(new SelectListItem { Value = "Aggregated.ALEGroup.Indicator", Text = "Line 21: Aggregated ALE Group Member? (YES)", Group = group1094 });
            items.Add(new SelectListItem { Value = "Aggregated.ALEGroup.Indicator.No", Text = "Line 21: Aggregated ALE Group Member? (NO)", Group = group1094 });

            items.Add(new SelectListItem { Value = "Cert.QualifyingOffer", Text = "Line 22 Box A: Qualifying Offer Method", Group = group1094 });
            items.Add(new SelectListItem { Value = "Cert.98Percent", Text = "Line 22 Box D: 98% Offer Method", Group = group1094 });
            // Boxes B and C on Line 22 are Reserved by the IRS (unused since 2016) - intentionally omitted.

            // Part III Lines 23-35: All 12 Months + Jan-Dec, columns (a) MEC offer, (b) FT count, (c) total count, (d) aggregated group indicator
            foreach (var m in months) // NOTE: includes "All" this time - Part III genuinely has an "All 12 Months" row (Line 23)
            {
                items.Add(new SelectListItem { Value = $"Ind.MEC.Yes.{m}", Text = $"Part III Col (a) - MEC Offer YES ({m})", Group = group1094 });
                items.Add(new SelectListItem { Value = $"Ind.MEC.No.{m}", Text = $"Part III Col (a) - MEC Offer NO ({m})", Group = group1094 });

                items.Add(new SelectListItem { Value = $"Count.FullTime.{m}", Text = $"Part III Col (b) - Full-Time Employee Count ({m})", Group = group1094 });
                items.Add(new SelectListItem { Value = $"Count.Total.{m}", Text = $"Part III Col (c) - Total Employee Count ({m})", Group = group1094 });
                items.Add(new SelectListItem { Value = $"Group.Indicator.{m}", Text = $"Part III Col (d) - Aggregated Group Indicator ({m})", Group = group1094 }); // NEW
            }

            // NEW — Part IV: Other ALE Members of Aggregated ALE Group (up to 30 per IRS instructions)
            var group1094PartIV = new SelectListGroup { Name = "1094-C Part IV: Aggregated ALE Group Members" };
            for (int n = 1; n <= 30; n++)
            {
                items.Add(new SelectListItem { Value = $"AggregateMember.Name.{n}", Text = $"Member {n} - Name", Group = group1094PartIV });
                items.Add(new SelectListItem { Value = $"AggregateMember.EIN.{n}", Text = $"Member {n} - EIN", Group = group1094PartIV });
            }

            // NEW — 1094-B Transmittal Specifics
            var group1094B = new SelectListGroup { Name = "1094-B Transmittal Data" };
            items.Add(new SelectListItem { Value = "Count.Total1095B.Submitted", Text = "Total Forms 1095-B Submitted With This 1094-B", Group = group1094B });

            return items;
        }

    }
}