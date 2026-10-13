using ACA360.BusinessLogic.Services;
using ACA360.Core.Constants;
using ACA360.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ACA360.Web.Controllers
{
    /// <summary>
    /// Reports > Duplicate SSNs (script 066): SSNs found under more than one employer, more than
    /// once under one employer, or with different names or birth dates, for a tax year. Only the
    /// employers the user may see; the SSN is shown by its last four digits.
    /// </summary>
    [Authorize(Roles = UserRoles.FileHandlers)]
    public class DuplicateSsnController : BaseController
    {
        private readonly IDataQualityService _dataQuality;
        private readonly ILogger<DuplicateSsnController> _logger;

        public DuplicateSsnController(IDataQualityService dataQuality, ILogger<DuplicateSsnController> logger)
        {
            _dataQuality = dataQuality;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int? year)
        {
            var selected = year ?? (int.TryParse(GetCurrentFilingYear(), out var y) && y > 0 ? y : DateTime.Today.Year);
            ViewBag.Year = selected;
            ViewBag.Years = (await GetAllFilingYearsAsync()).Select(f => f.Year).Where(v => v > 0).Distinct().OrderByDescending(v => v).ToList();
            try
            {
                var rows = await _dataQuality.DuplicateSsnsAsync(GetEffectiveScopeUserId(), selected);
                // The procedure scopes by user already; this keeps View As honest as well.
                var permitted = await GetPermittedEmployerIdsAsync();
                var hidden = rows.Where(r => !permitted.Contains(r.EmployerId)).Select(r => r.GroupNo).ToHashSet();
                return View(rows.Where(r => !hidden.Contains(r.GroupNo)).ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Duplicate SSN report failed for {Year}.", selected);
                ViewBag.Error = "The report could not be loaded.";
                return View(new List<ACA360.Core.Models.DuplicateSsnRow>());
            }
        }
    }
}
