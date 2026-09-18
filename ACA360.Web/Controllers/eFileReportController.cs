using ACA360.Logging.Interfaces;
using ACA360.Repositories.Interfaces;
using Microsoft.AspNetCore.Mvc;
using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Constants;
using Microsoft.AspNetCore.Authorization;
namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.InternalTeam)]
    public class eFileReportController : Controller
    {
        private readonly IReportService _reportService;
        private readonly ILoggerService _logger;
        private readonly IFilingYearService _filingyearservice;
        private readonly IEmployerService _employerService;
        public eFileReportController(IReportService reportService, ILoggerService logger, IFilingYearService filingYearService, IEmployerService employerService)
        {
            _reportService = reportService;
            _logger = logger;
            _filingyearservice = filingYearService;
            _employerService = employerService;
        }
        #region  ExportEFile_Report
        public async Task<IActionResult> Index()
        {
            // Load an empty page initially
            var FilingYear = await _filingyearservice.GetAllFilingYears();
            ViewBag.FilingYear = FilingYear;
            return View();
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ExportFileReport_GETDATA(string EmployerTaxId, string SSN, string FilingYear)
        {
            try
            {
                // 1. Call your Service to get the data (using the code we wrote previously)
                // Assuming _acaService is injected into your controller
                var dataModel = _reportService.GetAcaData(EmployerTaxId, SSN, FilingYear);

                // 2. Prepare the list for the DataTable
                var employeeList = dataModel.Employees;

                // 3. Handle Filtering/Sorting/Paging (Client-side logic applied to server data)
                // Since the SP returns all rows, we handle pagination in C# for the DataTable
                var draw = Request.Form["draw"].FirstOrDefault();
                var start = Request.Form["start"].FirstOrDefault();
                var length = Request.Form["length"].FirstOrDefault();
                var sortColumn = Request.Form["columns[" + Request.Form["order[0][column]"].FirstOrDefault() + "][name]"].FirstOrDefault();
                var sortColumnDirection = Request.Form["order[0][dir]"].FirstOrDefault();
                var searchValue = Request.Form["search[value]"].FirstOrDefault();

                int pageSize = length != null ? Convert.ToInt32(length) : 0;
                int skip = start != null ? Convert.ToInt32(start) : 0;
                int recordsTotal = 0;

                // Search Logic (Basic example)
                if (!string.IsNullOrEmpty(searchValue))
                {
                    employeeList = employeeList?.Where(m =>
     (m.First_Name ?? "").Contains(searchValue, StringComparison.OrdinalIgnoreCase) ||
     (m.Last_Name ?? "").Contains(searchValue, StringComparison.OrdinalIgnoreCase) ||
     (m.Member_SSN ?? "").Contains(searchValue)
 ).ToList();
                }

                recordsTotal = employeeList.Count();

                // Paging
                // var data = employeeList.Skip(skip).Take(pageSize).ToList();
                //var data = employeeList.Skip(skip).Take(recordsTotal).ToList();
                var data = (pageSize < 0) ? employeeList.Skip(skip).ToList() : employeeList.Skip(skip).Take(pageSize).ToList();
                // 4. Return JSON
                // We also pass the Employer Header info in "extraData" so we can show it on the UI
                return Json(new
                {
                    draw = draw,
                    recordsFiltered = recordsTotal,
                    recordsTotal = recordsTotal,
                    data = data,
                    employerInfo = dataModel.Employer // Pass header info
                });

            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }

        #endregion
    }
}
