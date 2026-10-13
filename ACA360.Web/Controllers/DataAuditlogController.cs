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

        [HttpGet]
        public async Task<IActionResult> LoadAuditLogPartial(string category, string recordId)
        {
            var logs = await _auditlogService.GetAuditLogsAsync(category, recordId);

            // This will now work because 'Controller' provides the PartialView method
            return PartialView("_AuditLogs", logs);
        }
    }
}