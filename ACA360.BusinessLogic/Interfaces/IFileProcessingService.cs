using System.Collections.Generic;
﻿using ACA360.Core.Models;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Interfaces
{
    public interface IFileProcessingService
    {
        Task ProcessFileAsync(int fileLogId, int templateId);
        Task<ImportValidationSummaryModel?> GetImportSummaryAsync(int fileLogId);
        /// <summary>Each worksheet's header row of an uploaded file (script 073). Empty when the file cannot be read.</summary>
        Task<Dictionary<string, List<string>>> ReadHeadersAsync(int fileLogId);
    }
}