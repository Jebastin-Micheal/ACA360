using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using ACA360.Core.Models;
using ACA360.Core.Models.Api;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Data;

namespace ACA360.BusinessLogic.Interfaces
{
    public interface IEmployeeService
    {
        // Employee Listing with Basic Filters + Pagination + Advanced Filters
        Task<(List<Employee> Employees, PaginationViewEntity PageRequest)>
            GetEmployeesAsync(
                string? employerId,
                string? filingYear,
                PaginationEntity paginationEntity,
                int? userId,
                EmployeeFilterRequest? filter = null);
        // Employee Detail by Primary Key
        Task<Employee?> GetEmployeeByIdAsync(int id);

        // Employee Full Tab Details (Multi-Result)
        Task<EmployeeBasicDetails> GetEmployeeBasicDetails(string? employeeId, string? filingYear);
        // Prev/Next record navigation — whole filtered id list for the nav bar.
        Task<List<int>> GetFilteredEmployeeIdsAsync(
            string? employerId, string? filingYear, int? userId, EmployeeFilterRequest filter);
        // Insert / Update Employee Basic Info
        Task<bool> InsertOrUpdateEmployeeBasicDetails(EmployeeBasicDetails employeeBasicDetails);

        // Insert / Update Employee Hire Info (TVP)
        Task<bool> InsertOrUpdateemployeeHireDetails(DataTable hires, string EmployeeID);

        // Insert / Update Employee Status Info (TVP)
        Task<bool> InsertOrUpdateemployeestatus(DataTable status, string EmployeeID);

        // Insert / Update Employee Payroll Info (TVP)
        Task<bool> InsertOrUpdateemployeepayrollInfo(DataTable payroll, string EmployeeID);

        // Insert / Update Employee Medical Info (TVP)
        Task<bool> InsertOrUpdateemployeemedical(DataTable medical, string EmployeeID);

        // Insert / Update Employee Cobra Info (TVP)
        Task<bool> InsertOrUpdatecobra(DataTable cobra, string EmployeeID);

        // Insert / Update Employee Union Info (TVP)
        Task<bool> InsertOrUpdateunion(DataTable union, string EmployeeID);

        // Insert / Update Employee Retiree Info (TVP)
        Task<bool> InsertOrUpdateretiree(DataTable retiree, string EmployeeID);

        // Insert / Update Employee Dependents Info (TVP)
        Task<bool> InsertOrUpdateemployeeDependentDetails(DataTable dependent, string EmployeeID);

        Task<bool> UpdateMonthlyCodesAsync(EmployeeCode input, string modifiedBy);

        Task<bool> UpdateCoveredIndividualCoverageAsync(CoveredIndividualModel model);
        //Delete : softDelete the record
        Task DeleteEmployeeSoft(int employeeId);

        // Dropdown Bindings
        Task<List<SelectListItem>> GetAll_Country_Async();
        Task<List<SelectListItem>> GetAll_Plan_by_employer_Async(int Emp_ID);
        Task<List<SelectListItem>> GetAll_Plan_by_employers_Async(string empIds);
        Task<List<EmployeeFlagDetail>> GetEmployeeFlagDetailsAsync(int employeeId, int filingYear);
        Task<EmployeeQuickStats> GetEmployeeQuickStatsAsync(string? employerId, int filingYear, EmployeeFilterRequest? filter);
        Task<byte[]> GenerateFlaggedEmployeesExportAsync(string? employerId, List<long>? employerIds, int filingYear);
        Task<List<EmployerLookupDto>> GetAll_Employer_by_employer_Async(int? Emp_ID);

        // Advanced Multi-Filter Employee Filtering + Pagination
        Task<(List<EmployeeFilterRequest> Records, PaginationViewEntity Pagination)>
            GetEmployeesAsync(EmployeeFilterRequest filter);
        Task<ApiImportResult> ProcessApiImportAsync(int employerId, EmployeePushRequest request);

        /// <summary>
        /// Bulk-saves edited employee rows from the Flag Fix Grid.
        /// Saves only Changes through IFlagRepository and invokes the existing audit logger.
        /// Returns database counts and any post-save audit warning. More than 1000 rows is rejected.
        /// </summary>
        Task<FlaggedGridSaveResult> BulkFixFlaggedEmployeesAsync(
            IEnumerable<FlaggedEmployeeEdit> edits, int filingYear,
            CancellationToken cancellationToken = default);
        Task<int?> GetEmployeeIdForCompareAsync(string currentEmployeeId, string filingYear, string? employerId, int? userId);
    }
}
