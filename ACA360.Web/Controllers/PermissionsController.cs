using ACA360.Core.Attributes;
using ACA360.Core.Constants;
using ACA360.Core.Extensions;
using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using ACA360.Security.Interfaces;
using ACA360.Repositories.Interfaces;
using ACA360.Repositories;
using ACA360.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector + "," + UserRoles.DASupervisor + "," + UserRoles.DASupervisor + "," + UserRoles.AMSupervisor + "," + UserRoles.Broker + "," + UserRoles.DataAnalyst + "," + UserRoles.Employer + "," + UserRoles.StaffAC + "," + UserRoles.StaffAU + "," + UserRoles.AccountManager)]
    [Route("[controller]")]
    public class PermissionsController : Controller
    {
        private readonly IPermissionService _permissionService; // Service handling all permission business logic
        private readonly ILoggerService _logger;                // Custom domain logger for structured audit/error logging

        /// <summary>
        /// Initializes the PermissionsController with the permission service and domain logger.
        /// </summary>
        /// <param name="permissionService">Service responsible for reading and updating role-based permissions.</param>
        /// <param name="logger">Domain logger for capturing structured errors with caller and IP context.</param>
        public PermissionsController(
            IPermissionService permissionService,
            ILoggerService logger)
        {
            _permissionService = permissionService;
            _logger = logger;
        }

        /// <summary>
        /// Retrieves all permissions assigned to a given role and renders them as a partial view.
        /// Typically called via AJAX when a role is selected on the Role management screen.
        /// GET: /Permissions/GetByRole?roleId={roleId}
        /// </summary>
        /// <param name="roleId">The unique identifier of the role whose permissions are to be displayed.</param>
        /// <returns>
        /// A "Permissions" partial view populated with the role's current permission set,
        /// or an "Error" partial view if the lookup fails.
        /// </returns>
        [HttpGet("GetByRole")]
        public async Task<IActionResult> GetByRole(int roleId)
        {
            try
            {
                // Fetch the full permission set for the specified role from the service layer
                var result = await _permissionService.GetPermissionsByRoleAsync(roleId);

                return PartialView("Permissions", result);
            }
            catch (Exception ex)
            {
                // Log the failure with caller context, role ID, and client IP for audit traceability
                _logger.LogError(
                    ex,
                    nameof(GetByRole),
                    nameof(PermissionsController),
                    $"Failed to get permissions for role {roleId}",
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                // Return a graceful error partial view rather than surfacing a raw exception to the UI
                return PartialView("Error");
            }
        }

        /// <summary>
        /// Persists the updated permission selections for a given role.
        /// Accepts a dictionary of menu IDs mapped to their selected action IDs,
        /// converts them into a flat permission list, and delegates to the service layer.
        ///
        /// NOTE: Only explicitly enabled (checked) permissions are submitted in the dictionary.
        /// The underlying stored procedure is expected to handle a full replace of the role's permissions
        /// based on this "enabled only" list — i.e., any previously granted permissions not present
        /// in the new list will be revoked.
        ///
        /// POST: /Permissions/update/{roleId}
        /// </summary>
        /// <param name="roleId">The unique identifier of the role whose permissions are being updated.</param>
        /// <param name="SelectedActions">
        /// A dictionary keyed by MenuId, where each value is an array of ActionIds the user has checked.
        /// Only checked (enabled) entries are included — unchecked permissions are implicitly revoked.
        /// </param>
        /// <returns>
        /// Redirects to the Role Index page on success,
        /// or renders an "Error" view if the update operation fails.
        /// </returns>
        [HttpPost("update/{roleId}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(int roleId, Dictionary<int, int[]> SelectedActions)
        {
            try
            {
                // Step 1: Initialise the flat permissions list that the service layer expects
                var permissionsList = new List<PermissionViewModel>();

                // Step 2: Flatten the submitted dictionary into individual permission entries
                // Each dictionary key is a MenuId; its value is the array of ActionIds checked by the user
                foreach (var menuId in SelectedActions.Keys)
                {
                    var actionIds = SelectedActions[menuId];

                    if (actionIds != null)
                    {
                        foreach (var actionId in actionIds)
                        {
                            // Each entry in the list represents a single enabled menu-action permission
                            permissionsList.Add(new PermissionViewModel
                            {
                                MenuId = menuId,
                                ActionId = actionId,
                                IsEnabled = true
                            });
                        }
                    }
                }

                // Step 3: Resolve the modifying user's identity for audit trail purposes
                // Falls back to "System" if the claim is unavailable (e.g., service account context)
                var modifiedBy = User.FindFirstValue(ClaimTypes.Name) ?? "System";

                // Step 4: Persist the updated permission set via the service layer
                // The SP performs a full replace — only the permissions in this list will remain active
                await _permissionService.UpdatePermissionsAsync(roleId, permissionsList, modifiedBy);

                return RedirectToAction("Index", "Role");
            }
            catch (Exception ex)
            {
                // Log the failure with caller context, role ID, and client IP for audit traceability
                _logger.LogError(
                    ex,
                    nameof(Update),
                    nameof(PermissionsController),
                    $"Failed to update permissions for role {roleId}",
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                // Render a full error view since this is a POST action, not an AJAX partial request
                return View("Error");
            }
        }

        //[HttpGet("menus")]
        //public async Task<IActionResult> GetMenusWithActions()
        //{
        //    try
        //    {
        //        var menus = await _permissionService.GetMenuWithActionsAsync();
        //        return View("MenuActions", menus);
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, nameof(GetMenusWithActions), nameof(PermissionsController),
        //            "Failed to get menus with actions", HttpContext.Connection.RemoteIpAddress?.ToString());
        //        return View("Error");
        //    }
        //}
    }
}