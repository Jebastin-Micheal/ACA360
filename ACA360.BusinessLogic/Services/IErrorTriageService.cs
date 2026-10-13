using ACA360.Core.Models;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Services
{
    public interface IErrorTriageService // Ensure this is public
    {
        Task<MemoryStream> GenerateErrorReportAsync(int fileLogId);
        Task<ErrorTriageViewModel?> BuildTriageViewModelAsync(int fileLogId, PaginationEntity paginationEntity, string severity);
        // --- NEW METHODS ---
        Task<bool> UpdateErrorStatusAsync(long stagingRowErrorId, string status, int fileLogId, string? changedBy, string? reason = null);
        Task<UpdateRowResult> UpdateFieldAndRevalidateAsync(UpdateFieldPayload payload, string? changedBy);
        Task<object?> GetStagingRowAsync(string? tableName, long rowId);
        /// <summary>The row's editor, or null when the row is not in that file.</summary>
        Task<EditRowViewModel?> BuildEditRowViewModelAsync(int fileLogId, string tableName, long rowId);
        Task<FileCorrectionsViewModel> GetCorrectionsAsync(int fileLogId, string? source, int? ruleId, string? columnName, string? search, int pageIndex, int pageSize);
        // SendForApprovalAsync removed — the AM authorisation step no longer exists.
    }
}
