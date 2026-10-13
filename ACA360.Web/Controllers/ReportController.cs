using ACA360.Core.Attributes;
using ACA360.Core.Constants;
using ACA360.Core.Extensions;
using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using ACA360.Repositories.Interfaces;
using ACA360.Repositories;
using ACA360.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using System.Data;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using ACA360.BusinessLogic.Interfaces;

namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.SuperAdmin + "," + UserRoles.Admin + "," + UserRoles.ACADirector + "," + UserRoles.DASupervisor + "," + UserRoles.DASupervisor + "," + UserRoles.AMSupervisor + "," + UserRoles.Broker + "," + UserRoles.DataAnalyst + "," + UserRoles.Employer + "," + UserRoles.StaffAC + "," + UserRoles.StaffAU + "," + UserRoles.AccountManager)]
    public class ReportController : Controller
    {
        private readonly IReportService _reportService;
        private readonly ILoggerService _logger;
        private readonly IFilingYearService _filingyearservice;
        private readonly IEmployerService _employerService;

        public ReportController(IReportService reportService, ILoggerService logger, IFilingYearService filingYearService, IEmployerService employerService)
        {
            _reportService = reportService;
            _logger = logger;
            _filingyearservice = filingYearService;
            _employerService = employerService;
        }


        #region Employer_Report
        public async Task<IActionResult> Employer_Report()
        {
            var FilingYear = await _filingyearservice.GetAllFilingYears();
            ViewBag.FilingYear =  FilingYear;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployerReport_GETDATA()
        {
            try
            {
                // Read DataTables parameters from the Request Form
                var form = Request.Form;

                string? search = form["search[value]"];
                string? draw = form["draw"];
                string? order = form["order[0][column]"];
                string? orderDir = form["order[0][dir]"];
                int startRec = Convert.ToInt32(form["start"]);
                int pageSize = Convert.ToInt32(form["length"]);

                string? FilingYearFilter = form["columns[1][search][value]"];
                string? EmployerNameFilter = form["columns[2][search][value]"];
                string? cityFilter = form["columns[3][search][value]"];
                string? Is_Corrected = form["columns[4][search][value]"];
                string? Is_Authoritative = form["columns[5][search][value]"];
                string? Is_EnableCallCenter = form["columns[6][search][value]"];

                // Load full data (make sure your service is async and awaited properly)
                List<Employer_ReportModel> data = _reportService.GetEmployerReport("Employer_Report").Result;

                int totalRecords = data.Count;

                // Global search filter
                if (!string.IsNullOrWhiteSpace(search))
                {
                    data = data.Where(p =>
                        (p.Name ?? "").Contains(search, StringComparison.OrdinalIgnoreCase) ||
                        (p.Address ?? "").Contains(search, StringComparison.OrdinalIgnoreCase) ||
                        (p.Address2 ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)
                    ).ToList();
                }

                // Column-specific filter
                if (!string.IsNullOrEmpty(EmployerNameFilter) && EmployerNameFilter != "Select All")
                {
                    data = data.Where(e => e.Name != null && e.Name.Trim() == EmployerNameFilter).ToList();
                }
                if (!string.IsNullOrEmpty(FilingYearFilter) && FilingYearFilter != "Select All")
                {
                    data = data.Where(e => e.FilingYear != null && e.FilingYear.Trim() == FilingYearFilter).ToList();
                }
                if (!string.IsNullOrEmpty(cityFilter) && cityFilter != "Select All")
                {
                    data = data.Where(e => e.City != null && e.City.Trim() == cityFilter).ToList();
                }
                if (!string.IsNullOrEmpty(Is_Corrected) && Is_Corrected != "No")
                {
                    data = data.Where(e => e.IsCorrected != null && e.IsCorrected.Trim() == Is_Corrected).ToList();
                }
                if (!string.IsNullOrEmpty(Is_Authoritative) && Is_Authoritative != "No")
                {
                    data = data.Where(e => e.IsAuthoritative != null && e.IsAuthoritative.Trim() == Is_Corrected).ToList();
                }
                if (!string.IsNullOrEmpty(Is_EnableCallCenter) && Is_EnableCallCenter != "No")
                {
                    data = data.Where(e => e.EnableCallCenter != null && e.EnableCallCenter.Trim() == Is_EnableCallCenter).ToList();
                }
                // Sort data according to requested column and direction
                if (!string.IsNullOrEmpty(order) && !string.IsNullOrEmpty(orderDir))
                {
                    data = SortEmployerReportData(order, orderDir, data);
                }
                int recordsFiltered = data.Count;

                // Paging
                if (pageSize < 0)
                    data = data.Skip(startRec).Take(totalRecords).ToList();
                else
                    data = data.Skip(startRec).Take(pageSize).ToList();

                // Return JSON data as IActionResult
                return Json(new
                {
                    draw = Convert.ToInt32(draw),
                    recordsTotal = totalRecords,
                    recordsFiltered = recordsFiltered,
                    data = data
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                   ex,
                   nameof(EmployerReport_GETDATA),
                  nameof(ReportController),
                 $"Error  EmployerReportFetch"
              );

                // Return empty JSON result on error
                return Json(new
                {
                    draw = 0,
                    recordsTotal = 0,
                    recordsFiltered = 0,
                    data = new List<Employer_ReportModel>()
                });
            }
        }

        private List<Employer_ReportModel> SortEmployerReportData(string order, string orderDir, List<Employer_ReportModel> data)
        {
            try
            {
                switch (order)
                {
                    case "0":
                        return orderDir == "desc"
                            ? data.OrderByDescending(e => e.Name).ToList()
                            : data.OrderBy(e => e.Name).ToList();
                    case "1":
                        return orderDir == "desc"
                            ? data.OrderByDescending(e => e.FilingYear).ToList()
                            : data.OrderBy(e => e.FilingYear).ToList();
                    case "2":
                        return orderDir == "desc"
                            ? data.OrderByDescending(e => e.Taxid).ToList()
                            : data.OrderBy(e => e.Taxid).ToList();
                    case "3":
                        return orderDir == "desc"
                            ? data.OrderByDescending(e => e.Name).ToList()
                            : data.OrderBy(e => e.Name).ToList();
                    default:
                        return data.OrderBy(e => e.Name).ToList();
                }
            }
            catch
            {
                return data;
            }
        }
        #endregion

      

        
    }
}
