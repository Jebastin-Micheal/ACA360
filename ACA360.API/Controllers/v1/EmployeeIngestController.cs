using ACA360.API.Authentication;
using ACA360.Core.Models.Api; // You need to create DTOs in Core
using ACA360.Logging.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using ACA360.BusinessLogic.Interfaces;

namespace ACA360.API.Controllers.v1
{
    [ApiController]
    [Route("api/v1/employees")]
    [ApiKeyAuth] // Applies the security filter
    public class EmployeeIngestController : ControllerBase
    {
        private readonly IEmployeeService _employeeService;
        private readonly ILoggerService _logger;

        public EmployeeIngestController(IEmployeeService employeeService, ILoggerService logger)
        {
            _employeeService = employeeService;
            _logger = logger;
        }

        [HttpPost("push")]
        public async Task<IActionResult> PushData([FromBody] EmployeePushRequest request)
        {
            // 1. Get Context from Auth Filter
            int employerId = (int)HttpContext.Items["EmployerId"];
            string clientName = (string)HttpContext.Items["ClientName"];

            //_logger.LogInfo($"API Push started for Employer {employerId} by {clientName}");

            // 2. Validate Payload
            if (request == null || request.Employees.Count == 0)
                return BadRequest(new { error = "No data provided" });

            // 3. Process (Using existing Service Logic)
            // You might need to add a method to IEmployeeService that accepts this DTO
            var result = await _employeeService.ProcessApiImportAsync(employerId, request);

            if (result.Success)
            {
                return Ok(new
                {
                    message = "Data processed successfully",
                    count = result.RecordsProcessed,
                    transactionId = result.TransactionId
                });
            }

            return StatusCode(500, new { error = result.ErrorMessage });
        }
    }
}