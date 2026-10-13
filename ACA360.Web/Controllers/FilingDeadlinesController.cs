using ACA360.BusinessLogic.Services;
using ACA360.Core.Constants;
using ACA360.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ACA360.Web.Controllers
{
    /// <summary>
    /// Administration > Filing Deadlines (script 063): the IRS and state dates the dashboards count
    /// down to and the Calendar shows. Seeded state dates are marked "check" until saved here.
    /// </summary>
    [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin)]
    public class FilingDeadlinesController : Controller
    {
        private readonly IFilingDeadlineService _deadlines;
        private readonly ILogger<FilingDeadlinesController> _logger;

        public FilingDeadlinesController(IFilingDeadlineService deadlines, ILogger<FilingDeadlinesController> logger)
        {
            _deadlines = deadlines;
            _logger = logger;
        }

        private string? Who() => User.Identity?.Name;

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var all = await _deadlines.ListAsync();
            return View(all);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(FilingDeadline deadline)
        {
            if (!FilingDeadline.Kinds.Contains(deadline.Kind)) ModelState.AddModelError(nameof(deadline.Kind), "Choose a kind.");
            if (deadline.DueDate.Year < 2015) ModelState.AddModelError(nameof(deadline.DueDate), "Enter the due date.");
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "The deadline was not saved: " + string.Join(" ",
                    ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).Where(m => m.Length > 0).Distinct());
                return RedirectToAction(nameof(Index));
            }
            try
            {
                var id = await _deadlines.SaveAsync(deadline, Who());
                TempData[id > 0 ? "Success" : "Error"] = id > 0 ? $"Saved: {deadline.Title} ({deadline.DueDate:MM/dd/yyyy})." : "That deadline no longer exists.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Saving filing deadline {Id} failed.", deadline.FilingDeadlineId);
                TempData["Error"] = "The deadline could not be saved.";
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                if (await _deadlines.DeleteAsync(id, Who())) TempData["Success"] = "Deadline removed.";
                else TempData["Error"] = "That deadline no longer exists.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Removing filing deadline {Id} failed.", id);
                TempData["Error"] = "The deadline could not be removed.";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}
