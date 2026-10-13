using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Constants;
using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using ACA360.Web.Builders;
using ACA360.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ACA360.Web.Controllers
{
    /// <summary>
    /// The home page. Every role gets the client-specified dashboard (the portfolio band
    /// and employer detail shared with Employer/Dashboard), plus its own tiles above it.
    /// </summary>
    [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector + "," + UserRoles.DASupervisor + "," + UserRoles.AMSupervisor + "," + UserRoles.Broker + "," + UserRoles.DataAnalyst + "," + UserRoles.Employer + "," + UserRoles.StaffAC + "," + UserRoles.StaffAU + "," + UserRoles.AccountManager)]
    public class DashboardController : BaseController
    {
        private readonly IDashboardService _dashboardService;
        private readonly IIrsXmlService _irsXmlService;
        private readonly ILoggerService _logger;
        private readonly EmployerDashboardPageBuilder _dashboardPageBuilder;

        public DashboardController(
            IDashboardService dashboardService,
            IIrsXmlService irsXmlService,
            ILoggerService logger,
            EmployerDashboardPageBuilder dashboardPageBuilder)
        {
            _dashboardService = dashboardService;
            _irsXmlService = irsXmlService;
            _logger = logger;
            _dashboardPageBuilder = dashboardPageBuilder;
        }

        /// <summary>The role's own tiles, shown above the shared bands. Roles not listed have none.</summary>
        private static readonly Dictionary<string, string> RoleSections = new(StringComparer.OrdinalIgnoreCase)
        {
            [UserRoles.SuperAdmin] = "Dashboard/Roles/_AdminTiles",
            [UserRoles.Admin] = "Dashboard/Roles/_AdminTiles",
            [UserRoles.ACADirector] = "Dashboard/Roles/_DirectorTiles",
            [UserRoles.AMSupervisor] = "Dashboard/Roles/_AmSupervisorTiles",
            [UserRoles.AccountManager] = "Dashboard/Roles/_AccountManagerTiles",
            [UserRoles.DASupervisor] = "Dashboard/Roles/_DaSupervisorTiles",
            [UserRoles.DataAnalyst] = "Dashboard/Roles/_DataAnalystTiles"
        };

        public async Task<IActionResult> Index(int id = 0, int year = 0)
        {
            try
            {
                // Follows View As: an admin viewing as a broker or an employer gets that
                // user's dashboard, not their own.
                return await SharedDashboardAsync(CurrentContext, id, year);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(Index), nameof(DashboardController),
                    $"Failed to load the dashboard for user {User?.Identity?.Name} ({User?.FindFirstValue(ClaimTypes.Role)}), employer {id}, year {year}.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading the dashboard.");
            }
        }

        /// <summary>
        /// The client-specified dashboard, hosted on /Dashboard: the portfolio band and
        /// employer detail from Employer/Dashboard, plus the role's own tiles.
        /// </summary>
        private async Task<IActionResult> SharedDashboardAsync(EffectiveContext ctx, int id, int year)
        {
            if (year <= 0) int.TryParse(GetCurrentFilingYear(), out year);
            if (year <= 0) year = DateTime.Now.Year;

            var vm = await _dashboardPageBuilder.BuildAsync(ctx, id, year, GetCurrentEmployerId());
            if (vm == null)
            {
                _logger.LogError(
                    new UnauthorizedAccessException("Employer scope denied"),
                    nameof(Index), nameof(DashboardController),
                    $"User {ctx.ActorUserId} (effective {ctx.EffectiveRole}) requested employer {id}.");
                return Forbid();
            }

            vm.PageController = "Dashboard";
            vm.PageAction = "Index";
            vm.DashboardKey = "home";

            if (RoleSections.TryGetValue(ctx.EffectiveRole, out var section))
            {
                vm.RoleSectionPartial = section;
                await FillRoleSectionAsync(vm, ctx, year);
            }

            if (vm.EmployerIdValue > 0)
            {
                // Id and name together: the header and the menu read the name.
                SetCurrentEmployerId(vm.EmployerIdValue.ToString());
                SetCurrentEmployerName(vm.Employer?.Name ?? "");
            }

            return View("~/Views/Employer/Dashboard.cshtml", vm);
        }

        private async Task FillRoleSectionAsync(EmployerDashboardPageViewModel vm, EffectiveContext ctx, int year)
        {
            var role = vm.Role;

            // Password resets and user admin happen on User Accounts; the tile appears only
            // for roles whose permissions include that page. Director may add users; the
            // AM roles are limited to resets by the spec.
            role.CanManageUsers = User.HasClaim("Permission", "UserAccounts:Index");
            // Edit without Add is reset-only, the same rule UserAccountsController applies.
            role.CanAddUsers = role.CanManageUsers && User.HasClaim("Permission", "UserAccounts:Add");

            switch (ctx.EffectiveRole)
            {
                case UserRoles.SuperAdmin:
                case UserRoles.Admin:
                    role.Irs = await BuildIrsOverviewAsync(vm, year);
                    role.System = await _dashboardService.GetDashboardSystemStatsAsync();
                    (role.FileCounts, _) = await _dashboardService.GetDashboardFileCountsAsync(year, null);
                    role.Alerts = BuildAlerts(vm, role.Irs, role.FileCounts, role.System);
                    break;

                case UserRoles.ACADirector:
                    role.Irs = await BuildIrsOverviewAsync(vm, year);
                    (role.FileCounts, _) = await _dashboardService.GetDashboardFileCountsAsync(year, null);
                    role.Alerts = BuildAlerts(vm, role.Irs, role.FileCounts, null);
                    break;

                case UserRoles.AccountManager:
                    role.Irs = await BuildIrsOverviewAsync(vm, year);
                    break;

                case UserRoles.DASupervisor:
                    (role.FileCounts, _) = await _dashboardService.GetDashboardFileCountsAsync(year, null);
                    break;

                case UserRoles.DataAnalyst:
                    // Only the analyst's own files.
                    (role.FileCounts, role.Queue) = await _dashboardService.GetDashboardFileCountsAsync(year, ctx.ActorUserId);
                    break;

                case UserRoles.AMSupervisor:
                    var (links, workload) = await _dashboardService.GetDashboardAccountManagersAsync(ctx.ScopeUserId, year);
                    role.TeamWorkload = workload;

                    var managers = links.GroupBy(l => l.EmployerId)
                                        .ToDictionary(g => g.Key, g => string.Join(", ",
                                            g.Select(l => l.AccountManagerName).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct()));
                    var steps = vm.ProcessRows.GroupBy(r => r.EmployerId).ToDictionary(g => g.Key, g => g.First());

                    role.ClientsByManager = vm.SelectableEmployers
                        .Select(e =>
                        {
                            int.TryParse(e.Id, out var eid);
                            steps.TryGetValue(eid, out var step);
                            managers.TryGetValue(eid, out var names);
                            return new ManagerClientRow
                            {
                                EmployerId = eid,
                                EmployerName = e.Name,
                                Ein = e.TaxId,
                                IsAffiliate = e.CompanyId > 0,
                                ManagerNames = string.IsNullOrWhiteSpace(names) ? null : names,
                                ProcessStep = step?.ProcessStep,
                                DueDate = step?.DueDate,
                                DaysOverdue = step?.DaysOverdue ?? 0
                            };
                        })
                        .OrderBy(r => r.ManagerNames == null)
                        .ThenBy(r => r.ManagerNames)
                        .ThenBy(r => r.EmployerName)
                        .ToList();
                    break;
            }
        }

        /// <summary>
        /// IRS AIR results for the employers this user can see, latest submission per
        /// employer (the same source as the IRS AIR page).
        /// </summary>
        private async Task<IrsFilingOverview> BuildIrsOverviewAsync(EmployerDashboardPageViewModel vm, int year)
        {
            var scope = vm.SelectableEmployers.Select(e => e.Id).ToHashSet();
            var items = (await _irsXmlService.GetIrsDashboardAsync(year))
                        .Where(i => scope.Contains(i.EmployerId.ToString()))
                        .ToList();

            return new IrsFilingOverview
            {
                NotGenerated = items.Count(i => !i.SubmissionId.HasValue),
                ReadyToTransmit = items.Count(i => i.SubmissionId.HasValue && (i.StatusId ?? 0) == 0),
                Transmitted = items.Count(i => i.StatusId == 1),
                Accepted = items.Count(i => i.StatusId == 2),
                AcceptedWithErrors = items.Count(i => i.StatusId == 3),
                Rejected = items.Count(i => i.StatusId == 4),
                RecentRejections = items.Where(i => i.StatusId == 4 && i.SubmissionId.HasValue)
                    .OrderByDescending(i => i.GeneratedDate)
                    .Take(5)
                    .Select(i => new IrsRejectionDto
                    {
                        SubmissionId = i.SubmissionId!.Value,
                        EmployerName = i.EmployerName,
                        TaxYear = year,
                        GeneratedDate = i.GeneratedDate ?? default,
                        StatusMessage = "Rejected by the IRS"
                    })
                    .ToList()
            };
        }

        /// <summary>Director / Admin alerts, from the same numbers the tiles show.</summary>
        private List<DashboardAlert> BuildAlerts(EmployerDashboardPageViewModel vm, IrsFilingOverview irs,
                                                 List<AnalystFileCounts> files, DashboardSystemStats? system)
        {
            var alerts = new List<DashboardAlert>();
            var p = vm.Portfolio;
            var irsUrl = Url.Action("Index", "Irs", new { year = vm.FilingYear });
            var filesUrl = Url.Action("Index", "FileUpload");
            int critical = files.Sum(f => f.CriticalErrors), waiting = files.Sum(f => f.Waiting);

            if (system?.LockedOutUsers > 0)
                alerts.Add(new DashboardAlert { Severity = "crit", Text = $"{system.LockedOutUsers} user{(system.LockedOutUsers == 1 ? " is" : "s are")} locked out after failed sign-ins.", LinkText = "User Accounts", LinkUrl = Url.Action("Index", "UserAccounts") });
            if (irs.Rejected > 0)
                alerts.Add(new DashboardAlert { Severity = "crit", Text = $"{irs.Rejected} IRS filing{(irs.Rejected == 1 ? " was" : "s were")} rejected and need correcting.", LinkText = "Open IRS AIR", LinkUrl = irsUrl });
            if (p.PastDueClients > 0)
                alerts.Add(new DashboardAlert { Severity = "crit", Text = $"{p.PastDueClients} client{(p.PastDueClients == 1 ? " is" : "s are")} past their process due date (oldest {p.OldestPastDueDays} days)." });
            if (critical > 0)
                alerts.Add(new DashboardAlert { Severity = "warn", Text = $"{critical} file{(critical == 1 ? " has" : "s have")} critical errors (needs fixes or import failed).", LinkText = "Open Files", LinkUrl = filesUrl });
            if (waiting > 0)
                alerts.Add(new DashboardAlert { Severity = "warn", Text = $"{waiting} file{(waiting == 1 ? " is" : "s are")} waiting for an analyst to be assigned.", LinkText = "Open Files", LinkUrl = filesUrl });
            if (irs.AcceptedWithErrors > 0)
                alerts.Add(new DashboardAlert { Severity = "warn", Text = $"{irs.AcceptedWithErrors} IRS filing{(irs.AcceptedWithErrors == 1 ? " was" : "s were")} accepted with errors.", LinkText = "Open IRS AIR", LinkUrl = irsUrl });
            if (p.ClientsWithPenalties > 0)
                alerts.Add(new DashboardAlert { Severity = "warn", Text = $"{p.ClientsWithPenalties} client{(p.ClientsWithPenalties == 1 ? " shows" : "s show")} potential penalties ({p.TotalPenaltyExposure:C0})." });
            if (irs.ReadyToTransmit > 0)
                alerts.Add(new DashboardAlert { Severity = "info", Text = $"{irs.ReadyToTransmit} filing{(irs.ReadyToTransmit == 1 ? " is" : "s are")} ready to transmit to the IRS.", LinkText = "Open IRS AIR", LinkUrl = irsUrl });

            return alerts;
        }
    }
}