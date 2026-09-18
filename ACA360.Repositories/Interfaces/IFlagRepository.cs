using System.Threading;
using ACA360.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ACA360.Repositories.Interfaces
{
    public interface IFlagRepository
    {
        /// <summary>
        /// Retrieves all flag definitions ordered by Flag Code.
        /// </summary>
        Task<IEnumerable<FlagDefinition>> GetAllFlagsAsync();

        /// <summary>
        /// Retrieves a specific flag definition by ID.
        /// </summary>
        Task<FlagDefinition> GetFlagByIdAsync(int flagId);

        /// <summary>
        /// Retrieves a specific flag definition by Code (e.g. "16.1").
        /// </summary>
        Task<FlagDefinition> GetFlagByCodeAsync(string flagCode);

        /// <summary>
        /// Inserts or Updates a flag definition based on whether FlagId is 0.
        /// </summary>
        Task<int> SaveFlagAsync(FlagDefinition flag);

        /// <summary>
        /// Soft deletes or deactivates a flag.
        /// </summary>
        Task DeleteFlagAsync(int flagId);

        /// <summary>
        /// Retrieves a paginated list of flags with a total count.
        /// </summary>
        Task<(IEnumerable<FlagDefinition> List, int TotalCount)> GetFlagListAsync(
            string search, string sortCol, string sortOrder, int page, int pageSize);

        /// <summary>
        /// Returns one employee page (RS1), flag definitions (RS2), and counts (RS3).
        /// Only the requested tab's child collection is loaded.
        /// Calls sp_GetFlaggedEmployees_GridAndFlags_demo.
        /// </summary>
        Task<FlaggedGridDataResult> GetFlaggedEmployeeGridAsync(
     string? employerIds, int filingYear, int skip, int take,
     string? searchValue, string? flagFilter, string? severityFilter,
     bool includeFlagDefs = true, string? editedEmployeeCodeIds = null,
     string? editedEmployeeIds = null, CancellationToken cancellationToken = default,
     string tab = "employee");

        Task<FlaggedGridSaveResult> SaveFlaggedGridAsync(
            IReadOnlyCollection<FlaggedEmployeeEdit> edits, int filingYear,
            CancellationToken cancellationToken = default);

        Task<List<FlagGridDefinition>> GetFlaggedGridFlagCountsAsync(
            string? employerIds, int filingYear, CancellationToken cancellationToken = default);


    }
}
