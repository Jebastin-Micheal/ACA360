using ACA360.BusinessLogic.Interfaces;
using ACA360.BusinessLogic.Services;
using ACA360.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;

namespace ACA360.Web.Controllers
{
    [Authorize]
    public class CalendarController : Controller
    {
        private readonly ICalendarService _calendarService;
        private readonly ILogger<CalendarController> _logger;
        private readonly IFilingDeadlineService _deadlines;

        // Injected the ILogger into the constructor
        public CalendarController(ICalendarService calendarService, ILogger<CalendarController> logger, IFilingDeadlineService deadlines)
        {
            _calendarService = calendarService;
            _deadlines = deadlines;
            _logger = logger;
        }

        // GET: /Calendar
        public IActionResult Index()
        {
            try
            {
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while loading the Calendar Index page.");
                return View("Error");
            }
        }

        // GET: /Calendar/GetEvents -> JSON for FullCalendar
        [HttpGet]
        public async Task<IActionResult> GetEvents()
        {
            try
            {
                long userId = GetLoggedInUserId();
                var events = _calendarService.GetEventsByUser(userId);

                // FullCalendar expects: id, title, start, end, allDay, color
                var result = events.Select(e => new
                {
                    id = e.Id,
                    title = e.Title,
                    start = e.StartDate,
                    end = e.EndDate,
                    allDay = e.IsAllDay,
                    color = e.ColorCode,
                    extendedProps = new
                    {
                        description = e.Description,
                        eventUrl = e.EventUrl,
                        location = e.Location,
                        guests = e.Guests
                    }
                }).Cast<object>().ToList();

                // Filing deadlines (script 063): everyone sees them, nobody edits them here.
                try
                {
                    foreach (var d in await _deadlines.ListAsync())
                        result.Add(new
                        {
                            id = "deadline-" + d.FilingDeadlineId,
                            title = d.Jurisdiction + ": " + d.Title,
                            start = d.DueDate.ToString("yyyy-MM-dd"),
                            allDay = true,
                            color = "#ff3e1d",                 // the IRS Filing Deadlines label
                            editable = false,
                            extendedProps = new { readOnly = true, description = d.Notes, filingYear = d.FilingYear, needsCheck = d.NeedsCheck }
                        });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Filing deadlines could not be added to the calendar.");
                }

                return Json(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching calendar events.");
                return Json(new { success = false, message = "Failed to load calendar events." });
            }
        }

        // POST: /Calendar/AddEvent
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddEvent([FromBody] CalendarEventModel model)
        {
            try
            {
                if (model == null)
                {
                    return Json(new { success = false, message = "Event data cannot be empty." });
                }

                long userId = GetLoggedInUserId();
                var newId = _calendarService.AddEvent(userId, model);
                return Json(new { success = true, id = newId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while adding a new calendar event.");
                return Json(new { success = false, message = "Failed to add the event. Please try again." });
            }
        }

        // POST: /Calendar/UpdateEvent
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateEvent([FromBody] CalendarEventModel model)
        {
            try
            {
                if (model == null)
                {
                    return Json(new { success = false, message = "Event data cannot be empty." });
                }

                long userId = GetLoggedInUserId();
                _calendarService.UpdateEvent(userId, model);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while updating the calendar event.");
                return Json(new { success = false, message = "Failed to update the event." });
            }
        }

        // POST: /Calendar/DeleteEvent
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteEvent(int id)
        {
            try
            {
                long userId = GetLoggedInUserId();
                _calendarService.DeleteEvent(userId, id);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while deleting the calendar event with ID: {EventId}", id);
                return Json(new { success = false, message = "Failed to delete the event." });
            }
        }

        private long GetLoggedInUserId()
        {
            try
            {
                var claim = User?.FindFirst("UserId");

                if (claim != null && long.TryParse(claim.Value, out long userId))
                {
                    return userId;
                }

                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while getting the logged-in User ID.");
                return 0;
            }
        }
    }
}