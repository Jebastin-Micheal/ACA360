using System.Data;
using ACA360.Repositories.Interfaces;
using ACA360.Web.Helpers;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace ACA360.Web.Controllers
{
    /// <summary>
    /// Global search (Ctrl+K): pages from the user's own menu, the employers they may see, the
    /// employees of the employer they are working in (script 059), and the files the File
    /// Upload list shows them. Every source applies the check its own page applies.
    /// </summary>
    [Authorize]
    public class SearchController : BaseController
    {
        private const int PerGroup = 6;
        private readonly IMenuService _menus;
        private readonly IFileUploadLogService _files;
        private readonly string _connectionString;
        private readonly ILogger<SearchController> _logger;

        public SearchController(IMenuService menus, IFileUploadLogService files, IConfiguration configuration, ILogger<SearchController> logger)
        {
            _menus = menus;
            _files = files;
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
            _logger = logger;
        }

        public sealed record Hit(string Title, string? Subtitle, string Url, string Icon);

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Query(string? q)
        {
            q = (q ?? "").Trim();
            if (q.Length < 2) return Json(new { pages = Array.Empty<Hit>(), employers = Array.Empty<Hit>(), employees = Array.Empty<Hit>(), files = Array.Empty<Hit>() });
            if (q.Length > 100) q = q[..100];

            var pages = await Safe("pages", Pages(q));
            var employers = await Safe("employers", Employers(q));
            var (employees, employeeNote) = await Safe("employees", Employees(q), (new List<Hit>(), (string?)null));
            var files = await Safe("files", Files(q));
            return Json(new { pages, employers, employees, employeeNote, files });
        }

        // One failing source must not empty the whole search.
        private async Task<T> Safe<T>(string source, Task<T> task, T fallback)
        {
            try { return await task; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Global search: {Source} failed.", source);
                return fallback;
            }
        }

        private Task<List<Hit>> Safe(string source, Task<List<Hit>> task) => Safe(source, task, new List<Hit>());

        private static bool Has(string? text, string q) => text?.Contains(q, StringComparison.OrdinalIgnoreCase) == true;
        private static string Digits(string? text) => new((text ?? "").Where(char.IsDigit).ToArray());

        /// <summary>Pages: the entries of the user's own menu that open a page.</summary>
        private async Task<List<Hit>> Pages(string q)
        {
            var userId = User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(userId)) return new();
            var menu = await _menus.GetMenuByUserId(userId);
            return menu
                .Where(m => !string.IsNullOrWhiteSpace(m.Controller) && !menu.Any(c => c.ParentMenuId == m.Id) && Has(m.MenuName, q))
                .OrderBy(m => m.MenuName!.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(m => m.MenuName)
                .Take(PerGroup)
                .Select(m => new Hit(m.MenuName!.Trim(), menu.FirstOrDefault(p => p.Id == m.ParentMenuId)?.MenuName?.Trim(),
                    Url.Action(MenuLinks.ActionFor(m.Controller, m.MenuName), m.Controller!.Trim()) ?? "/", "bx " + (m.Icon ?? "bx-file").Replace("bx ", "")))
                .ToList();
        }

        /// <summary>Employers the user may see, by name or EIN; choosing one makes it the selected employer.</summary>
        private async Task<List<Hit>> Employers(string q)
        {
            var digits = Digits(q);
            int.TryParse(GetCurrentFilingYear(), out var year);
            var returnUrl = Request.Headers.Referer.FirstOrDefault() is { } referer && Uri.TryCreate(referer, UriKind.Absolute, out var from)
                ? from.PathAndQuery : "/";
            return (await GetPermittedEmployersAsync())
                .Where(e => Has(e.Name, q) || (digits.Length >= 3 && (Digits(e.TaxId).Contains(digits) || Digits(e.EIN).Contains(digits))))
                .OrderBy(e => e.FilingYear == year.ToString() ? 0 : 1)
                .ThenBy(e => e.Name!.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(e => e.Name)
                .Take(PerGroup)
                .Select(e => new Hit(e.Name ?? "", $"EIN {e.TaxId ?? e.EIN} · {e.FilingYear}",
                    Url.Action("SelectThis", "SelectEmployer", new { id = e.Id, empname = e.Name, returnUrl }) ?? "/", "bx bx-buildings"))
                .ToList();
        }

        /// <summary>Employees of the selected employer and its affiliates, by name or the last four SSN digits.</summary>
        private async Task<(List<Hit>, string?)> Employees(string q)
        {
            if (!int.TryParse(GetCurrentEmployerId(), out var employerId) || employerId <= 0)
                return (new(), "Select an employer to search its employees.");
            int.TryParse(GetCurrentFilingYear(), out var year);
            using var db = new SqlConnection(_connectionString);
            var rows = await db.QueryAsync<(int EmployeeId, string Name, string? SsnLast4, string? EmployerName, DateTime? TerminationDate)>(
                "dbo.sp_GlobalSearch_Employees", new { EmployerId = employerId, FilingYear = year, Query = q, Top = PerGroup },
                commandType: CommandType.StoredProcedure);
            return (rows.Select(r => new Hit(r.Name, $"SSN ···{r.SsnLast4} · {r.EmployerName}" + (r.TerminationDate != null ? " · left" : ""),
                Url.Action("Index", "Employee", new { focusId = r.EmployeeId }) ?? "/", "bx bx-user")).ToList(), null);
        }

        /// <summary>Uploaded files the File Upload list shows this user.</summary>
        private async Task<List<Hit>> Files(string q)
        {
            var result = await _files.SearchPaginatedAsync(q, PerGroup, 1, GetCurrentUserId(), "UploadedAt", "DESC", "All", null, null, false);
            return (result.Logs ?? Enumerable.Empty<ACA360.Core.Models.UploadedFileLog>())
                .Take(PerGroup)
                .Select(f => new Hit(f.OriginalFileName ?? $"File #{f.FileLogId}", $"#{f.FileLogId} · {f.EmployerName} · {f.UploadedAt:MM/dd/yyyy}",
                    Url.Action("Index", "FileUpload", new { searchTerm = f.OriginalFileName }) ?? "/", "bx bx-file"))
                .ToList();
        }
    }
}
