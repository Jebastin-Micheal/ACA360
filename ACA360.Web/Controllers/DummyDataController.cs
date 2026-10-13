using System.Text;
using System.Text.Json;
using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Constants;
using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using ACA360.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ACA360.Web.Controllers
{
    /// <summary>
    /// Administration > Dummy Data (SuperAdmin only): made-up import workbooks for testing uploads,
    /// with clean rows, rows with one warning and rows with one error, in the shares asked for.
    /// Reads employers; writes nothing.
    /// </summary>
    [Authorize(Roles = UserRoles.SuperAdmin)]
    public class DummyDataController : Controller
    {
        public const string SummaryHeader = "X-Dummy-Data-Summary";

        private readonly IDummyDataService _dummyData;
        private readonly IWebHostEnvironment _env;
        private readonly ILoggerService _logger;

        public DummyDataController(IDummyDataService dummyData, IWebHostEnvironment env, ILoggerService logger)
        {
            _dummyData = dummyData;
            _env = env;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var years = await _dummyData.GetFilingYearsAsync();
            // The year chosen in the top bar, when System Settings has it.
            // (Stored as text or as a number, as BaseController.GetCurrentFilingYear reads it.)
            int? selected = int.TryParse(HttpContext.Session.GetString("SelectedFilingYear"), out var text) ? text
                            : HttpContext.Session.GetInt32("SelectedFilingYear");
            int year = selected is int s && years.Contains(s) ? s : years.Count > 0 ? years[0] : DateTime.Now.Year - 1;
            if (!years.Contains(year)) years.Insert(0, year);
            return View(new DummyDataViewModel
            {
                Employers = await _dummyData.GetMainEmployersAsync(),
                Years = years,
                Defaults = new DummyDataRequest { FilingYear = year },
            });
        }

        /// <summary>The workbook (or a zip of them); what each holds is in the X-Dummy-Data-Summary header (base64 JSON).</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Generate([FromForm] DummyDataRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { success = false, message = "Check the numbers: " +
                    string.Join(" ", ModelState.Where(m => m.Value?.Errors.Count > 0).Select(m => m.Key)) + "." });
            try
            {
                var template = Path.Combine(_env.WebRootPath, "templates", "ACA_General_Import_Template.xlsx");
                var (content, fileName, contentType, files) = await _dummyData.GenerateAsync(request, template);
                Response.Headers[SummaryHeader] = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(files, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
                return File(content, contentType, fileName);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Generate), nameof(DummyDataController), "Generating dummy data");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "The file could not be generated." });
            }
        }
    }
}
