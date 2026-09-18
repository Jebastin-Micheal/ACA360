using ACA360.Core.Constants;
using ACA360.Core.Models.Tracker;
using ACA360.Core.ViewModels;
using ACA360.Logging.Interfaces;
using ACA360.Repositories.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector + "," + UserRoles.DASupervisor + "," + UserRoles.AMSupervisor + "," + UserRoles.Broker + "," + UserRoles.DataAnalyst + "," + UserRoles.Employer + "," + UserRoles.StaffAC + "," + UserRoles.StaffAU + "," + UserRoles.AccountManager)]
    public class SalesRepController : Controller
    {
        private readonly ISalesRepRepository _repo;
        private readonly ILoggerService _logger;

        public SalesRepController(ISalesRepRepository repo, ILoggerService logger)
        {
            _repo = repo;
            _logger = logger;
        }

        public IActionResult Index()
        {
            try
            {
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Index), nameof(SalesRepController), "An error occurred while loading index view.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetSalesRepList(string search, string sortColumn = "SalesRepName",
string sortOrder = "ASC", int pageIndex = 1, int pageSize = 10)
        {
            try
            {
                var data = await _repo.GetSalesRepListAsync(search, sortColumn, sortOrder, pageIndex, pageSize);

                int totalPages = (int)Math.Ceiling((double)data.TotalCount / pageSize);

                var model = new SalesRepListViewModel
                {
                    SalesReps = data.List,
                    SortColumn = sortColumn,
                    SortOrder = sortOrder,
                    Metadata = new ACA360.Core.Models.PaginationViewEntity
                    {
                        TotalItems = data.TotalCount,
                        CurrentPage = pageIndex,
                        PageSize = pageSize,
                        TotalPages = totalPages,
                        TotalCount = data.TotalCount,
                        PageNumber = pageIndex
                    }
                };

                return PartialView("_SalesRepListPartial", model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetSalesRepList), nameof(SalesRepController), "An error occurred fetching sales rep list.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
            }
        }

        [HttpGet]
        public async Task<IActionResult> AddUpdate(int id)
        {
            try
            {
                if (id == 0)
                {
                    return PartialView("_AddUpdateSalesRep", new SalesRepItem { IsActive = true });
                }

                var salesRep = await _repo.GetSalesRepByIdAsync(id);
                if (salesRep == null)
                {
                    return NotFound("The requested record could not be found.");
                }

                return PartialView("_AddUpdateSalesRep", salesRep);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(AddUpdate), nameof(SalesRepController), $"An error occurred loading form for ID {id}.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(SalesRepItem model)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { success = false, message = "Invalid data submitted." });
                }

                await _repo.AddUpdateSalesRepAsync(model);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Save), nameof(SalesRepController), "An error occurred while saving.");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred while saving." });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                if (id <= 0)
                {
                    return BadRequest(new { success = false, message = "A valid ID is required." });
                }

                await _repo.DeleteSalesRepAsync(id);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Delete), nameof(SalesRepController), $"An error occurred while deleting ID {id}.");
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An unexpected error occurred." });
            }
        }
    }
}