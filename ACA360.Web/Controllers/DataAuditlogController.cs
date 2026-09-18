using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ACA360.Web.Controllers
{
    [Authorize(Roles = "SuperAdmin,Admin")] // Restrict viewing logs to top roles
    [Route("api/[controller]")]
    [ApiController]
    public class DataAuditlogController : Controller
    {
        private readonly IDataAuditlogService _auditlogService;
        private readonly ILoggerService _logger;

        public DataAuditlogController(IDataAuditlogService auditlogService, ILoggerService logger)
        {
            _auditlogService = auditlogService;
            _logger = logger;
        }

        [HttpPost("search")]
        public async Task<IActionResult> GetAuditLogs([FromBody] AuditLogFilter filter)
        {
            try
            {
                var logs = await _auditlogService.GetLogsAsync(filter);
                return Ok(logs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetAuditLogs), nameof(DataAuditlogController), "Error occurred while searching audit logs");
                return StatusCode(500, "An error occurred while retrieving the audit logs.");
            }
        }

        [HttpGet]
        public async Task<IActionResult> LoadAuditLogPartial(string category, string recordId)
        {
            var logs = await _auditlogService.GetAuditLogsAsync(category, recordId);

            // This will now work because 'Controller' provides the PartialView method
            return PartialView("_AuditLogs", logs);
        }
    }
}