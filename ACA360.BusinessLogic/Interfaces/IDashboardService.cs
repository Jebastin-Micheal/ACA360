using ACA360.Core.Models;
using ACA360.Core.Models.Dashboard;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Interfaces
{
    public interface IDashboardService
    {
        /// <summary>Employer → account manager links in the user's scope, and clients per account manager.</summary>
        Task<(List<EmployerManagerLink> Links, List<StaffLoad> Workload)> GetDashboardAccountManagersAsync(int userId, int year);
        /// <summary>File results per analyst for a plan year; with onlyAssignedTo, that analyst's row and open-file queue.</summary>
        Task<(List<AnalystFileCounts> Counts, List<DashboardQueueFile> Queue)> GetDashboardFileCountsAsync(int year, int? onlyAssignedTo);
        /// <summary>Active users, lockouts, sign-ins and monthly uploads for the Admin dashboard.</summary>
        Task<DashboardSystemStats> GetDashboardSystemStatsAsync();
        Task<EmployerDashboardDto> GetEmployerDashboardParityAsync(int employerId, int year, int userId);
        Task<DashboardLayoutDto> GetDashboardLayoutAsync(int userId, string dashboardKey);
        Task SaveDashboardLayoutAsync(int userId, DashboardLayoutDto layout, string dashboardKey);
        Task<PortfolioCountsDto> GetPortfolioCountsAsync(int userId, int year);
        Task<List<PortfolioProcessRow>> GetPortfolioProcessStepsAsync(int userId, int year, int top = 25);
        Task<List<PlanSummaryRow>> GetPlanSummaryAsync(int employerId, int year);
        Task<int[]> GetCoverageMapAsync(int employerId, int year);
        Task<dynamic?> GetPenaltySummaryAsync(int employerId, int year);
    }
}
