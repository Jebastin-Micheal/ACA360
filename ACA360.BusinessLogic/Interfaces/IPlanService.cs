using ACA360.Core.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Interfaces
{
    public interface IPlanService
    {
        Task<(List<Plan> Plans, PaginationViewEntity? PageRequest)> GetPlanList(string? empId, string? FillingYear, PaginationEntity paginationEntity, string? planType, string? fundingType);
        Task<Plan> getPlan(string planid);
        Task<bool> InsertOrUpdate(Plan plan, DataTable plan_benefits);
        Task<bool> PlanDelete(string planid);

        Task<List<SelectListItem>> GetAll_Plan_Banding_TypeAsync();
        Task<List<SelectListItem>> GetAll_Plan_Waiting_PeriodAsync();
        Task<List<SelectListItem>> GetAll_Plan_TypeAsync();
        Task<List<SelectListItem>> GetAll_Plan_Funding_TypeAsync();
        Task<string?> GetPlanIdForCompareAsync(string currentPlanId, string? targetEmployerId);
    } 
}
