using ACA360.Core.Constants;
using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using ACA360.Repositories.Interfaces;
using ACA360.Repositories;
using ACA360.Web.Helpers;
using ACA360.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ACA360.BusinessLogic.Interfaces;
using System.Linq;
using ACA360.Web.Filters;

namespace ACA360.Web.Controllers
{
    // Portfolio assignment decides which staff member can see which clients, so it
    // belongs to the roles that manage staff — not to everyone who can log in.
    //
    // This previously carried the same "every role" list as the read-only screens,
    // which meant an Employer or Broker login could reach SaveAssignments and
    // reassign portfolios. The employer ids are separately scoped by
    // [RequireEmployerScope] on the actions that accept them.
    [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," +
                       UserRoles.ACADirector + "," + UserRoles.DASupervisor + "," +
                       UserRoles.AMSupervisor)]
    public class AssignmentController : BaseController
    {
        private readonly IAssignmentService _assignmentRepository;
        private readonly ILoggerService _logger;
        private readonly IEmployerService _employerService;

        public AssignmentController(IAssignmentService assignmentRepository, ILoggerService logger, IEmployerService employerService)
        {
            _assignmentRepository = assignmentRepository;
            _logger = logger;
            _employerService = employerService;
        }

        /// <summary>
        /// Retrieves the assignment management view for a specific staff member based on their role.
        /// Fetches all available employers and the currently assigned employers to build the assignment UI.
        /// </summary>
        /// <param name="id">The unique identifier of the staff member.</param>
        /// <param name="roleId">The role identifier determining the type of staff (e.g., Data Analyst, Account Manager, Broker).</param>
        /// <returns>A partial view containing the assignment management UI and the populated data model.</returns>
        public async Task<IActionResult> ManageAssignments(int id, int roleId)
        {
            try
            {
                // Initialize the view model to hold assignment data
                EmployerAssignmentViewModel model;

                // Retrieve the current user's ID to fetch contextual employer data
                string UserId = GetCurrentUserId();

                // Branch logic based on the specific staff role to fetch appropriate entities
                if (roleId == 11 || roleId == 5) // Data Analyst
                {
                    // Fetch Data Analyst details, all contextual employers, and their specific assignments
                    var dataAnalyst = await _assignmentRepository.GetDataAnalystByIdAsync(id);
                    if (dataAnalyst == null)
                    {
                        return NotFound($"DataAnalyst with ID {id} not found.");
                    }
                    var allEmployers = await _employerService.GetAllEmployersAsync(UserId);
                    var assignedEmployers = await _assignmentRepository.GetEmployersByDataAnalystIdAsync(id, roleId);

                    model = new EmployerAssignmentViewModel
                    {
                        StaffMember = new StaffMember
                        {
                            Id = dataAnalyst.Id,
                            AM_name = dataAnalyst.AM_name
                        },
                        StaffType_ID = roleId,
                        AllEmployers = allEmployers,
                        AssignedEmployers = assignedEmployers
                    };
                }
                else if (roleId == 16 || roleId == 6) // Account Manager
                {
                    // Fetch Account Manager details, all contextual employers, and their specific assignments
                    var accountManager = await _assignmentRepository.GetAccountManagerByIdAsync(id);
                    if (accountManager == null)
                    {
                        return NotFound($"AccountManager with ID {id} not found.");
                    }
                    var allEmployers = await _employerService.GetAllEmployersAsync(UserId);
                    var assignedEmployers = await _assignmentRepository.GetEmployersByAccountManagerIdAsync(id, roleId);

                    model = new EmployerAssignmentViewModel
                    {
                        StaffMember = new StaffMember
                        {
                            Id = accountManager.Id,
                            AM_name = accountManager.AM_name
                        },
                        StaffType_ID = roleId,
                        AllEmployers = allEmployers,
                        AssignedEmployers = assignedEmployers
                    };
                }
                else // Broker
                {
                    var broker = await _assignmentRepository.GetBrokerByIdAsync(id);
                    if (broker == null)
                    {
                        return NotFound($"Broker with ID {id} not found.");
                    }
                    // Fetch Broker details, all contextual employers, and their specific assignments
                    var allEmployers = await _employerService.GetAllEmployersAsync(UserId);
                    var assignedEmployers = await _assignmentRepository.GetEmployersByBrokerIdAsync(id, roleId);

                    model = new EmployerAssignmentViewModel
                    {
                        StaffMember = new StaffMember
                        {
                            Id = broker.Id,
                            AM_name = broker.AM_name
                        },
                        StaffType_ID = roleId,
                        AllEmployers = allEmployers,
                        AssignedEmployers = assignedEmployers
                    };
                }
                // The assigning page works with PRIMARY employers only: rows with a
                // companyId are affiliates of that primary and are not assignable here.
                model.AllEmployers = model.AllEmployers.Where(e => (e.CompanyId ?? 0) == 0).ToList();
                model.AssignedEmployers = model.AssignedEmployers.Where(e => (e.CompanyId ?? 0) == 0).ToList();
                // Return the generated model to the partial view
                return PartialView("ManageAssignments", model);
            }
            catch (Exception ex)
            {
                // Handle unexpected errors by logging the exception details
                // Return a generic 500 Internal Server Error to avoid leaking sensitive stack traces to the client
                _logger.LogError(ex, nameof(ManageAssignments), "AssignmentController", "An error occurred while ManageAssignments", id.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading assignments.");
            }
        }

        /// <summary>
        /// Saves the selected employer assignments for a specific staff member.
        /// Maps the request to the appropriate repository assignment method based on the staff type.
        /// </summary>
        /// <param name="staffId">The unique identifier of the staff member.</param>
        /// <param name="staffType">The role/type identifier as a string.</param>
        /// <param name="assignedEmployerIds">A list of employer IDs to be assigned to the staff member.</param>
        /// <returns>A JSON result indicating the success or failure of the save operation.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireEmployerScope("assignedEmployerIds")]
        public async Task<IActionResult> SaveAssignments(int staffId, string staffType, List<int> assignedEmployerIds)
        {
            try
            {
                // Input validation: Ensure staffType is not null or empty
                if (string.IsNullOrWhiteSpace(staffType))
                {
                    return BadRequest(new { success = false, message = "Staff type is required." });
                }

                // Get current admin ID (Placeholder for actual authentication system implementation)
                //int adminId = 0;
                int.TryParse(GetCurrentUserId(), out int adminId);

                // Route to the appropriate assignment mapping logic based on staff type
                if (staffType == "16" || staffType == "6") // Account Manager
                {
                    await _assignmentRepository.AssignEmployersToAccountManagerAsync(
                        staffId, assignedEmployerIds, adminId);
                }
                else if (staffType == "11" || staffType == "5") // Data Analyst
                {
                    await _assignmentRepository.AssignEmployersToDataAnalystAsync(
                        staffId, assignedEmployerIds, adminId);
                }
                else if (staffType == "10") // Broker
                {
                    await _assignmentRepository.AssignEmployersToBrokerAsync(
                       staffId, assignedEmployerIds, adminId);
                }
                else
                {
                    // Validation error: The provided staff type does not match any recognized mapping
                    return BadRequest(new { success = false, message = "Invalid staff type." });
                }

                // Return success response if the mapping completes without throwing
                return Json(new { success = true, message = "Assignments saved successfully" });
            }
            catch (Exception ex)
            {
                // Catch any database or execution exceptions during the save process
                // Log the issue and return a 500 Status Code with a generic JSON error payload
                _logger.LogError(ex, nameof(SaveAssignments), "AssignmentController", "$ An error occurred while saving assignments", staffId.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred while saving assignments." });
            }
        }

        /// <summary>
        /// Checks whether any of the employers being assigned already have an ACTIVE
        /// Account Manager / Data Analyst who is a different person, so the UI can warn
        /// before overriding ("this employer is assigned to X - proceed or cancel").
        /// </summary>
        /// <param name="staffId">The staff member the employers are being assigned to.</param>
        /// <param name="staffType">The role/type identifier (16/6 = AM, 11/5 = DA).</param>
        /// <param name="assignedEmployerIds">The employer IDs in the assigned list.</param>
        /// <returns>JSON with the conflicting employers and their current AM / DA names.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireEmployerScope("assignedEmployerIds")]
        public async Task<IActionResult> CheckAssignmentConflicts(int staffId, string staffType, List<int> assignedEmployerIds)
        {
            try
            {
                string side = staffType == "16" || staffType == "6" ? "AM"
                            : staffType == "11" || staffType == "5" ? "DA"
                            : "";

                // Brokers (or unknown types) have no single-owner rule - nothing to warn about
                if (side == "")
                {
                    return Json(new { conflicts = new List<AssignmentConflictItem>() });
                }

                var conflicts = await _assignmentRepository.GetAssignmentConflictsAsync(
                    assignedEmployerIds ?? new List<int>(), staffId, side);

                return Json(new { conflicts });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(CheckAssignmentConflicts), "AssignmentController", "An error occurred while checking assignment conflicts", staffId.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An error occurred while checking assignments." });
            }
        }

        /// <summary>
        /// Loads the assignment history popup for one employer.
        /// </summary>
        /// <param name="employerId">The unique identifier of the employer.</param>
        /// <param name="section">"AM" for Account Manager history only, "DA" for Data Analyst history only, empty for both.</param>
        /// <returns>A partial view listing the assignments with their dates.</returns>
        [RequireEmployerScope]
        public async Task<IActionResult> AssignmentHistory(int employerId, string? section = null)
        {
            try
            {
                // Affiliates inherit their Account Manager / Data Analyst (and therefore
                // the assignment history) from the parent company — the affiliate's own id
                // has no history. Resolve to the parent so the history actually shows.
                var parentId = await _employerService.GetPrimaryEmployerIdAsync(employerId.ToString());
                int effectiveId = parentId ?? employerId;

                var model = await _assignmentRepository.GetEmployerAssignmentHistoryAsync(effectiveId);
                if (model == null)
                {
                    return NotFound($"Employer with ID {employerId} not found.");
                }
                model.Section = section;
                return PartialView("_EmployerAssignmentHistory", model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(AssignmentHistory), "AssignmentController", "An error occurred while loading assignment history", employerId.ToString());
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading assignment history.");
            }
        }
    }
}
