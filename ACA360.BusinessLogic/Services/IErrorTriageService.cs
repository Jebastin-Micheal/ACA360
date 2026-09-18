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
        Task<ErrorTriageViewModel> BuildTriageViewModelAsync(int fileLogId, PaginationEntity paginationEntity, string severity);
        // --- NEW METHODS ---
        Task UpdateErrorStatusAsync(long stagingRowErrorId, string status);
        Task<UpdateRowResult> UpdateFieldAndRevalidateAsync(UpdateFieldPayload payload);
        Task<object> GetStagingRowAsync(string tableName, long rowId);
        Task<EditRowViewModel> BuildEditRowViewModelAsync(int fileLogId, string tableName, long rowId);
        // SendForApprovalAsync removed — the AM authorisation step no longer exists.
    }
}
