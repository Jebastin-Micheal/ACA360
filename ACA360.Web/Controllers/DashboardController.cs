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
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using ACA360.BusinessLogic.Interfaces;

namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector + "," + UserRoles.DASupervisor + "," + UserRoles.DASupervisor + "," + UserRoles.AMSupervisor + "," + UserRoles.Broker + "," + UserRoles.DataAnalyst + "," + UserRoles.Employer + "," + UserRoles.StaffAC + "," + UserRoles.StaffAU + "," + UserRoles.AccountManager)]
    public class DashboardController : BaseController
    {
        private readonly IDashboardService _dashboardService;
        private readonly IEmployerService _employerService;
        private readonly IFileUploadLogService _fileService;
        private readonly IACALogicService _acaLogicService;
        private readonly IIrsXmlService _irsXmlService;
        private readonly ILoggerService _logger;

        public DashboardController(
            IDashboardService dashboardService,
            IEmployerService employerService,
            IFileUploadLogService fileService,
            IACALogicService acaLogicService,
            IIrsXmlService irsXmlService,
            ILoggerService logger)
        {
            _dashboardService = dashboardService;
            _employerService = employerService;
            _fileService = fileService;
            _acaLogicService = acaLogicService;
            _irsXmlService = irsXmlService;
            _logger = logger;
        }

        /// <summary>
        /// Retrieves and constructs the dashboard view for the currently authenticated user.
        /// Aggregates both common statistics and role-specific data into a unified view model.
        /// </summary>
        /// <returns>An IActionResult rendering the dashboard view with the populated data model.</returns>
        public async Task<IActionResult> Index()
        {
            try
            {
                // Extract the user's role and unique identifier from claims context
                var role = User.FindFirstValue(ClaimTypes.Role);
                var ProfileName = User.FindFirst("Profile_Name")?.Value ?? "Guest";
                var userId = GetCurrentUserId();

                // Initialize the base dashboard view model with user identity info
                var model = new DashboardViewModel
                {
                    UserRole = role,
                    UserName = User.Identity?.Name ?? string.Empty,
                    Profile_Name = ProfileName
                };

                // 1. Common Stats: Fetch and assign statistics applicable to all users
                var stats = await _dashboardService.GetDashboardStatsAsync(userId);
                model.TotalEmployers = stats.TotalEmployers;

                // 2. Role-Specific Logic: Execute specialized queries based on the user's role authorization
                switch (role)
                {
                    case UserRoles.SuperAdmin:
                    case UserRoles.Admin:
                        // Fetch elevated stats for administrators encompassing all system activity
                        model.SuperAdminData = await _dashboardService.GetAdvancedSuperAdminStatsAsync();
                        model.TotalEmployers = model.SuperAdminData.TotalEmployers;
                        break;

                    case UserRoles.ACADirector:
                        // Fetch department-level aggregates for ACA Directors
                        model.DirectorData = await _dashboardService.GetDirectorStatsAsync(userId);
                        break;

                    case UserRoles.DataAnalyst:
                        // Retrieve file queue and pending validations specifically assigned to the analyst
                        model.FilesInQueue = stats.FilesInQueue;
                        model.PendingValidations = stats.PendingValidations;
                        model.AnalystData = await _dashboardService.GetDataAnalystStatsAsync(userId);
                        model.DataAnalystQueue = await _fileService.GetPendingFilesForUserAsync(userId);
                        break;

                    case UserRoles.DASupervisor:
                    case UserRoles.AMSupervisor:
                        // Fetch oversight metrics for supervisory roles
                        model.SupervisorData = await _dashboardService.GetSupervisorStatsAsync(userId, role);
                        break;

                    case UserRoles.AccountManager:
                        // Compute portfolio health and risks for Account Managers
                        model.TotalEmployers = stats.TotalEmployers;
                        model.MissingDataClients = stats.MissingDataClients;
                        model.AmPortfolio = await _employerService.GetAmClientPortfolioAsync(userId);

                        // Calculate specific compliance stages based on status IDs
                        model.ClientsMissingData = model.AmPortfolio.Count(x => x.ComplianceStatusId == 2);
                        model.ClientsOnTrack = model.AmPortfolio.Count(x => x.ComplianceStatusId == 1);
                        model.Stage_DataCollection = model.TotalEmployers - model.ClientsOnTrack;

                        // Aggregate risk scoring across the assigned portfolio
                        model.TotalPortfolioRisk = await _acaLogicService.GetPortfolioRiskAsync(userId, DateTime.Now.Year);

                        // Fetch IRS submission metrics and recent rejection details
                        var amSubmissions = await _irsXmlService.GetSubmissionsForAmAsync(userId, DateTime.Now.Year);
                        model.AmAcceptedCount = amSubmissions.Count(x => x.StatusId == 2);
                        model.AmRejectedCount = amSubmissions.Count(x => x.StatusId == 4);
                        model.AmRecentRejections = amSubmissions.Where(x => x.StatusId == 4)
                            .OrderByDescending(x => x.GeneratedDate).Take(5)
                            .Select(x => new IrsRejectionDto { EmployerName = x.EmployerName, StatusMessage = x.StatusMessage, GeneratedDate = x.GeneratedDate, SubmissionId = x.SubmissionId }).ToList();
                        break;

                    case UserRoles.StaffAC:
                        // Fetch statistics relevant to accounting staff
                        model.AccountingData = await _dashboardService.GetAccountingStatsAsync();
                        break;

                    case UserRoles.StaffAU:
                        // Fetch statistics relevant to audit staff
                        model.AuditData = await _dashboardService.GetAuditStatsAsync();
                        break;

                    case UserRoles.Employer:
                        // Fetch specific employer data if the user acts as a direct employer
                        var employerId = GetCurrentEmployerId();
                        if (int.TryParse(employerId, out int eid))
                        {
                            model.Employer360 = await _employerService.GetEmployer360Async(eid, DateTime.Now.Year);
                        }
                        break;

                    case UserRoles.Broker:
                        // Fetch high-level portfolio overview for brokers
                        model.TotalEmployers = stats.TotalEmployers;
                        model.MissingDataClients = stats.MissingDataClients;
                        model.AmPortfolio = await _employerService.GetBrokerClientPortfolioAsync(userId);
                        break;
                }

                // Render the finalized dashboard view
                return View(model);
            }
            catch (Exception ex)
            {
                // Log the exception including contextual user details for easier troubleshooting
                _logger.LogError(ex, nameof(Index), "DashboardController", "An error occurred while DashboardController", $"An error occurred while loading the dashboard for user {User?.Identity?.Name} with role {User?.FindFirstValue(ClaimTypes.Role)}.");
                // Return a generic 500 error code to avoid exposing sensitive stack traces to the end user
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading the dashboard.");
            }
        }
    }
}