using ACA360.BusinessLogic.Services;
using ACA360.Core.Constants;
using ACA360.Core.Models;
using ACA360.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ACA360.Web.Controllers
{
    /// <summary>
    /// Reports > State Filings (script 072): for CA, NJ, RI, DC and MA, the residents of each employer
    /// the user may see, who had coverage, and who had it from a self-insured plan; the state's
    /// deadlines; an Excel workbook per employer and state. No state e-file files.
    /// </summary>
    [Authorize(Roles = UserRoles.FileHandlers)]
    public class StateFilingsController : BaseController
    {
        private readonly IStateFilingService _states;
        private readonly IFilingDeadlineService _deadlines;
        private readonly ILogger<StateFilingsController> _logger;

        public StateFilingsController(IStateFilingService states, IFilingDeadlineService deadlines, ILogger<StateFilingsController> logger)
        {
            _states = states;
            _deadlines = deadlines;
            _logger = logger;
        }

        private int YearOr(int? year) => year ?? (int.TryParse(GetCurrentFilingYear(), out var y) && y > 0 ? y : DateTime.Today.Year);

        [HttpGet]
        public async Task<IActionResult> Index(int? year)
        {
            var selected = YearOr(year);
            ViewBag.Year = selected;
            ViewBag.Years = (await GetAllFilingYearsAsync()).Select(f => f.Year).Where(v => v > 0).Distinct().OrderByDescending(v => v).ToList();
            ViewBag.Deadlines = (await _deadlines.ListAsync(selected)).Where(d => d.Jurisdiction != "IRS").OrderBy(d => d.DueDate).ToList();
            try
            {
                var permitted = await GetPermittedEmployerIdsAsync();
                var rows = (await _states.SummaryAsync(GetEffectiveScopeUserId(), selected)).Where(r => permitted.Contains(r.EmployerId)).ToList();
                return View(rows);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "State filing summary failed for {Year}.", selected);
                ViewBag.Error = "The report could not be loaded.";
                return View(new List<StateFilingSummaryRow>());
            }
        }

        /// <summary>One employer's residents of one state, with months covered and dependents.</summary>
        [HttpGet]
        public async Task<IActionResult> Residents(long employerId, int? year, string state)
        {
            state = (state ?? "").Trim().ToUpperInvariant();
            if (employerId <= 0 || !StateFilingStates.IsState(state)) return BadRequest("Choose an employer and a state.");
            if (!await IsEmployerPermittedAsync(employerId)) return Forbid();
            var selected = YearOr(year);
            var (residents, dependents) = await _states.ResidentsAsync(employerId, selected, state);
            ViewBag.Year = selected;
            ViewBag.State = state;
            ViewBag.EmployerId = employerId;
            ViewBag.Dependents = dependents;
            ViewBag.FullSsn = User.CanViewFullPii();
            return PartialView("_Residents", residents);
        }

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Export(long employerId, int? year, string state, [FromServices] ACA360.BusinessLogic.Interfaces.IEmployerService employers)
        {
            state = (state ?? "").Trim().ToUpperInvariant();
            if (employerId <= 0 || !StateFilingStates.IsState(state)) return BadRequest("Choose an employer and a state.");
            if (!await IsEmployerPermittedAsync(employerId)) return Forbid();
            var selected = YearOr(year);
            try
            {
                var name = await employers.GetEmployerNameAsync((int)employerId);
                var bytes = await _states.ExportAsync(employerId, name, selected, state, User.CanViewFullPii());
                var safe = new string((name ?? "Employer").Where(ch => char.IsLetterOrDigit(ch) || ch == ' ').ToArray()).Trim().Replace(' ', '-');
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{state}-residents-{safe}-{selected}.xlsx");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "State filing export failed for employer {EmployerId}, {State}, {Year}.", employerId, state, selected);
                return StatusCode(500, "The workbook could not be created.");
            }
        }
    }
}
