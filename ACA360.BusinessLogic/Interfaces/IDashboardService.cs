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
        Task<DashboardStatsModel> GetDashboardStatsAsync(string userId);
        // NEW METHOD for the Advanced Dashboard
        Task<SuperAdminDashboardData> GetAdvancedSuperAdminStatsAsync();
        Task<DataAnalystDashboardData> GetDataAnalystStatsAsync(string userId);
        Task<DirectorDashboardData> GetDirectorStatsAsync(string userId);
        Task<SupervisorDashboardData> GetSupervisorStatsAsync(string userId, string role);
        Task<AccountingDashboardData> GetAccountingStatsAsync();
        Task<AuditDashboardData> GetAuditStatsAsync();
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
