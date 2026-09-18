using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Constants;
using ACA360.Core.Models;
using ACA360.Repositories.Interfaces;
using Microsoft.AspNetCore.Http; // Required for Session extensions
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ACA360.Web.Helpers
{
    public class BaseController : Controller
    {
        /// <summary>
        /// The effective context for this request � the real user, plus any active
        /// View As. Resolved server-side; never populated from the client.
        /// </summary>
        protected EffectiveContext CurrentContext =>
            HttpContext?.RequestServices?.GetService(typeof(IViewAsService)) is IViewAsService svc
                ? svc.Resolve(HttpContext)
                : new EffectiveContext();

        /// <summary>Role to scope data by � the impersonated role when viewing as.</summary>
        protected string GetEffectiveRole() => CurrentContext.EffectiveRole;

        /// <summary>User id entitlement lookups should run against.</summary>
        protected int GetEffectiveScopeUserId() => CurrentContext.ScopeUserId;

        /// <summary>
        /// Roles that legitimately see every employer rather than an assigned set.
        ///
        /// Kept identical to SelectEmployerController.IsUnrestrictedRole on purpose:
        /// if the employer picker and a data surface disagree about who is unscoped,
        /// one of them is wrong and it is not obvious which.
        ///
        /// Note this is a broader list than the one GetEmployersForUserAsync treats
        /// as unrestricted — that routes both supervisor roles through assignment
        /// lookups. Widening here preserves existing supervisor behaviour rather
        /// than silently narrowing it as part of a security fix.
        /// </summary>
        protected bool IsUnrestrictedEmployerRole()
        {
            var role = GetEffectiveRole();

            return role == UserRoles.SuperAdmin
                || role == UserRoles.Admin
                || role == UserRoles.ACADirector
                || role == UserRoles.DASupervisor
                || role == UserRoles.AMSupervisor;
        }

        /// <summary>
        /// True when this request is entitled to the given employer.
        ///
        /// Routed through IViewAsService.GetPermittedEmployersAsync — the same
        /// oracle SelectEmployer uses — so the answer accounts for impersonation:
        /// an admin viewing as one employer is scoped to that employer, not to
        /// their own unrestricted set. Deliberately no role short-circuit, since a
        /// role test would miss exactly that case.
        ///
        /// Use this on any action that takes an employer id from the client.
        /// Reading employer scope from session needs no check; accepting one as a
        /// parameter always does.
        /// </summary>
        protected async Task<bool> IsEmployerPermittedAsync(long employerId)
        {
            if (employerId <= 0) return false;

            var permitted = await GetPermittedEmployerIdsAsync();
            return permitted.Contains(employerId);
        }

        /// <summary>
        /// Narrows a client-supplied list of employer ids to those this request is
        /// entitled to. Returns the intersection, never the input: an id the caller
        /// cannot reach is dropped rather than honoured.
        /// </summary>
        protected async Task<List<long>> FilterPermittedEmployerIdsAsync(IEnumerable<long>? requested)
        {
            var asked = (requested ?? Enumerable.Empty<long>())
                        .Where(id => id > 0)
                        .Distinct()
                        .ToList();

            if (asked.Count == 0) return new List<long>();

            var permitted = await GetPermittedEmployerIdsAsync();
            return asked.Where(permitted.Contains).ToList();
        }

        /// <summary>
        /// Every employer id this request may reach. Employer.Id is a string on the
        /// model, so unparseable ids are dropped rather than allowed through as 0.
        /// </summary>
        protected async Task<HashSet<long>> GetPermittedEmployerIdsAsync()
        {
            if (HttpContext?.RequestServices?.GetService(typeof(IViewAsService)) is not IViewAsService svc)
                return new HashSet<long>();

            var employers = await svc.GetPermittedEmployersAsync(CurrentContext);

            return (employers ?? new List<Employer>())
                   .Select(e => long.TryParse(e.Id, out var id) ? id : 0L)
                   .Where(id => id > 0)
                   .ToHashSet();
        }
        // 1. Get Current User ID
        protected string GetCurrentUserId()
        {
            if (User == null) return "0";
            var claim = User.FindFirst("UserId");
            if (claim != null && int.TryParse(claim.Value, out int id))
            {
                return id.ToString();
            }
            return "0";
        }

        // 2. Get Selected Employer ID
        protected string GetCurrentEmployerId()
        {
            if (HttpContext?.Session == null) return "0";
            return HttpContext.Session.GetString("SelectedEmployerId") ?? "0";
        }

        protected string GetCurrentEmployerName()
        {
            if (HttpContext?.Session == null) return "0";
            return HttpContext.Session.GetString("SelectedEmployerName") ?? "";
        }
        protected void SetCurrentEmployerId(string empId)
        {
            // FIX 2: Ensure consistency. Always use "SelectedEmployerId"
            if (!string.IsNullOrEmpty(empId))
            {
                HttpContext.Session.SetString("SelectedEmployerId", empId);
            }
            else
            {
                HttpContext.Session.SetString("SelectedEmployerId", "0");
            }
        }

        // 3. Get Current Filing Year
        protected string GetCurrentFilingYear()
        {
            if (HttpContext?.Session == null) return DateTime.UtcNow.Year.ToString();

            // 1. Try reading as a string
            var stringVal = HttpContext.Session.GetString("SelectedFilingYear");
            if (int.TryParse(stringVal, out int parsedYear))
            {
                return parsedYear.ToString();
            }

            // 2. Try reading as Int32
            var intVal = HttpContext.Session.GetInt32("SelectedFilingYear");
            if (intVal.HasValue)
            {
                return intVal.Value.ToString();
            }

            return DateTime.UtcNow.Year.ToString();
        }

        protected void SetCurrentFilingYear(int filingYear)
        {
            // Safety Check: Ensure HttpContext exists before accessing Session
            if (HttpContext == null || !HttpContext.Session.IsAvailable)
            {
                // Log this error in production instead of just throwing
                Console.WriteLine("CRITICAL: Attempted to set session when context was null.");
                return;
            }

            // FIX 4: Store as Int32 (Standardized)
            if (filingYear > 0)
            {
                HttpContext.Session.SetString("SelectedFilingYear", filingYear.ToString());
            }
            else
            {
                HttpContext.Session.SetString("SelectedFilingYear", DateTime.Now.Year.ToString());
            }
        }

        // Overload for String input, converting to Int
        protected void SetCurrentFilingYear(string filingYear)
        {
            if (int.TryParse(filingYear, out int year))
            {
                SetCurrentFilingYear(year);
            }
            else
            {
                SetCurrentFilingYear(DateTime.Now.Year);
            }
        }

        protected string GetUserIp()
        {
            return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        }
        protected void SetCurrentEmployerName(string empName)
        {
            if (HttpContext?.Session == null) return;
            HttpContext.Session.SetString("SelectedEmployerName", empName ?? "");
        }

        //Handle Export Functionality
        protected IActionResult HandleExport(
            IExportService exportService,
            string format,
            string title,
            string fileName,
            List<ExportColumn> columns,
            List<Dictionary<string, object>> rows)
        {
            var exportRequest = new ExportRequest
            {
                Title = title,
                FileName = $"{fileName}_{DateTime.Now:yyyyMMdd_HHmmss}",
                Columns = columns,
                Rows = rows
            };

            switch (format?.ToLowerInvariant())
            {
                case "csv":
                    var csvBytes = exportService.BuildCsv(exportRequest);
                    return File(csvBytes, "text/csv", $"{exportRequest.FileName}.csv");

                case "excel":
                    var excelBytes = exportService.BuildExcel(exportRequest);
                    return File(excelBytes,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"{exportRequest.FileName}.xlsx");

                case "pdf":
                    var pdfBytes = exportService.BuildPdf(exportRequest);
                    return File(pdfBytes, "application/pdf", $"{exportRequest.FileName}.pdf");

                case "print":
                    var html = exportService.BuildPrintHtml(exportRequest);
                    Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
                    return Content(html, "text/html");

                default:
                    return BadRequest(new { success = false, message = "Unsupported export format." });
            }
        }

        // Get All Filing Years globally
        protected async Task<List<FilingYearModel>> GetAllFilingYearsAsync()
        {
            // Resolve the service from the DI container dynamically.
            // This prevents constructor bloat in all derived controllers.
            var filingYearService = HttpContext.RequestServices.GetRequiredService<IFilingYearService>();

            if (filingYearService == null)
            {
                return new List<FilingYearModel>();
            }

            return await filingYearService.GetAllFilingYears();
        }

        // Get All Country globally
        protected async Task<List<SelectListItem>> GetAllCountriesAsync()
        {
            var employeeService = HttpContext.RequestServices.GetService<IEmployeeService>();
            if (employeeService == null) return new List<SelectListItem>();

            return await employeeService.GetAll_Country_Async();
        }

        // Get All States globally
        protected async Task<List<SelectListItem>> GetAllStatesAsync()
        {
            var partnerService = HttpContext.RequestServices.GetService<IPartnerRepository>();
            if (partnerService == null) return new List<SelectListItem>();

            var states = await partnerService.GetAllStatesAsync();

            // Convert StateModel list into SelectListItem list for uniform view rendering
            return states.Select(s => new SelectListItem
            {
                Text = s.StateName,   // Replace with your exact State property name
                Value = s.StateId.ToString()
            }).ToList();
        }
    }
}