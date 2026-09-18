using ACA360.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Interfaces
{
    public interface IDataAuditlogService
    {
        Task<bool> LogChangeAsync(DataAuditLog auditLog);
        Task<IEnumerable<DataAuditLog>> GetLogsAsync(AuditLogFilter filter);
        Task<bool> CaptureAndLogChangeAsync(string tableName, string recordId, string actionType, object? oldRecord, object? newRecord);

        Task<IEnumerable<AuditLogModel>> GetAuditLogsAsync(string categoryType, string recordId = null);
    }
}
