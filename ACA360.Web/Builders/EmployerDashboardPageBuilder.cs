using ACA360.BusinessLogic.Interfaces;
using ACA360.BusinessLogic.Services;
using ACA360.Core.Models;
using ACA360.Repositories.Interfaces;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ACA360.Web.Builders
{
    /// <summary>
    /// Builds the client-specified dashboard (portfolio band + employer detail) for one
    /// request. Shared by Employer/Dashboard and the role home page (/Dashboard), so
    /// both show the same numbers from the same scope.
    /// </summary>
    public class EmployerDashboardPageBuilder
    {
        private readonly IViewAsService _viewAsService;
        private readonly IDashboardService _dashboardService;
        private readonly IEmployerService _employerService;
        private readonly IFilingYearService _filingYearService;
        private readonly IFilingDeadlineService _deadlines;
        private readonly IACALogicService _aca;

        public EmployerDashboardPageBuilder(
            IViewAsService viewAsService,
            IDashboardService dashboardService,
            IEmployerService employerService,
            IFilingYearService filingYearService,
            IFilingDeadlineService deadlines,
            IACALogicService aca)
        {
            _deadlines = deadlines;
            _aca = aca;
            _viewAsService = viewAsService;
            _dashboardService = dashboardService;
            _employerService = employerService;
            _filingYearService = filingYearService;
        }

        /// <summary>
        /// Returns null when the requested employer is outside the caller's scope
        /// (the caller answers 403). <paramref name="sessionEmployerId"/> is the
        /// employer picked elsewhere in the app, used when no id is requested.
        /// </summary>
        public async Task<EmployerDashboardPageViewModel?> BuildAsync(
            EffectiveContext ctx, int id, int year, string? sessionEmployerId)
        {
            // Permission first, and only permission. The full permitted set —
            // not filtered by year.
            var permitted = await _viewAsService.GetPermittedEmployersAsync(ctx);

            if (ctx.Mode == ImpersonationMode.Employer) id = ctx.TargetId;
            if (id <= 0) int.TryParse(sessionEmployerId, out id);
            if (id <= 0) int.TryParse(permitted.FirstOrDefault()?.Id, out id);

            var idText = id.ToString();

            // "You may not see this employer" is a 403. Whether it has a row for the
            // chosen year is answered below with an empty state — conflating the two
            // turns an ordinary year switch into an access denial.
            if (id > 0 && !permitted.Any(e => e.Id == idText))
                return null;

            // Employer rows are per filing year, so the picker only offers the ones
            // that exist in the selected year.
            var forYear = permitted
                .Where(e => string.IsNullOrEmpty(e.FilingYear) || e.FilingYear == year.ToString())
                .ToList();

            var vm = new EmployerDashboardPageViewModel
            {
                Context = ctx,
                FilingYear = year,
                AvailableYears = await _filingYearService.GetAllFilingYears(),
                SelectableEmployers = forYear.Any() ? forYear : permitted,

                // Null when the selected employer has no row for this year. The view
                // renders "No employer record for plan year N" and the picker above
                // still lists the employers that do exist, so recovery is one click.
                Employer = forYear.FirstOrDefault(e => e.Id == idText)
            };

            // The next filing deadlines, IRS and state, for every role (script 063).
            try { vm.UpcomingDeadlines = await _deadlines.UpcomingAsync(4); }
            catch { vm.UpcomingDeadlines = new(); }   // never stops the dashboard

            // The employers in scope whose estimated penalty grew most since last year (script 064).
            try
            {
                var ids = forYear.Select(e => int.TryParse(e.Id, out var x) ? x : 0).Where(x => x > 0);
                vm.ExposureGrowth = await _aca.GetExposureGrowthAsync(year, ids, 5);
            }
            catch { vm.ExposureGrowth = new(); }

            // Only the plan year on screen: employers are stored once per filing year.
            vm.ViewAs = await _viewAsService.GetOptionsAsync(ctx.ActorUserId, ctx.ActorRole, year);

            if (vm.ShowPortfolioBand)
            {
                vm.Portfolio = await _dashboardService.GetPortfolioCountsAsync(ctx.ScopeUserId, year);
                // Search and sort are client-side, so send the whole book rather than
                // the worst 25 — otherwise the search only ever sees the top of the
                // list and quietly misses the rest.
                vm.ProcessRows = await _dashboardService.GetPortfolioProcessStepsAsync(ctx.ScopeUserId, year, 500);
            }

            if (vm.Employer != null && id > 0)
            {
                vm.Employer360 = await _dashboardService.GetEmployerDashboardParityAsync(id, year, ctx.ActorUserId);
                vm.Plans = await _dashboardService.GetPlanSummaryAsync(id, year);

                // The 360 view supplies the ALE trend, the coverage map and the
                // workforce split — the three things the parity DTO does not carry.
                var e360 = await _employerService.GetEmployer360Async(id, year);
                if (e360 != null)
                {
                    vm.AleTrend = e360.AleTrend ?? new();

                    var pen = await _dashboardService.GetPenaltySummaryAsync(id, year);
                    if (pen != null)
                    {
                        vm.PenaltyTypeA = (decimal)pen.TypeAPenalty;
                        vm.PenaltyTypeB = (decimal)pen.TypeBPenalty;
                        vm.PenaltyExposure = (decimal)pen.TotalPenalty;
                        vm.PenaltyTypeATriggered = (bool)pen.TypeATriggered;
                        vm.PenaltyChargeableFTEs = (int)pen.ChargeableFTEs;
                        vm.PenaltyApplicableMonths = (int)pen.ApplicableMonths;
                        vm.PenaltyTypeBViolations = (int)pen.TypeBViolations;
                        vm.PenaltyCalculatedDate = pen.CalculatedDate;
                        vm.PenaltyType = vm.PenaltyLabel;
                    }
                    vm.CoveragePercent = await _dashboardService.GetCoverageMapAsync(id, year);
                    vm.WorkforceFullTime = e360.FullTimeCount;
                    vm.WorkforcePartTime = e360.PartTimeCount;
                    vm.WorkforceVariable = e360.VariableHoursCount;
                    vm.WorkforceCobra = e360.COBRACount;
                }
            }

            return vm;
        }
    }
}
