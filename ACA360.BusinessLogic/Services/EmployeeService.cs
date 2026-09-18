using ACA360.Repositories.Interfaces;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using System;
using ACA360.Core.Models;
using ACA360.Core.Models.Api;
using ACA360.Logging.Interfaces;
using Dapper;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ACA360.BusinessLogic.Interfaces;
namespace ACA360.BusinessLogic.Services
{
    public class EmployeeService : IEmployeeService
    {
        private readonly string _connectionString;
        private readonly ILoggerService _logger;
        private readonly IDataAuditlogService _auditLogger; // Added Audit Logger
        private readonly IFlagRepository _flagRepository;
        private readonly IACALogicService _acaLogicService;
        public EmployeeService(string connectionString, ILoggerService logger,
            IDataAuditlogService auditLogger, IFlagRepository flagRepository, IACALogicService acaLogicService)
        {
            _connectionString = connectionString;
            _logger = logger;
            _auditLogger = auditLogger; // Assigned Audit Logger
            _flagRepository = flagRepository;
            _acaLogicService = acaLogicService;
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        #region 1.Get Employees with Pagination and Filters
        
        public async Task<(List<Employee> Employees, PaginationViewEntity PageRequest)> GetEmployeesAsync(string? employerId, string? filingYear, PaginationEntity paginationEntity, int? userId, EmployeeFilterRequest? filter = null)
        {
            var employees = new List<Employee>();
            PaginationViewEntity? pagination = null;

            try
            {
                using (var db = Connection)
                {
                    var p = new DynamicParameters();
                    //p.Add("@Employer_ID", employerId ?? (object)DBNull.Value);
                    //p.Add("@FillingYear", string.IsNullOrEmpty(filingYear) ? DateTime.Now.Year : filingYear);
                    long? employerIdValue = null;
                    if (!string.IsNullOrWhiteSpace(employerId) && long.TryParse(employerId, out var eid))
                        employerIdValue = eid;
                    p.Add("@Employer_ID", (object?)employerIdValue ?? DBNull.Value, dbType: DbType.Int64);

                    int filingYearValue = DateTime.Now.Year;
                    if (!string.IsNullOrWhiteSpace(filingYear) && int.TryParse(filingYear, out var fy))
                        filingYearValue = fy;
                    p.Add("@FillingYear", filingYearValue, dbType: DbType.Int32);

                    p.Add("@PageIndex", paginationEntity.PageIndex);
                    p.Add("@PageSize", paginationEntity.PageSize);
                    p.Add("@Search", paginationEntity.Search ?? (object)DBNull.Value);
                    p.Add("@SortColumn", paginationEntity.SortColumn);
                    p.Add("@SortOrder", paginationEntity.SortOrder);
                    p.Add("@User_ID", userId);
                    p.Add("@EmployerIDs", (filter?.Employer_IDs?.Any() == true)
                        ? JsonSerializer.Serialize(filter.Employer_IDs) : null);

                    p.Add("@PlanIDs", (filter?.PlanIDs?.Any() == true)
                        ? JsonSerializer.Serialize(filter.PlanIDs) : null);

                    p.Add("@Statuses", (filter?.Statuses?.Any() == true)
                        ? JsonSerializer.Serialize(filter.Statuses) : null);

                    p.Add("@EnrollmentIDs", (filter?.EnrollmentIDs?.Any() == true)
                        ? JsonSerializer.Serialize(filter.EnrollmentIDs) : null);

                    p.Add("@Flags", (filter?.Flags?.Any() == true)
                        ? JsonSerializer.Serialize(filter.Flags) : null);

                    p.Add("@IsReceivingForms", (filter?.OtherIDs?.Any() == true)
                        ? JsonSerializer.Serialize(filter.OtherIDs) : null);

                    p.Add("@QuickFilter", string.IsNullOrWhiteSpace(filter?.QuickFilter) ? null : filter.QuickFilter);
                    // Use QueryMultipleAsync to get both Data and Counts in one go
                    using (var multi = await db.QueryMultipleAsync("sp_EmployeeList_Filter", p, commandType: CommandType.StoredProcedure))
                    {
                        // 1. Read Employee Data
                        // Dapper automatically maps columns like 'firstName' to the Employee property 'FirstName'
                        employees = (await multi.ReadAsync<Employee>()).ToList();

                        // 2. Read Pagination Metadata (RowCount, TotalCount)
                        var metaRow = await multi.ReadFirstOrDefaultAsync();
                        if (metaRow != null)
                        {
                            int totalItems = Convert.ToInt32(metaRow.TotalCount);
                            pagination = new PaginationViewEntity
                            {
                                TotalItems = totalItems,
                                CurrentPage = paginationEntity.PageIndex,
                                PageSize = paginationEntity.PageSize,
                                TotalPages = (int)Math.Ceiling((double)totalItems / paginationEntity.PageSize),
                                StartPage = ((paginationEntity.PageIndex - 1) / 10) * 10 + 1,
                                EndPage = Math.Min(((paginationEntity.PageIndex - 1) / 10) * 10 + 10, (int)Math.Ceiling((double)totalItems / paginationEntity.PageSize)),
                                SortColumn = paginationEntity.SortColumn,
                                SortOrder = paginationEntity.SortOrder,
                                filingYear = filingYear = filingYear ?? string.Empty,
                                RecordCount = Convert.ToInt32(metaRow.RowCount),
                                PageNumber = paginationEntity.PageIndex,
                                TotalCount = totalItems
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetEmployeesAsync), nameof(EmployeeService), $"Failed to list employees for Employer {employerId}", "DB");
                // Return empty list instead of throwing, to keep UI stable
                return (new List<Employee>(), new PaginationViewEntity());
            }

            return (employees, pagination ?? new PaginationViewEntity());
        }

        public async Task<Employee?> GetEmployeeByIdAsync(int id)
        {
            try
            {
                using (var db = Connection)
                {
                    // We selecting * from Employee. 
                    // Since we updated the table schema, this will now include:
                    // HireDate, TerminationDate, CoverageElected, etc.
                    var sql = "SELECT * FROM Employee WHERE id = @Id AND IsDeleted = 0";
                    return await db.QuerySingleOrDefaultAsync<Employee>(sql, new { Id = id });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetEmployeeByIdAsync), nameof(EmployeeService), $"Failed to fetch employee {id}", "DB");
                return null;
            }
        }
        
        public async Task<EmployeeBasicDetails> GetEmployeeBasicDetails(string? employeeId, string? filingYear)
        {
            if (string.IsNullOrWhiteSpace(employeeId))
                throw new ArgumentNullException(nameof(employeeId));

            using var connection = new SqlConnection(_connectionString);

            try
            {
                await connection.OpenAsync();

                using var multi = await connection.QueryMultipleAsync(
                    "sp_EmployeeTabView",
                    new { id = employeeId, filingYear = filingYear },
                    commandType: CommandType.StoredProcedure);

                var employeeDetails = await multi.ReadFirstOrDefaultAsync<EmployeeBasicDetails>();

                if (employeeDetails == null)
                    throw new InvalidOperationException($"Employee details not found for ID: {employeeId}");

                employeeDetails.EmployeeEnrollment ??= new List<EmployeeEnrollmentInfo>();
                employeeDetails.EmployeeHireDetail ??= new List<EmployeeHireDetails>();
                employeeDetails.EmployeeStatus ??= new List<EmployeeStatus>();
                employeeDetails.EmployeePayrollDetails ??= new List<EmployeePayroll>();
                employeeDetails.AuditLog ??= new List<AuditLog>();
               // employeeDetails.Flag ??= new List<Flag>();
                employeeDetails.CoveredIndividuals ??= new List<CoveredIndividualModel>();
                employeeDetails.CoveredIndividualCodes ??= new List<CoveredIndividualModel>();
                employeeDetails.CoveredIndividualCodes ??= new List<CoveredIndividualModel>();

                //Codes
                employeeDetails.MonthlyCodes = await multi.ReadFirstOrDefaultAsync<EmployeeCode>();

                // Enrollment Info
                employeeDetails.EmployeeEnrollment.AddRange(await multi.ReadAsync<EmployeeEnrollmentInfo>());

                // Hire Info
                employeeDetails.EmployeeHireDetail.AddRange(await multi.ReadAsync<EmployeeHireDetails>());

                // Status Info
                employeeDetails.EmployeeStatus.AddRange(await multi.ReadAsync<EmployeeStatus>());

                // Payroll Info
                employeeDetails.EmployeePayrollDetails.AddRange(await multi.ReadAsync<EmployeePayroll>());

                // Audit Log
                employeeDetails.AuditLog.AddRange(await multi.ReadAsync<AuditLog>());

                // Flags
                //employeeDetails.Flag.AddRange(await multi.ReadAsync<Flag>());

                // Covered Individuals
                employeeDetails.CoveredIndividuals.AddRange(await multi.ReadAsync<CoveredIndividualModel>());

                // Covered Individuals Codes
                employeeDetails.CoveredIndividualCodes.AddRange(await multi.ReadAsync<CoveredIndividualModel>());

                // "Will Employee Receive 1095 Form?" (getForm) is not returned by
                // sp_EmployeeTabView, so MonthlyCodes.GetForm would otherwise stay at its
                // model default of 1. Load the real, engine-computed value explicitly.
                if (employeeDetails.MonthlyCodes != null)
                {
                    using var gfConn = new SqlConnection(_connectionString);
                    employeeDetails.MonthlyCodes.GetForm = await gfConn.ExecuteScalarAsync<int?>(
                        "SELECT TOP 1 getForm FROM EmployeeCode WHERE employeeId = @id AND filingYear = @fy",
                        new { id = employeeId, fy = filingYear }) ?? 1;
                }
                return employeeDetails;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "GetEmployeeBasicDetails",
                    "SERVICE",
                    $"Failed to fetch basic details for EmployeeId: {employeeId}");
                throw;
            }
        }

        // Prev/Next record navigation — returns the WHOLE filtered id list (all pages)
        // so the nav bar can walk every filtered record, not just the visible page. Reuses the same
        // GetEmployeesAsync path the grid uses so ordering/filtering match exactly.
        public async Task<List<int>> GetFilteredEmployeeIdsAsync(string? employerId, string? filingYear, int? userId, EmployeeFilterRequest filter)
        {
            var pe = new PaginationEntity
            {
                PageIndex = 1,
                PageSize = int.MaxValue,   // one call, whole list
                Search = filter.Search ?? "",
                SortColumn = filter.SortColumn,
                SortOrder = filter.SortOrder ?? "asc",
                fillingYear = filingYear
            };
            var (employees, meta) = await GetEmployeesAsync(employerId, filingYear, pe, userId, filter);
            var ids = employees.Where(e => e.Id.HasValue).Select(e => e.Id!.Value).ToList();
            return ids;
        }
        #endregion

        #region 2.Dropdown Filters
        public async Task<List<SelectListItem>> GetAll_Country_Async()
        {
            try
            {
                using (var db = Connection)
                {
                    var parameters = new DynamicParameters();

                    var Country = await db.QueryAsync<drp_Country>(
                        "usp_drop_dwon_Country",
                        commandType: CommandType.StoredProcedure
                    );

                    return Country.Select(c => new SelectListItem
                    {
                        Text = c.Country_Name,
                        Value = c.Country_ID
                    }).ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetAll_Country_Async),
                    nameof(EmployeeService), "Failed to load Plan dropdown Country", "DB");
                return new List<SelectListItem>();
            }
        }

        public async Task<List<SelectListItem>> GetAll_Plan_by_employer_Async(int Emp_ID)
        {
            try
            {
                using (var db = Connection)
                {
                    var parameters = new DynamicParameters();
                    parameters.Add("@Emp_ID", Emp_ID);

                    var planTypes = await db.QueryAsync<drp_Plan_Type>(
                        "usp_select_Plan",
                        parameters,
                        commandType: CommandType.StoredProcedure
                    );

                    return planTypes.Select(c => new SelectListItem
                    {
                        Text = c.Plan_Type_Name,
                        Value = c.Plan_Type_ID
                    }).ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetAll_Plan_by_employer_Async),
                    nameof(EmployeeService), "Failed to load Plan dropdown", "DB");
                return new List<SelectListItem>();
            }
        }

        public async Task<EmployeeQuickStats> GetEmployeeQuickStatsAsync(string? employerId, int filingYear, EmployeeFilterRequest? filter)
        {
            try
            {
                using (var db = Connection)
                {
                    // Counts respect the same filters as the list, so the badge
                    // numbers always match what the grid can show.
                    var p = new DynamicParameters();
                    p.Add("@Employer_ID", employerId ?? (object)DBNull.Value);
                    p.Add("@EmployerIDs", (filter?.Employer_IDs?.Any() == true) ? JsonSerializer.Serialize(filter.Employer_IDs) : null);
                    p.Add("@FillingYear", filingYear);
                    p.Add("@Search", string.IsNullOrWhiteSpace(filter?.Search) ? null : filter.Search);
                    p.Add("@PlanIDs", (filter?.PlanIDs?.Any() == true) ? JsonSerializer.Serialize(filter.PlanIDs) : null);
                    p.Add("@Statuses", (filter?.Statuses?.Any() == true) ? JsonSerializer.Serialize(filter.Statuses) : null);
                    p.Add("@EnrollmentIDs", (filter?.EnrollmentIDs?.Any() == true) ? JsonSerializer.Serialize(filter.EnrollmentIDs) : null);
                    p.Add("@Flags", (filter?.Flags?.Any() == true) ? JsonSerializer.Serialize(filter.Flags) : null);
                    p.Add("@IsReceivingForms", (filter?.OtherIDs?.Any() == true) ? JsonSerializer.Serialize(filter.OtherIDs) : null);

                    var stats = await db.QueryFirstOrDefaultAsync<EmployeeQuickStats>(
                        "sp_Employee_QuickStats", p, commandType: CommandType.StoredProcedure);

                    return stats ?? new EmployeeQuickStats();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetEmployeeQuickStatsAsync),
                    nameof(EmployeeService), "Failed to load employee quick stats", "DB");
                return new EmployeeQuickStats();
            }
        }

        public async Task<byte[]> GenerateFlaggedEmployeesExportAsync(string? employerId, List<long>? employerIds, int filingYear)
        {
            using (var db = Connection)
            {
                var p = new DynamicParameters();
                p.Add("@Employer_ID", employerId ?? (object)DBNull.Value);
                p.Add("@EmployerIDs", (employerIds?.Any() == true) ? JsonSerializer.Serialize(employerIds) : null);
                p.Add("@FillingYear", filingYear);

                var rows = (await db.QueryAsync<FlaggedEmployeeExportRow>(
                    "sp_FlaggedEmployees_Export", p, commandType: CommandType.StoredProcedure)).ToList();

                using var package = new OfficeOpenXml.ExcelPackage();
                var ws = package.Workbook.Worksheets.Add("Flagged Employees");

                string[] headers = { "First Name", "Last Name", "SSN", "Employer", "Tax ID", "Severity", "Flags" };
                for (int c = 0; c < headers.Length; c++)
                {
                    ws.Cells[1, c + 1].Value = headers[c];
                    ws.Cells[1, c + 1].Style.Font.Bold = true;
                }

                int r = 2;
                foreach (var row in rows)
                {
                    ws.Cells[r, 1].Value = row.FirstName;
                    ws.Cells[r, 2].Value = row.LastName;
                    ws.Cells[r, 3].Value = row.SSN;
                    ws.Cells[r, 4].Value = row.EmployerName;
                    ws.Cells[r, 5].Value = row.TaxId;
                    ws.Cells[r, 6].Value = row.FlagSeverity;
                    ws.Cells[r, 7].Value = row.FlagDescription;
                    r++;
                }

                if (rows.Count > 0)
                    ws.Cells[1, 1, r - 1, headers.Length].AutoFitColumns(10, 80);

                return package.GetAsByteArray();
            }
        }

        public async Task<List<EmployeeFlagDetail>> GetEmployeeFlagDetailsAsync(int employeeId, int filingYear)
        {
            try
            {
                using (var db = Connection)
                {
                    var p = new DynamicParameters();
                    p.Add("@EmployeeId", employeeId);
                    p.Add("@FilingYear", filingYear);

                    var rows = await db.QueryAsync<EmployeeFlagDetail>(
                        "sp_GetEmployeeFlagDetails", p, commandType: CommandType.StoredProcedure);

                    return rows.ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetEmployeeFlagDetailsAsync),
                    nameof(EmployeeService), $"Failed to load flag details for employee {employeeId}", "DB");
                return new List<EmployeeFlagDetail>();
            }
        }

        public async Task<List<SelectListItem>> GetAll_Plan_by_employers_Async(string empIds)
        {
            try
            {
                using (var db = Connection)
                {
                    var parameters = new DynamicParameters();
                    parameters.Add("@Emp_IDs", empIds);

                    var planTypes = await db.QueryAsync<drp_Plan_Type>(
                        "usp_select_Plan",
                        parameters,
                        commandType: CommandType.StoredProcedure
                    );

                    return planTypes.Select(c => new SelectListItem
                    {
                        Text = c.Plan_Type_Name,
                        Value = c.Plan_Type_ID
                    }).ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetAll_Plan_by_employers_Async),
                    nameof(EmployeeService), "Failed to load Plan dropdown for multiple employers", "DB");
                return new List<SelectListItem>();
            }
        }

        public async Task<List<EmployerLookupDto>> GetAll_Employer_by_employer_Async(int? Emp_ID)
        {
            try
            {
                using (var db = Connection)
                {
                    var parameters = new DynamicParameters();
                    if (Emp_ID == null || Emp_ID == 0) parameters.Add("@Emp_ID", DBNull.Value);
                    else parameters.Add("@Emp_ID", Emp_ID);

                    parameters.Add("@PageIndex", 1);
                    parameters.Add("@PageSize", 1000);
                    parameters.Add("@Search", "");

                    var result = await db.QueryAsync<dynamic>("sp_EmployerList", parameters, commandType: CommandType.StoredProcedure);

                    return result.Select(c => {
                        string name = c.name;
                        return new EmployerLookupDto
                        {
                            Id = (int)c.id,
                            Name = name,
                            IsPrimary = c.companyId == null,
                            // Uses your AvatarExtensions logic
                            Initials = ACA360.Core.Extensions.AvatarExtensions.GetInitials(name),
                            ColorClass = ACA360.Core.Extensions.AvatarExtensions.GetAvatarColor(name)
                        };
                    })
                    .OrderByDescending(x => x.IsPrimary)
                    .ThenBy(x => x.Name)
                    .ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetAll_Employer_by_employer_Async),
                    nameof(EmployeeService), "Failed to load Employer dropdown", "DB");
                return new List<EmployerLookupDto>();
            }

        }

        #endregion

        #region 3.filters
        public async Task<(List<EmployeeFilterRequest> Records, PaginationViewEntity Pagination)> GetEmployeesAsync(EmployeeFilterRequest filter)
        {
            var records = new List<EmployeeFilterRequest>();
            PaginationViewEntity pagination = null!;

            // 1. Prepare DataTables for Table-Valued Parameters (TVPs)
            // These must match the column names and types defined in SQL User-Defined Table Types

            // dbo.BigIntList
            var empTVP = new DataTable();
            empTVP.Columns.Add("Id", typeof(long));
            if (filter.Employer_IDs != null)
            {
                foreach (var id in filter.Employer_IDs) empTVP.Rows.Add(id);
            }

            // dbo.BigIntList
            var planTVP = new DataTable();
            planTVP.Columns.Add("Id", typeof(long));
            if (filter.PlanIDs != null)
            {
                foreach (var id in filter.PlanIDs) planTVP.Rows.Add(id);
            }

            // dbo.BigIntList
            var otherTVP = new DataTable();
            otherTVP.Columns.Add("Id", typeof(long));
            if (filter.OtherIDs != null)
            {
                foreach (var id in filter.OtherIDs) otherTVP.Rows.Add(id);
            }

            // dbo.StringList
            var statusTVP = new DataTable();
            statusTVP.Columns.Add("Value", typeof(string));
            if (filter.Statuses != null)
            {
                foreach (var status in filter.Statuses) statusTVP.Rows.Add(status.Trim());
            }

            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    var p = new DynamicParameters();

                    // Simple Parameters
                    p.Add("@PageIndex", filter.PageIndex);
                    p.Add("@PageSize", filter.PageSize);
                    p.Add("@SortColumn", filter.SortColumn);
                    p.Add("@SortOrder", filter.SortOrder ?? "asc");
                    p.Add("@FillingYear", filter.FilingYear);
                    p.Add("@FullName", filter.FullName);
                    p.Add("@SSN", filter.SSN);
                    p.Add("@Email", filter.Email);
                    p.Add("@City", filter.City);
                    p.Add("@Zip", filter.Zip);
                    p.Add("@FlagReason", filter.FlagReason);
                    p.Add("@UserID", filter.UserID);

                    // Table-Valued Parameters
                    // The string name here ("dbo.BigIntList") MUST match the Type name in SQL Server
                    p.Add("@EmployerIDs", empTVP.AsTableValuedParameter("dbo.BigIntList"));
                    p.Add("@PlanIDs", planTVP.AsTableValuedParameter("dbo.BigIntList"));
                    p.Add("@OtherIDs", otherTVP.AsTableValuedParameter("dbo.BigIntList"));
                    p.Add("@Statuses", statusTVP.AsTableValuedParameter("dbo.StringList"));

                    // Execute Stored Procedure
                    using (var multi = await connection.QueryMultipleAsync("dbo.usp_GetEmployeeDetails_Advanced_dt", p, commandType: CommandType.StoredProcedure))
                    {
                        // 2. Read Result Set 1: Employee Records
                        // We use 'dynamic' here to manually map columns that don't match property names exactly
                        var rawData = await multi.ReadAsync<dynamic>();

                        foreach (var row in rawData)
                        {
                            records.Add(new EmployeeFilterRequest
                            {
                                Id = (int)row.EmployeeID, // Mapped from alias in SP
                                FirstName = row.firstName,
                                LastName = row.lastName,
                                Suffix = row.suffix,
                                Status = row.EmployeeStatus, // Mapped from alias in SP
                                SSN = row.ssn
                                // Add other properties here as needed based on your SP output
                            });
                        }

                        // 3. Read Result Set 2: Pagination Metadata
                        var metaRow = await multi.ReadFirstOrDefaultAsync<dynamic>();

                        if (metaRow != null)
                        {
                            int totalItems = (int)metaRow.TotalCount;
                            int pageSize = filter.PageSize > 0 ? filter.PageSize : 10;
                            int currentPage = filter.PageIndex > 0 ? filter.PageIndex : 1;
                            int totalPages = (int)Math.Ceiling((double)totalItems / pageSize);
                            int startPage = ((currentPage - 1) / 10) * 10 + 1;
                            int endPage = Math.Min(startPage + 9, totalPages);

                            pagination = new PaginationViewEntity
                            {
                                TotalItems = totalItems,
                                CurrentPage = currentPage,
                                PageSize = pageSize,
                                TotalPages = totalPages,
                                StartPage = startPage,
                                EndPage = endPage,
                                SortColumn = filter.SortColumn,
                                SortOrder = filter.SortOrder,
                                filingYear = filter.FilingYear,
                                RecordCount = records.Count,
                                PageNumber = currentPage,
                                TotalCount = totalItems
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetEmployeesAsync), nameof(EmployeeService), "Failed to execute advanced employee search using Dapper.", "DB");
                throw;
            }

            // Ensure pagination is not null to prevent UI crashes
            if (pagination == null)
            {
                pagination = new PaginationViewEntity { TotalItems = 0, TotalPages = 0, CurrentPage = 1, PageSize = filter.PageSize };
            }

            return (records, pagination);
        }
        #endregion       

        #region 4.InsertOrUpdate Employee Details

        // Keeps only the client-declared field(s) from the posted model; every other
        // basic field is reset to its stored value. 
        private static void RevertUndeclaredBasicFields(EmployeeBasicDetails posted, EmployeeBasicDetails stored, List<string> declared)
        {
            var keep = new HashSet<string>(
                declared.Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.OrdinalIgnoreCase);

            // Editing a dropdown changes both the text and the id — keep them as a pair.
            if (keep.Contains("State")) keep.Add("StateId");
            if (keep.Contains("StateId")) keep.Add("State");
            if (keep.Contains("Country")) keep.Add("CountryId");
            if (keep.Contains("CountryId")) keep.Add("Country");

            if (!keep.Contains("FirstName")) posted.FirstName = stored.FirstName;
            if (!keep.Contains("MiddleName")) posted.MiddleName = stored.MiddleName;
            if (!keep.Contains("LastName")) posted.LastName = stored.LastName;
            if (!keep.Contains("Suffix")) posted.Suffix = stored.Suffix;
            if (!keep.Contains("SSN")) posted.SSN = stored.SSN;
            if (!keep.Contains("Birthday")) posted.Birthday = stored.Birthday;
            if (!keep.Contains("Email")) posted.Email = stored.Email;
            if (!keep.Contains("Address")) posted.Address = stored.Address;
            if (!keep.Contains("Address2")) posted.Address2 = stored.Address2;
            if (!keep.Contains("City")) posted.City = stored.City;
            if (!keep.Contains("State")) posted.State = stored.State;
            if (!keep.Contains("StateId")) posted.StateId = stored.StateId;
            if (!keep.Contains("Zip")) posted.Zip = stored.Zip;
            if (!keep.Contains("Country")) posted.Country = stored.Country;
            if (!keep.Contains("CountryId")) posted.CountryId = stored.CountryId;
            if (!keep.Contains("IsForeign")) posted.IsForeign = stored.IsForeign;
            if (!keep.Contains("IsExPatriot")) posted.IsExPatriot = stored.IsExPatriot;
            if (!keep.Contains("getForm")) posted.getForm = stored.getForm;
            if (!keep.Contains("IsCorrected")) posted.IsCorrected = stored.IsCorrected;
            if (!keep.Contains("ConsentedToElectronic")) posted.ConsentedToElectronic = stored.ConsentedToElectronic;
        }
        public async Task<bool> InsertOrUpdateEmployeeBasicDetails(EmployeeBasicDetails employeeBasicDetails)
        {
            try
            {
                // AUDIT FIX: Fetch Old Data
                EmployeeBasicDetails oldRecord = null;
                try { if (employeeBasicDetails.EmployeeID != "0") { oldRecord = await GetEmployeeBasicDetails(employeeBasicDetails.EmployeeID, employeeBasicDetails.FilingYear); } }
                catch { } // Ex doesn't exist = Insert

                string actionType = oldRecord == null ? "INSERT" : "UPDATE";
                //  SINGLE-FIELD EDIT GUARD (double-click flow) — if the client declared
                // which field(s) it opened for edit, force every OTHER basic field back to its stored
                // value. This rejects any change made outside the double-click UI (e.g. a field enabled
                // via browser Inspect) — it simply is not saved. Full-section edits send no EditedFields.
                if (oldRecord != null && employeeBasicDetails.EditedFields != null && employeeBasicDetails.EditedFields.Count > 0)
                {
                    RevertUndeclaredBasicFields(employeeBasicDetails, oldRecord, employeeBasicDetails.EditedFields);
                }
                using (var db = Connection)
                {
                    var p = new DynamicParameters();
                    p.Add("@EmployeeID", employeeBasicDetails.EmployeeID);
                    p.Add("@IsCorrected", employeeBasicDetails.IsCorrected);
                    p.Add("@FirstName", string.IsNullOrWhiteSpace(employeeBasicDetails.FirstName) ? null : employeeBasicDetails.FirstName);
                    p.Add("@MiddleName", string.IsNullOrWhiteSpace(employeeBasicDetails.MiddleName) ? null : employeeBasicDetails.MiddleName);
                    p.Add("@LastName", string.IsNullOrWhiteSpace(employeeBasicDetails.LastName) ? null : employeeBasicDetails.LastName);
                    p.Add("@Email", string.IsNullOrWhiteSpace(employeeBasicDetails.Email) ? null : employeeBasicDetails.Email);
                    p.Add("@SSN", string.IsNullOrWhiteSpace(employeeBasicDetails.SSN) ? null : employeeBasicDetails.SSN);
                    p.Add("@Suffix", string.IsNullOrWhiteSpace(employeeBasicDetails.Suffix) ? null : employeeBasicDetails.Suffix);
                    p.Add("@Birthday", employeeBasicDetails.Birthday);
                    p.Add("@Address", string.IsNullOrWhiteSpace(employeeBasicDetails.Address) ? null : employeeBasicDetails.Address);
                    p.Add("@Address2", string.IsNullOrWhiteSpace(employeeBasicDetails.Address2) ? null : employeeBasicDetails.Address2);
                    p.Add("@City", string.IsNullOrWhiteSpace(employeeBasicDetails.City) ? null : employeeBasicDetails.City);
                    p.Add("@State", string.IsNullOrWhiteSpace(employeeBasicDetails.State) ? null : employeeBasicDetails.State);
                    p.Add("@Zip", string.IsNullOrWhiteSpace(employeeBasicDetails.Zip) ? null : employeeBasicDetails.Zip);
                    p.Add("@Country", string.IsNullOrWhiteSpace(employeeBasicDetails.Country) ? null : employeeBasicDetails.Country);
                    p.Add("@IsForeign", employeeBasicDetails.IsForeign);
                    p.Add("@IsExPatriot", employeeBasicDetails.IsExPatriot);
                    p.Add("@getForm", employeeBasicDetails.getForm);
                    p.Add("@ConsentedToElectronic", employeeBasicDetails.ConsentedToElectronic);
                    p.Add("@EmployerId", employeeBasicDetails.EmployerId);
                    p.Add("@FilingYear", employeeBasicDetails.FilingYear);
                    p.Add("@StateId", employeeBasicDetails.StateId);
                    p.Add("@CountryId", employeeBasicDetails.CountryId);
                    await db.ExecuteAsync("usp_employee_basic_details_action", p, commandType: CommandType.StoredProcedure);
                }
                // ==========================================
                // AUDIT FIX: ISOLATE ONLY THE BASIC FIELDS
                // Strip away all Lists and complex objects so the logger only compares strings/dates
                // ==========================================
                var oldBasic = oldRecord == null ? null : new
                {
                    oldRecord.FirstName,
                    oldRecord.MiddleName,
                    oldRecord.LastName,
                    oldRecord.SSN,
                    oldRecord.Suffix,
                    oldRecord.Birthday,
                    oldRecord.Address,
                    oldRecord.Address2,
                    oldRecord.City,
                    oldRecord.State,
                    oldRecord.StateId,
                    oldRecord.Zip,
                    oldRecord.Country,
                    oldRecord.IsForeign,
                    oldRecord.IsExPatriot,
                    oldRecord.IsCorrected,
                    oldRecord.ConsentedToElectronic,
                    oldRecord.Email
                };

                var newBasic = new
                {
                    employeeBasicDetails.FirstName,
                    employeeBasicDetails.MiddleName,
                    employeeBasicDetails.LastName,
                    employeeBasicDetails.SSN,
                    employeeBasicDetails.Suffix,
                    employeeBasicDetails.Birthday,
                    employeeBasicDetails.Address,
                    employeeBasicDetails.Address2,
                    employeeBasicDetails.City,
                    employeeBasicDetails.State,
                    employeeBasicDetails.StateId,
                    employeeBasicDetails.Zip,
                    employeeBasicDetails.Country,
                    employeeBasicDetails.IsForeign,
                    employeeBasicDetails.IsExPatriot,
                    employeeBasicDetails.IsCorrected,
                    employeeBasicDetails.ConsentedToElectronic,
                    employeeBasicDetails.Email
                };

                // Pass the clean, flat objects to the logger
                // AUDIT FIX: Log the change!
                await _auditLogger.CaptureAndLogChangeAsync("EmployeeBasicDetails", employeeBasicDetails.EmployeeID ?? string.Empty, actionType, oldBasic, newBasic);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(InsertOrUpdateEmployeeBasicDetails),
                    nameof(EmployeeService),
                    $"Failed: Employee ID: {employeeBasicDetails.EmployeeID}", "DB");

                // Bubble up the exact error instead of swallowing it as 'return false'
                throw new InvalidOperationException(ex.Message, ex);
            }
        }



        public Task<bool> InsertOrUpdateemployeeHireDetails(DataTable hires, string EmployeeID)
      => ExecuteTVPAsync<EmployeeHireDetails>("usp_employee_hire_details_action", "@HireDetails", hires, "EmployeeHireSpan", "Id", EmployeeID);

        public Task<bool> InsertOrUpdateemployeestatus(DataTable status, string EmployeeID)
            => ExecuteTVPAsync<EmployeeStatus>("usp_employee_status_details_action", "@StatusDetails", status, "EmployeeStatus", "StatusId", EmployeeID);

        public Task<bool> InsertOrUpdateemployeepayrollInfo(DataTable payroll, string EmployeeID)
            => ExecuteTVPAsync<EmployeePayroll>("usp_employee_payroll_details_action", "@PayrollDetails", payroll, "EmployeePayroll", "Id", EmployeeID);

        public Task<bool> InsertOrUpdateemployeemedical(DataTable medical, string EmployeeID)
            => ExecuteTVPAsync<EmployeeEnrollmentInfo>("usp_employee_medical_details_action", "@MedicalDetails", medical, "EmployeeEnrollment", "Id", EmployeeID);

        public Task<bool> InsertOrUpdatecobra(DataTable cobra, string EmployeeID)
            => ExecuteTVPAsync<EmployeeEnrollmentInfo>("usp_employee_cobra_details_action", "@CobraDetails", cobra, "EmployeeEnrollment", "Id", EmployeeID);

        public Task<bool> InsertOrUpdateunion(DataTable union, string EmployeeID)
            => ExecuteTVPAsync<EmployeeEnrollmentInfo>("usp_employee_union_details_action", "@UnionDetails", union, "EmployeeEnrollment", "Id", EmployeeID);

        public Task<bool> InsertOrUpdateretiree(DataTable retiree, string EmployeeID)
            => ExecuteTVPAsync<EmployeeEnrollmentInfo>("usp_employee_retiree_details_action", "@RetireeDetails", retiree, "EmployeeEnrollment", "Id", EmployeeID);

        public Task<bool> InsertOrUpdateemployeeDependentDetails(DataTable dependent, string EmployeeID)
            => ExecuteTVPAsync<CoveredIndividualModel>("usp_employee_dependent_details_action", "@DependentDetails", dependent, "CoveredIndividual", "Id", EmployeeID);

        public async Task<bool> UpdateMonthlyCodesAsync(EmployeeCode input, string modifiedBy)
        {
            using var connection = new SqlConnection(_connectionString);
            // 1. Fetch existing record using Dapper
            string selectSql = "SELECT * FROM EmployeeCode WHERE id = @Id";

            // We map to a DTO/Model that matches your DB table exactly
            var currentEntity = await connection.QueryFirstOrDefaultAsync<EmployeeCode>(selectSql, new { Id = input.EmployeeCodeID });

            if (currentEntity == null) return false; // Record not found

            // 2. Setup for Dynamic Update
            var months = new[] { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };
            var types = new[] { "COC", "LCMP", "SHC" };

            var updateQuery = new StringBuilder("UPDATE EmployeeCode SET ");
            var parameters = new DynamicParameters();
            parameters.Add("@Id", input.EmployeeCodeID); // Use ID for WHERE clause

            bool hasChanges = false;

            // 3. Loop through properties to find changes
            foreach (var m in months)
            {
                foreach (var t in types)
                {
                    // Example Property Name: "JAN_COC"
                    string propName = $"{m}_{t}";

                    // Get New Value from Input Model
                    var newValue = input.GetType().GetProperty(propName)?.GetValue(input)?.ToString();

                    // Get Old Value from Database Entity
                    var dbProp = currentEntity.GetType().GetProperty(propName);
                    var oldValue = dbProp?.GetValue(currentEntity)?.ToString();

                    // Compare
                    if (oldValue != newValue)
                    {
                        // Append to SQL: "JAN_COC = @JAN_COC, "
                        updateQuery.Append($"{propName} = @{propName}, ");

                        // Add value to Dapper Parameters
                        parameters.Add($"@{propName}", newValue);

                        hasChanges = true;
                    }
                }
            }

            // 4. Execute Update if changes exist
            if (hasChanges)
            {
                // Update Metadata (if you have these columns)
                // updateQuery.Append("ModifiedBy = @ModifiedBy, ModifiedDate = @ModifiedDate, ");
                // parameters.Add("@ModifiedBy", modifiedBy);
                // parameters.Add("@ModifiedDate", DateTime.Now);

                // Remove the last comma and space ", "
                string sql = updateQuery.ToString().TrimEnd(',', ' ');

                // Add Where Clause
                sql += " WHERE id = @Id";

                await connection.ExecuteAsync(sql, parameters);
            }

            return true;
        }

        public async Task<bool> UpdateCoveredIndividualCoverageAsync(CoveredIndividualModel model)
        {
            try
            {
                using (var db = Connection)
                {
                    // 1. Fetch Old Data for Audit Logging
                    // Dapper returns a dynamic object that implements IDictionary<string, object>
                    var oldRecord = await db.QueryFirstOrDefaultAsync(
                        "SELECT * FROM CoveredIndividualCode WHERE id = @Id AND filingYear = @FilingYear",
                        new { Id = model.Id, FilingYear = model.FilingYear }
                    );

                    // 2. Prepare Parameters for the Update Stored Procedure
                    var parameters = new DynamicParameters();
                    parameters.Add("@Id", model.Id, DbType.Int32);
                    parameters.Add("@FilingYear", model.FilingYear, DbType.Int32);
                    parameters.Add("@DisableCoding", model.CI_chk_disable_coding ? 1 : 0, DbType.Int16);
                    parameters.Add("@AllM", model.AllM ? 1 : 0, DbType.Int16);
                    parameters.Add("@Jan", model.Jan ? 1 : 0, DbType.Int16);
                    parameters.Add("@Feb", model.Feb ? 1 : 0, DbType.Int16);
                    parameters.Add("@Mar", model.Mar ? 1 : 0, DbType.Int16);
                    parameters.Add("@Apr", model.Apr ? 1 : 0, DbType.Int16);
                    parameters.Add("@May", model.May ? 1 : 0, DbType.Int16);
                    parameters.Add("@Jun", model.Jun ? 1 : 0, DbType.Int16);
                    parameters.Add("@Jul", model.Jul ? 1 : 0, DbType.Int16);
                    parameters.Add("@Aug", model.Aug ? 1 : 0, DbType.Int16);
                    parameters.Add("@Sep", model.Sep ? 1 : 0, DbType.Int16);
                    parameters.Add("@Oct", model.Oct ? 1 : 0, DbType.Int16);
                    parameters.Add("@Nov", model.Nov ? 1 : 0, DbType.Int16);
                    parameters.Add("@Dec", model.Dec ? 1 : 0, DbType.Int16);

                    // 3. Execute the Update
                    // NOTE: Make sure sp_UpdateCoveredIndividualCoverage does NOT have 'SET NOCOUNT ON;' 
                    // or use ExecuteScalarAsync if it does.
                    int rowsAffected = await db.ExecuteAsync(
                        "sp_UpdateCoveredIndividualCoverage",
                        parameters,
                        commandType: CommandType.StoredProcedure
                    );

                    // 4. Handle Audit Logging if the update was successful
                    if (rowsAffected > 0 && oldRecord != null)
                    {
                        // Cast the dynamic Dapper row to a dictionary for easy lookups
                        var oldDict = (IDictionary<string, object>)oldRecord;

                        var changedOld = new Dictionary<string, object>();
                        var changedNew = new Dictionary<string, object>();

                        // Local helper function to compare and track differences safely
                        void CheckDiff(string column, int newValue)
                        {
                            if (oldDict.TryGetValue(column, out var oldValue))
                            {
                                // Convert DB value to int safely (handles bit, tinyint, smallint differences)
                                int dbValue = Convert.ToInt32(oldValue ?? 0);

                                if (dbValue != newValue)
                                {
                                    changedOld[column] = dbValue;
                                    changedNew[column] = newValue;
                                }
                            }
                        }

                        // Check all updatable fields
                        CheckDiff("disableCoding", model.CI_chk_disable_coding ? 1 : 0);
                        CheckDiff("AllM", model.AllM ? 1 : 0);
                        CheckDiff("Jan", model.Jan ? 1 : 0);
                        CheckDiff("Feb", model.Feb ? 1 : 0);
                        CheckDiff("Mar", model.Mar ? 1 : 0);
                        CheckDiff("Apr", model.Apr ? 1 : 0);
                        CheckDiff("May", model.May ? 1 : 0);
                        CheckDiff("Jun", model.Jun ? 1 : 0);
                        CheckDiff("Jul", model.Jul ? 1 : 0);
                        CheckDiff("Aug", model.Aug ? 1 : 0);
                        CheckDiff("Sep", model.Sep ? 1 : 0);
                        CheckDiff("Oct", model.Oct ? 1 : 0);
                        CheckDiff("Nov", model.Nov ? 1 : 0);
                        CheckDiff("Dec", model.Dec ? 1 : 0);

                        // Only log if there are actual changes
                        if (changedNew.Count > 0)
                        {
                            // Pass the filtered dictionaries to the centralized audit logger
                            await _auditLogger.CaptureAndLogChangeAsync(
                                "CoveredIndividualCode",
                                model.Id.ToString(),
                                "UPDATE",
                                changedOld,
                                changedNew
                            );
                        }
                    }

                    return rowsAffected > 0;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(UpdateCoveredIndividualCoverageAsync), nameof(EmployeeService), $"Failed to update coverage for CI ID: {model.Id}", "DB");
                throw;
            }
        }

        // ==========================================
        // THE UPGRADED TVP METHOD
        // ==========================================
        // Notice the <T> added to the method signature
        private async Task<bool> ExecuteTVPAsync<T>(string spName, string parameterName, DataTable data, string tableName, string primaryKeyColumn, string EmployeeID) where T : class
        {
            try
            {
                using (var db = new SqlConnection(_connectionString))
                {
                    await db.OpenAsync();

                    string matchColumn = primaryKeyColumn;
                    bool allIdsZero = data.AsEnumerable().All(r => r[primaryKeyColumn]?.ToString() == "0" || string.IsNullOrEmpty(r[primaryKeyColumn]?.ToString()));

                    if (allIdsZero && data.Columns.Contains("EmployeeCodeId"))
                    {
                        matchColumn = "EmployeeCodeId";
                    }

                    // 1. FAST FETCH OLD RECORDS
                    List<T> oldRecordsList = new List<T>();
                    if (data != null && data.Rows.Count > 0 && data.Columns.Contains(matchColumn))
                    {
                        var keyValues = data.AsEnumerable()
                                            .Select(row => row[matchColumn]?.ToString())
                                            .Where(id => !string.IsNullOrEmpty(id) && id != "0")
                                            .Distinct()
                                            .ToList();

                        if (keyValues.Any())
                        {
                            // ==========================================
                            // ULTIMATE FIX: DYNAMIC SQL GENERATOR
                            // ==========================================
                            var props = typeof(T).GetProperties();
                            var selectColumns = new List<string>();

                            foreach (var prop in props)
                            {
                                // 1. Check if the property has [NotMapped]. If yes, skip it entirely!
                                var isNotMapped = prop.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute), false).Any();
                                if (isNotMapped) continue;

                                // 2. Otherwise, map the column normally
                                var columnAttr = prop.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.Schema.ColumnAttribute), false).FirstOrDefault() as System.ComponentModel.DataAnnotations.Schema.ColumnAttribute;
                                string sqlColName = columnAttr != null ? columnAttr.Name : prop.Name;

                                selectColumns.Add($"[{sqlColName}] AS [{prop.Name}]");
                            }

                            string selectQuery = string.Join(", ", selectColumns);

                            // Ensure the WHERE clause uses the actual SQL column name
                            var matchProp = props.FirstOrDefault(p => p.Name.Equals(matchColumn, StringComparison.OrdinalIgnoreCase));
                            var matchAttr = matchProp?.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.Schema.ColumnAttribute), false).FirstOrDefault() as System.ComponentModel.DataAnnotations.Schema.ColumnAttribute;
                            string dbMatchColumn = matchAttr != null ? matchAttr.Name : matchColumn;

                            string fetchSql = $"SELECT {selectQuery} FROM {tableName} WHERE [{dbMatchColumn}] IN @Keys";

                            var fetchResult = await db.QueryAsync<T>(fetchSql, new { Keys = keyValues });
                            oldRecordsList = fetchResult.ToList();
                        }
                    }

                    // 2. EXECUTE TVP UPLOAD
                    var p = new DynamicParameters();
                    p.Add(parameterName, data.AsTableValuedParameter());
                    await db.ExecuteAsync(spName, p, commandType: CommandType.StoredProcedure);

                    // 3. IN-MEMORY DELTA CALCULATION
                    if (data != null && data.Rows.Count > 0)
                    {
                        var props = typeof(T).GetProperties();
                        var oldRecordsDict = new Dictionary<string, Dictionary<string, object>>();

                        foreach (var item in oldRecordsList)
                        {
                            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                            foreach (var prop in props) dict[prop.Name] = prop.GetValue(item);

                            var matchVal = dict.ContainsKey(matchColumn) ? dict[matchColumn]?.ToString() : "";
                            if (!string.IsNullOrEmpty(matchVal)) oldRecordsDict[matchVal] = dict;
                        }

                        var bulkOldDeltas = new List<Dictionary<string, object>>();
                        var bulkNewDeltas = new List<Dictionary<string, object>>();

                        foreach (DataRow row in data.Rows)
                        {
                            string rowMatchValue = row[matchColumn]?.ToString();
                            var oldRowDelta = new Dictionary<string, object>();
                            var newRowDelta = new Dictionary<string, object>();
                            bool hasChanges = false;

                            if (!string.IsNullOrEmpty(rowMatchValue) && rowMatchValue != "0" && oldRecordsDict.TryGetValue(rowMatchValue, out var oldRow))
                            {
                                oldRowDelta[matchColumn] = rowMatchValue;
                                newRowDelta[matchColumn] = rowMatchValue;

                                foreach (DataColumn col in data.Columns)
                                {
                                    string colName = col.ColumnName;
                                    string newValueStr = row[col] == DBNull.Value ? "" : row[col].ToString();

                                    oldRow.TryGetValue(colName, out var oldValObj);
                                    string oldValueStr = oldValObj?.ToString() ?? "";

                                    if (newValueStr != oldValueStr)
                                    {
                                        if (DateTime.TryParse(newValueStr, out var newDate) && DateTime.TryParse(oldValueStr, out var oldDate))
                                            if (newDate == oldDate) continue;

                                        if (decimal.TryParse(newValueStr, out var newNum) && decimal.TryParse(oldValueStr, out var oldNum))
                                            if (newNum == oldNum) continue;

                                        oldRowDelta[colName] = oldValObj;
                                        newRowDelta[colName] = row[col] == DBNull.Value ? null : row[col];
                                        hasChanges = true;
                                    }
                                }

                                if (hasChanges)
                                {
                                    bulkOldDeltas.Add(oldRowDelta);
                                    bulkNewDeltas.Add(newRowDelta);
                                }
                            }
                            else
                            {
                                newRowDelta[matchColumn] = "New";
                                foreach (DataColumn col in data.Columns)
                                {
                                    if (row[col] != DBNull.Value)
                                    {
                                        newRowDelta[col.ColumnName] = row[col];
                                        hasChanges = true;
                                    }
                                }
                                if (hasChanges) bulkNewDeltas.Add(newRowDelta);
                            }
                        }

                        // 4. LOG BATCH
                        if (bulkNewDeltas.Any())
                        {
                            await _auditLogger.CaptureAndLogChangeAsync(tableName, EmployeeID, "BULK_UPSERT",
                                bulkOldDeltas.Any() ? bulkOldDeltas : null, bulkNewDeltas);
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, spName, nameof(EmployeeService), "Failed to save TVP records", "DB");
                return false;
            }
        }

        #endregion

        #region 5.DeleteEmployee        
        //Delete
        public async Task DeleteEmployeeSoft(int employeeId)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            await connection.ExecuteAsync(
                "sp_SoftDeleteEmployee",
                new { EmployeeId = employeeId },
                commandType: CommandType.StoredProcedure
            );
        }
        #endregion

        #region ProcessApiImportAsync        
        // Add this method to the class
        public async Task<ApiImportResult> ProcessApiImportAsync(int employerId, EmployeePushRequest request)
        {
            var result = new ApiImportResult { TransactionId = Guid.NewGuid() };
            using var db = Connection;
            db.Open();
            using var tran = db.BeginTransaction();

            try
            {
                foreach (var emp in request.Employees)
                {
                    // 1. Upsert Employee
                    string upsertSql = @"
                MERGE INTO Employee AS Target
                USING (SELECT @EmpId AS EmployerId, @SSN AS Ssn) AS Source
                ON Target.EmployerId = Source.EmployerId AND Target.ssn = Source.Ssn
                WHEN MATCHED THEN
                    UPDATE SET firstName = @First, lastName = @Last, [Address] = @Addr, City = @City, Zip = @Zip
                WHEN NOT MATCHED THEN
                    INSERT (EmployerId, ssn, firstName, lastName, [Address], City, Zip, IsDeleted)
                    VALUES (@EmpId, @SSN, @First, @Last, @Addr, @City, @Zip, 0);
                SELECT id FROM Employee WHERE EmployerId = @EmpId AND ssn = @SSN;";

                    int employeeId = await db.QuerySingleAsync<int>(upsertSql, new
                    {
                        EmpId = employerId,
                        emp.SSN,
                        First = emp.FirstName,
                        Last = emp.LastName,
                        Addr = emp.Address1,
                        emp.City,
                        emp.Zip
                    }, tran);

                    // 2. Insert/Update Codes
                    string codeSql = @"
                MERGE INTO EmployeeCode AS Target
                USING (SELECT @Id AS EmpId, @Yr AS Yr) AS Source
                ON Target.employeeId = Source.EmpId AND Target.filingYear = Source.Yr
                WHEN MATCHED THEN
                    UPDATE SET ALLM_COC = @C14, ALLM_SHC = @C16, ALLM_EESMC = @Prem
                WHEN NOT MATCHED THEN
                    INSERT (employeeId, employerId, filingYear, ALLM_COC, ALLM_SHC, ALLM_EESMC, getForm)
                    VALUES (@Id, @EmpId, @Yr, @C14, @C16, @Prem, 1);";

                    await db.ExecuteAsync(codeSql, new
                    {
                        Id = employeeId,
                        EmpId = employerId,
                        Yr = request.Year,
                        C14 = emp.PlanOfferCode_All12,
                        C16 = emp.SafeHarborCode_All12,
                        Prem = emp.MonthlyPremium
                    }, tran);
                }

                tran.Commit();
                result.Success = true;
                result.RecordsProcessed = request.Employees.Count;
            }
            catch (Exception ex)
            {
                tran.Rollback();
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }
        #endregion

        #region BulkFixFlaggedEmployees

        public async Task<FlaggedGridSaveResult> BulkFixFlaggedEmployeesAsync(
            IEnumerable<FlaggedEmployeeEdit> edits, int filingYear,
            CancellationToken cancellationToken = default)
        {
            if (filingYear < 1000 || filingYear > 9999)
                throw new ArgumentException("Invalid filing year.");
            if (edits == null)
                throw new ArgumentException("No changes to save.");

            var incoming = edits.Take(1001).ToList();
            if (incoming.Count == 0 || incoming.Count > 1000)
                throw new ArgumentException("Save between 1 and 1000 edited grid rows at a time.");
            if (incoming.Any(e => e == null || e.EmployeeId <= 0 || e.EmployeeCodeId <= 0 ||
                e.Changes == null || e.Changes.Count == 0 || e.Changes.Count > 50))
                throw new ArgumentException("Each edit needs employee IDs and a Changes dictionary. Reopen the updated grid.");

            // Snapshot the complete sparse contract. Do not map this batch to legacy full rows.
            var editList = incoming.Select(e => new FlaggedEmployeeEdit
            {
                EmployeeId = e.EmployeeId,
                EmployeeCodeId = e.EmployeeCodeId,
                HireSpanId = e.HireSpanId,
                EnrollmentId = e.EnrollmentId,
                StatusId = e.StatusId,
                PayrollId = e.PayrollId,
                CoveredIndividualId = e.CoveredIndividualId,
                Changes = new Dictionary<string, string?>(e.Changes, StringComparer.Ordinal)
            }).ToList();

            cancellationToken.ThrowIfCancellationRequested();
            var saveTimer = System.Diagnostics.Stopwatch.StartNew();
            FlaggedGridSaveResult result;
            try
            {
                // Exactly ONE database save. SQL checks ownership/year/columns and commits atomically.
                result = await _flagRepository.SaveFlaggedGridAsync(editList, filingYear, cancellationToken);
            }
            finally
            {
                WriteFlaggedTiming("repository-save", saveTimer.ElapsedMilliseconds, editList.Count);
            }

            // The database has committed by this point. Do not throw due to request cancellation
            // here, or report an audit/diagnostic failure as a failed database transaction.
            result.Warnings ??= new List<string>();
            var audits = result.AuditChanges ?? new List<FlaggedGridAuditChange>();
            var auditTimer = System.Diagnostics.Stopwatch.StartNew();
            long slowestAuditMs = 0;
            int auditCalls = 0;
            int auditFailuresBefore = result.AuditFailures;

            foreach (var audit in audits)
            {
                var itemTimer = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    using var oldDoc = JsonDocument.Parse(audit.OldValuesJson);
                    using var newDoc = JsonDocument.Parse(audit.NewValuesJson);
                    if (oldDoc.RootElement.ValueKind != JsonValueKind.Object ||
                        newDoc.RootElement.ValueKind != JsonValueKind.Object)
                        throw new InvalidOperationException("Audit values must be JSON objects.");

                    var oldDelta = new Dictionary<string, object?>(StringComparer.Ordinal);
                    var newDelta = new Dictionary<string, object?>(StringComparer.Ordinal);
                    foreach (var property in newDoc.RootElement.EnumerateObject())
                    {
                        oldDoc.RootElement.TryGetProperty(property.Name, out var oldValue);
                        if (oldValue.ValueKind != JsonValueKind.Undefined &&
                            oldValue.GetRawText() == property.Value.GetRawText())
                            continue;
                        oldDelta[property.Name] = ToFlaggedAuditValue(oldValue);
                        newDelta[property.Name] = ToFlaggedAuditValue(property.Value);
                    }
                    if (newDelta.Count == 0) continue;

                    if (audit.TableName != "Employee")
                    {
                        oldDelta["RowId"] = newDelta["RowId"] = audit.RowId;
                        oldDelta["EmployeeCodeId"] = newDelta["EmployeeCodeId"] = audit.EmployeeCodeId;
                    }

                    auditCalls++;
                    // Preserve sequential use of the existing audit logger: its connection/context
                    // ownership is unknown. A real batch writer is needed to remove these round trips.
                    await _auditLogger.CaptureAndLogChangeAsync(
                        audit.TableName == "Employee" ? "EmployeeBasicDetails" : audit.TableName,
                        audit.EmployeeId.ToString(), "UPDATE", oldDelta, newDelta);
                }
                catch (Exception ex)
                {
                    result.AuditFailures++;
                    try
                    {
                        // Preserves the custom logger signature supplied in your application.
                        _logger.LogError(ex, nameof(BulkFixFlaggedEmployeesAsync), nameof(EmployeeService),
                            $"Data saved; audit failed for employee {audit?.EmployeeId}, table {audit?.TableName}, row {audit?.RowId}.", "AUDIT");
                    }
                    catch (Exception)
                    {
                        // A secondary logger failure must not turn committed data into a failed save response.
                        WriteFlaggedTiming("audit-error-logger-failed", itemTimer.ElapsedMilliseconds, 1);
                    }
                }
                finally
                {
                    slowestAuditMs = Math.Max(slowestAuditMs, itemTimer.ElapsedMilliseconds);
                }
            }

            WriteFlaggedTiming("audit-total", auditTimer.ElapsedMilliseconds, auditCalls);
            WriteFlaggedTiming("audit-slowest-row", slowestAuditMs, audits.Count);
            int newAuditFailures = result.AuditFailures - auditFailuresBefore;
            if (newAuditFailures > 0)
                result.Warnings.Add($"Data was saved, but {newAuditFailures} audit record(s) could not be written. Contact your administrator.");

            // Neither the supplied service nor the supplied SAVE procedure invokes the flag engine.
            // Report this honestly; do not delete EmployeeFlags or claim a rule is fixed automatically.
            // Once the actual rule-engine integration is provided, replace this block with its awaited
            // refresh/durable-enqueue result. Account for cross-code/year dependencies of Employee edits.
            result.FlagRefreshRequired = result.SavedCells > 0;
            if (result.SavedCells > 0)
            {
                // Run once per employee, even when multiple cells/records were edited.
                foreach (int employeeId in editList.Select(e => e.EmployeeId).Distinct())
                {
                    try
                    {
                        await _acaLogicService.RecalculateFlagsForEmployeeAsync(
                            filingYear, employeeId);
                    }
                    catch (Exception)
                    {
                        // The data is already saved. Validation failure is separate.
                        // ACALogicService logs the actual exception.
                        result.FlagRefreshRequired = true;

                        result.Warnings.Add(
                            $"Changes saved, but flag validation failed for employee " +
                            $"{employeeId}. Run validation again before treating their flags as current.");
                    }
                }
            }
            audits.Clear();
            result.AuditChanges = audits;
            return result;
        }

        private static void WriteFlaggedTiming(string phase, long elapsedMs, int count)
        {
            try
            {
                // No names, SSNs, changed values or audit JSON. Route Trace to your diagnostics sink,
                // or replace this body with your custom logger's known timing/info API.
                System.Diagnostics.Trace.TraceInformation(
                    "FlaggedGrid phase={0}; elapsedMs={1}; count={2}", phase, elapsedMs, count);
            }
            catch (Exception)
            {
                // Diagnostic listeners must never alter save outcomes.
            }
        }

        private static object? ToFlaggedAuditValue(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Undefined:
                case JsonValueKind.Null: return null;
                case JsonValueKind.String: return value.GetString();
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.Number:
                    if (value.TryGetInt64(out var whole)) return whole;
                    if (value.TryGetDecimal(out var number)) return number;
                    return value.GetDouble();
                default: return value.GetRawText();
            }
        }

        #endregion

        public async Task<int?> GetEmployeeIdForCompareAsync(string currentEmployeeId, string filingYear, string? employerId, int? userId)
        {
            try
            {
                if (!int.TryParse(currentEmployeeId, out var cid)) return null;
                if (!int.TryParse(filingYear, out _)) return null;
                var cur = await GetEmployeeByIdAsync(cid);
                if (cur == null || string.IsNullOrWhiteSpace(cur.SSN)) return null;
                var scope = employerId;
                if (string.IsNullOrWhiteSpace(scope) && cur.EmployerId.HasValue) scope = cur.EmployerId.Value.ToString();
                var pe = new PaginationEntity { PageIndex = 1, PageSize = 100000, fillingYear = filingYear, SortColumn = 0, SortOrder = "asc", Search = "" };
                var (list, _) = await GetEmployeesAsync(scope, filingYear, pe, userId, new EmployeeFilterRequest { FilingYear = filingYear });
                var target = NormalizeSsn(cur.SSN);
                var match = list.FirstOrDefault(e => e.Id.HasValue && NormalizeSsn(e.SSN) == target);
                return match?.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetEmployeeIdForCompareAsync), nameof(EmployeeService), $"cur {currentEmployeeId}, year {filingYear}", "Service");
                return null;
            }
        }
        private static string NormalizeSsn(string? s) => new string((s ?? "").Where(char.IsDigit).ToArray());


    }
}
