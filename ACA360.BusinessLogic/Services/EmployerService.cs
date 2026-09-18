using ACA360.Core.Constants;
using ACA360.Core.Extensions;
using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CSharp.RuntimeBinder;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using ACA360.BusinessLogic.Interfaces;

namespace ACA360.BusinessLogic.Services
{
    public class EmployerService : IEmployerService
    {
        private readonly string _connectionString;
        private readonly ILoggerService _logger;
        private readonly IDataAuditlogService _auditLogger; // Added Audit Logger
        public EmployerService(string connectionString, ILoggerService logger, IDataAuditlogService auditLogger)
        {
            _connectionString = connectionString;
            _logger = logger;
            _auditLogger = auditLogger;
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        #region 1. SMART LOGIN & ACCESS METHODS
        // ===================================================================
        // 1. SMART LOGIN & ACCESS METHODS
        // ===================================================================

        public async Task<IEnumerable<Employer>> GetEmployersForUserAsync(string userId, string roleName)
        {
            try
            {
                if (roleName == UserRoles.SuperAdmin || roleName == UserRoles.Admin || roleName == UserRoles.ACADirector)
                {
                    return await GetAllEmployersAsync(userId.ToString());
                }
                else if (roleName == "Employer")
                {
                    var employer = await GetEmployerByUserIdAsync(userId);
                    return employer != null ? new List<Employer> { employer } : new List<Employer>();
                }
                else
                {
                    return await GetEmployersByStaffIdAsync(userId, roleName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetEmployersForUserAsync), nameof(EmployerService), $"Failed for User {userId} ({roleName})", "Service");
                return new List<Employer>();
            }
        }

        public async Task<List<Employer>> GetAllEmployersAsync(string userId)
        {
            try
            {
                using (var db = Connection)
                {
                    var employers = await db.QueryAsync<Employer>(
                        "sp_GetAllEmployers",
                        new { UserID = userId },
                        commandType: CommandType.StoredProcedure);
                    return employers.ToList();
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(GetAllEmployersAsync), nameof(EmployerService), $"SQL error retrieving all employers for UserID {userId}", "Service");
                return new List<Employer>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetAllEmployersAsync), nameof(EmployerService), $"Unexpected error retrieving all employers for UserID {userId}", "Service");
                return new List<Employer>();
            }
        }

        public async Task<List<Employer>> GetEmployersByStaffIdAsync(string staffId, string roleName)
        {
            try
            {
                using (var db = Connection)
                {
                    var result = await db.QueryAsync<Employer>(
                        "sp_GetEmployersByStaffId",
                        new { StaffId = staffId, RoleName = roleName },
                        commandType: CommandType.StoredProcedure);
                    return result.ToList();
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(GetEmployersByStaffIdAsync), nameof(EmployerService), $"SQL error retrieving employers for StaffId {staffId}", "Service");
                return new List<Employer>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetEmployersByStaffIdAsync), nameof(EmployerService), $"Unexpected error retrieving employers for StaffId {staffId}", "Service");
                return new List<Employer>();
            }
        }

        public async Task<Employer?> GetEmployerByUserIdAsync(string userId)
        {
            try
            {
                using (var db = Connection)
                {
                    int? employerId = await db.QuerySingleOrDefaultAsync<int?>(
                        "sp_GetRefIdByUserId",
                        new { UserId = userId },
                        commandType: CommandType.StoredProcedure);

                    if (employerId.HasValue && employerId.Value > 0)
                    {
                        return await GetEmployerDetailsByIdAsync(employerId.Value.ToString());
                    }
                    return null;
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(GetEmployerByUserIdAsync), nameof(EmployerService), $"SQL error fetching employer for UserId {userId}", "Service");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetEmployerByUserIdAsync), nameof(EmployerService), $"Unexpected error fetching employer for UserId {userId}", "Service");
                return null;
            }
        }

        public async Task<Employer?> GetEmployerByEinAsync(string ein)
        {
            if (string.IsNullOrWhiteSpace(ein)) return null;

            try
            {
                using (var db = Connection)
                {
                    // Clean the EIN parameter to ensure a match regardless of formatting
                    string cleanEin = ein.Replace("-", "").Trim();

                    // We check both EIN and TaxId columns just in case, 
                    // and strip hyphens on the DB side for a foolproof match.
                    string query = @"
                        SELECT TOP 1 * FROM Employer 
                        WHERE (REPLACE(TaxId, '-', '') = @CleanEin)
                          AND (IsDeleted = 0 OR IsDeleted IS NULL)";

                    return await db.QueryFirstOrDefaultAsync<Employer>(query, new { CleanEin = cleanEin });
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(GetEmployerByEinAsync), nameof(EmployerService), $"SQL error fetching employer by EIN: {ein}", "Service");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetEmployerByEinAsync), nameof(EmployerService), $"Unexpected error fetching employer by EIN: {ein}", "Service");
                return null;
            }
        }

        public async Task<string?> GetAssignedDataAnalystForEmployerAsync(int employerId)
        {
            try
            {
                using (var db = Connection)
                {
                    // Quickly fetches the mapped DA from the operational assignments table
                    string query = @"
                SELECT TOP 1 DA_UserID 
                FROM tbl_Operational_Assignment 
                WHERE EmployerID = @EmployerId AND IsActive = 1";

                    return await db.ExecuteScalarAsync<string?>(query, new { EmployerId = employerId });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetAssignedDataAnalystForEmployerAsync), "RoutingEngine", $"Failed to find DA for Employer {employerId}");
                return null;
            }
        }
        #endregion

        #region 2. CORE EMPLOYER METHODS
        // ===================================================================
        // 2. CORE EMPLOYER METHODS
        // ===================================================================

        // FIX: Return type changed to string? — method returns null on error/not-found,
        //      which is incompatible with non-nullable string.
        public async Task<string?> GetEmployerNameAsync(int employerId)
        {
            try
            {
                using (var db = Connection)
                {
                    return await db.ExecuteScalarAsync<string?>(
                        "sp_GetEmployerNameById",
                        new { Id = employerId },
                        commandType: CommandType.StoredProcedure);
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(GetEmployerNameAsync), nameof(EmployerService), $"SQL error fetching name for EmployerId {employerId}", "Service");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetEmployerNameAsync), nameof(EmployerService), $"Unexpected error fetching name for EmployerId {employerId}", "Service");
                return null;
            }
        }

        public async Task<(List<Employer> Employers, PaginationViewEntity PageRequest)> GetEmployersAsync(string empId, PaginationEntity pagination, string searchFields)
        {
            try
            {
                using (var db = Connection)
                {
                    var p = new DynamicParameters();
                    p.Add("@Emp_ID", empId);
                    p.Add("@PageIndex", pagination.PageIndex);
                    p.Add("@PageSize", pagination.PageSize);
                    p.Add("@Search", pagination.Search);
                    p.Add("@SortColumn", pagination.SortColumn);
                    p.Add("@SortOrder", pagination.SortOrder);
                    p.Add("@User_ID", null);
                    p.Add("@TypeFilter", pagination.TypeFilter);
                    p.Add("@SearchFields", searchFields ?? "all");

                    using (var multi = await db.QueryMultipleAsync("sp_EmployerList", p, commandType: CommandType.StoredProcedure))
                    {
                        var employers = (await multi.ReadAsync<Employer>()).ToList();
                        var metaRow = await multi.ReadFirstOrDefaultAsync();

                        var metadata = new PaginationViewEntity();
                        if (metaRow != null)
                        {
                            metadata.TotalItems = (int)metaRow.RecordCount;
                            metadata.CurrentPage = pagination.PageIndex;
                            metadata.PageSize = pagination.PageSize;
                            metadata.TotalPages = (int)Math.Ceiling((double)metadata.TotalItems / metadata.PageSize);
                        }

                        return (employers, metadata);
                    }
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(GetEmployersAsync), nameof(EmployerService), $"SQL error retrieving paginated employer list for EmpId {empId}", "Service");
                return (new List<Employer>(), new PaginationViewEntity());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetEmployersAsync), nameof(EmployerService), $"Unexpected error retrieving paginated employer list for EmpId {empId}", "Service");
                return (new List<Employer>(), new PaginationViewEntity());
            }
        }

        public async Task<List<Employer>> SearchEmployersAsync(string searchTerm, int pageSize, int filingYear)
        {
            try
            {
                using (var db = Connection)
                {
                    var p = new DynamicParameters();
                    p.Add("@search", searchTerm);
                    p.Add("@PageSize", pageSize);
                    p.Add("@fillingyear", filingYear);

                    var result = await db.QueryAsync<Employer>(
                        "sp_EmployersOnlyList",
                        p,
                        commandType: CommandType.StoredProcedure);
                    return result.ToList();
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(SearchEmployersAsync), nameof(EmployerService), $"SQL error searching employers with term '{searchTerm}'", "Service");
                return new List<Employer>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(SearchEmployersAsync), nameof(EmployerService), $"Unexpected error searching employers with term '{searchTerm}'", "Service");
                return new List<Employer>();
            }
        }

        public async Task<Employer?> GetEmployerDetailsByIdAsync(string employerId)
        {
            try
            {
                using (var db = Connection)
                {
                    return await db.QuerySingleOrDefaultAsync<Employer>(
                        "sp_EmployerDetails",
                        new { id = employerId },
                        commandType: CommandType.StoredProcedure);
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(GetEmployerDetailsByIdAsync), nameof(EmployerService), $"SQL error fetching details for EmployerId {employerId}", "Service");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetEmployerDetailsByIdAsync), nameof(EmployerService), $"Unexpected error fetching details for EmployerId {employerId}", "Service");
                return null;
            }
        }

        public async Task<Employer?> GetCommonEmployerDetailsAsync(string employer_Id, string filingYear)
        {
            try
            {
                using (var db = Connection)
                {
                    var row = await db.QuerySingleOrDefaultAsync<dynamic>(
                        "sp_GetEmployerCommonDetailsByEmployerid",
                        new { EmployerId = employer_Id, FilingYear = filingYear },
                        commandType: CommandType.StoredProcedure
                    );

                    if (row == null) return null;

                    return new Employer
                    {
                        Id = row.EmployerId?.ToString(),
                        Aca_Id = (int?)row.aca_employerid ?? 0,
                        EIN = row.EIN,
                        Name = row.Name,
                        Type = row.Type,
                        FilingYear = row.FilingYear?.ToString(),
                        Address1 = row.Address1,
                        Address2 = row.Address2,
                        City = row.City,
                        State = row.State,
                        StateId = row.StateId?.ToString(),
                        ZipCode = row.ZipCode?.ToString(),
                        Country = row.Country?.ToString(),
                        IsForeignAddress = row.IsForeignAddress != null && Convert.ToBoolean(row.IsForeignAddress),
                        IsCorrected = row.IsCorrected != null && Convert.ToBoolean(row.IsCorrected),
                        enableCallCenter = row.EnableCallCenter != null && Convert.ToBoolean(row.EnableCallCenter)
                    };
                }
            }
            catch (RuntimeBinderException rbEx)
            {
                _logger.LogError(rbEx, nameof(GetCommonEmployerDetailsAsync), nameof(EmployerService), $"Column mapping mismatch for EmployerId {employer_Id}.", "Service");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetCommonEmployerDetailsAsync), nameof(EmployerService), $"Error retrieving employer details for EmployerId: {employer_Id}", "Service");
                return null;
            }
        }

        // Split-screen filing-year comparison.
        // Each (employer, filing year) is its own Employer row with a distinct identity id;
        // the same business is linked across years only by EIN (taxid). Given the current
        // record's id, resolve the matching row for the requested year by EIN, then return
        // its common details. Returns null when that year has no row for this business.
        public async Task<Employer?> GetEmployerDetailsForCompareAsync(string currentEmployerId, string filingYear)
        {
            try
            {
                int? targetId;
                using (var db = Connection)
                {
                    targetId = await db.QuerySingleOrDefaultAsync<int?>(
                        @"SELECT TOP 1 d.id
                   FROM dbo.Employer c
                   JOIN dbo.Employer d
                       ON d.taxid = c.taxid
                      AND d.filingYear = @Year
                      AND ISNULL(d.IsDeleted, 0) = 0
                   WHERE c.id = @Id
                   ORDER BY CASE
                                WHEN (d.companyId IS NULL AND c.companyId IS NULL)
                                  OR (d.companyId IS NOT NULL AND c.companyId IS NOT NULL)
                                THEN 0 ELSE 1
                            END, d.id",
                        new { Id = currentEmployerId, Year = filingYear });
                }

                if (targetId == null) return null;
                return await GetCommonEmployerDetailsAsync(targetId.Value.ToString(), filingYear);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetEmployerDetailsForCompareAsync), nameof(EmployerService), $"currentId {currentEmployerId}, year {filingYear}", "Service");
                return null;
            }
        }

        public async Task<EmployerInfoModel?> GetEmployerInfoAsync(string employerId)
        {
            try
            {
                using (var db = Connection)
                {
                    var p = new DynamicParameters();
                    p.Add("@Action", "List");
                    p.Add("@employerid", employerId);

                    var row = await db.QuerySingleOrDefaultAsync<dynamic>(
                        "sp_EmployerInfo_Tracker",
                        p,
                        commandType: CommandType.StoredProcedure);

                    if (row == null) return null;

                    return new EmployerInfoModel
                    {
                        EmployerId = row.EmployerId?.ToString(),
                        SalesRepId = row.srepid?.ToString(),
                        Name = row._name,
                        City = row._city,
                        Stadress = row._stadress,
                        Stadress2 = row._stadress2,
                        Zip = row._zip?.ToString(),
                        Phone = row._phone,
                        Email = row._email,
                        State = row._state,
                        Vendor = row._vendor,
                        DataAnalyst = row.dataanalyst?.ToString().Trim(),
                        WaitingPeriod = row.WaitingPeriod,
                        BandingType = row.BandingType,
                        BrokerPhone = row.BrokerPhone,
                        BrokerEmail = row.BrokerEmail,
                        StateId = row.StateId == null ? (int?)null : Convert.ToInt32(row.StateId),
                        FirmId = row.firmid == null ? (int?)null : Convert.ToInt32(row.firmid),
                        BrokerId = row.brokerid == null ? (int?)null : Convert.ToInt32(row.brokerid),
                        AccountManagerId = row.acctmanagerid == null ? (int?)null : Convert.ToInt32(row.acctmanagerid),
                        ContactId = row.ACAContactId == null ? (int?)null : Convert.ToInt32(row.ACAContactId),
                        BillingContactId = row.BillingContactId == null ? (int?)null : Convert.ToInt32(row.BillingContactId),
                        OverProposedFormCount = row._OverProposedFormCount == null ? (int?)null : Convert.ToInt32(row._OverProposedFormCount),
                        OverProposedFormCountPrice = row._OverProposedFormCountPrice == null
                            ? (decimal?)null
                            : Convert.ToDecimal(row._OverProposedFormCountPrice)
                    };
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(GetEmployerInfoAsync), nameof(EmployerService), $"SQL error fetching tracker info for EmployerId {employerId}", "Service");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetEmployerInfoAsync), nameof(EmployerService), $"Unexpected error fetching tracker info for EmployerId {employerId}", "Service");
                return null;
            }
        }

        // FIX: Parameter changed to string? to match interface declaration.
        //      Return type changed to EmployerDropdownDataModel? — catches return null.
        public async Task<EmployerDropdownDataModel?> GetEmployerDropdownDataAsync(string? employerId, int? firmId, int? brokerId, bool IsBilling)
        {
            try
            {
                var model = new EmployerDropdownDataModel();
                model.Contacts = new List<ContactDropdownItem>();
                model.BillingContacts = new List<ContactDropdownItem>();

                using (var db = Connection)
                {
                    var p = new DynamicParameters();
                    p.Add("@Action", "GetDropdownData");
                    p.Add("@employerid", employerId);
                    p.Add("@firmid", firmId);
                    p.Add("@brokerid", brokerId);
                    p.Add("@isBilling", IsBilling);

                    using (var multi = await db.QueryMultipleAsync("sp_GetDropdownDataEmployer_new1_tracker", p, commandType: CommandType.StoredProcedure))
                    {
                        // 1. Industry (Skip)
                        multi.Read();

                        // 2. DataType (Skip)
                        multi.Read();

                        // 3. Account Managers
                        var ams = await multi.ReadAsync<dynamic>();
                        foreach (var item in ams)
                            model.AccountManagers[Convert.ToInt32(item.Id)] = item._name?.ToString() ?? "";

                        // 4. Data Analysts
                        var das = await multi.ReadAsync<dynamic>();
                        foreach (var item in das)
                            model.DataAnalysts[Convert.ToInt32(item.Id)] = item._name?.ToString() ?? "";

                        // 5. Sales Reps
                        var reps = await multi.ReadAsync<dynamic>();
                        foreach (var item in reps)
                            model.SalesReps[Convert.ToInt32(item.srepid)] = item.srepname?.ToString() ?? "";

                        // 6. Firms
                        var firms = await multi.ReadAsync<dynamic>();
                        foreach (var item in firms)
                            model.Firms[Convert.ToInt32(item.firmid)] = item.FirmName?.ToString() ?? "";

                        // 7. Brokers
                        var brokers = await multi.ReadAsync<dynamic>();
                        foreach (var item in brokers)
                            model.Brokers[Convert.ToInt32(item.brokerid)] = item.BrokerName?.ToString() ?? "";

                        // 8. States (Legacy table - Skip)
                        multi.Read();

                        // 9. Affiliates (Self-referencing Employer Table)
                        var affiliates = await multi.ReadAsync<dynamic>();
                        foreach (var item in affiliates)
                        {
                            var isMain = Convert.ToInt32(item.IsMain) == 1;
                            var affId = Convert.ToInt32(item.AffiliateId);
                            var affName = item.AffiliateName?.ToString() ?? "";

                            model.Affiliates[affId] = isMain ? $"[MAIN] {affName}" : affName;

                            model.RawAffiliateData.Add(new AffiliateSummary
                            {
                                AffiliateId = affId,
                                AffiliateName = affName,
                                EIN = item.EIN?.ToString() ?? "",
                                Address = item.Address?.ToString() ?? "",
                                City = item.City?.ToString() ?? "",
                                State = item.State?.ToString() ?? "",
                                Zip = item.Zip?.ToString() ?? "",
                                Phone = item.Phone?.ToString() ?? "",
                                Email = item.Email?.ToString() ?? "",
                                NumberOfEmployees = item.NumberOfEmployees == null ? 0
                         : Convert.ToInt32(item.NumberOfEmployees),
                                IsMain = isMain
                            });
                        }

                        // 10. General Contacts
                        var contacts = await multi.ReadAsync<dynamic>();
                        model.Contacts = contacts.Select(c => new ContactDropdownItem
                        {
                            Key = c.tbl_Medtracker_Contactid?.ToString(),
                            Value = c.name?.ToString() ?? "",
                            Phone = c.phone?.ToString() ?? "",
                            Email = c.email?.ToString() ?? ""
                        }).ToList();

                        // 11. Billing Contacts
                        var billing = await multi.ReadAsync<dynamic>();
                        model.BillingContacts = billing.Select(b => new ContactDropdownItem
                        {
                            Key = b.tbl_Medtracker_Contactid?.ToString(),
                            Value = b.name?.ToString() ?? "",
                            Phone = b.phone?.ToString() ?? "",
                            Email = b.email?.ToString() ?? ""
                        }).ToList();

                        // 12. Broker Contacts (Skip or map to a new property if needed)
                        await multi.ReadAsync<dynamic>();

                        // 13. State_ACA
                        var statesAca = await multi.ReadAsync<dynamic>();
                        foreach (var item in statesAca)
                            model.State[Convert.ToInt32(item.id)] = item.code?.ToString() ?? "";

                        // 14. Country_ACA
                        var countryAca = await multi.ReadAsync<dynamic>();
                        foreach (var item in countryAca)
                            model.Country[Convert.ToInt32(item.id)] = item.code?.ToString() ?? "";

                        // 15. Broker Entity Details
                        var brokerDetails = await multi.ReadSingleOrDefaultAsync<dynamic>();
                        if (brokerDetails != null)
                        {
                            model.BrokerEntityDetails = new ContactDropdownItem
                            {
                                Phone = brokerDetails.Phone?.ToString(),
                                Email = brokerDetails.Email?.ToString()
                            };
                        }
                    }
                }

                return model;
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(GetEmployerDropdownDataAsync), nameof(EmployerService), $"SQL error loading dropdown data for EmployerId {employerId}", "Service");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetEmployerDropdownDataAsync), nameof(EmployerService), $"Unexpected error loading dropdown data for EmployerId {employerId}", "Service");
                return null;
            }
        }

        public async Task<Employer?> GetEmployerByIdAsync(string id)
        {
            try
            {
                using (var db = Connection)
                {
                    return await db.QuerySingleOrDefaultAsync<Employer>(
                        "sp_EmployerDetails",
                        new { id = id },
                        commandType: CommandType.StoredProcedure);
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(GetEmployerByIdAsync), nameof(EmployerService), $"SQL error fetching employer by Id {id}", "Service");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetEmployerByIdAsync), nameof(EmployerService), $"Unexpected error fetching employer by Id {id}", "Service");
                return null;
            }
        }

        public async Task<(List<Employer> employerListEntity, PaginationMetadata pagination)>GetEmployersListViewAsync(PaginationEntity pagination, string userId = "", string roleName = "")
        {
            try
            {
                var employers = new List<Employer>();
                var paginationMetadata = new PaginationMetadata();

                using (var db = Connection)
                {
                    var p = new DynamicParameters();
                    p.Add("@PageIndex", pagination.PageIndex);
                    p.Add("@PageSize", pagination.PageSize);
                    p.Add("@Search", pagination.Search ?? "");
                    p.Add("@SortColumn", pagination.SortColumn);
                    p.Add("@SortOrder", pagination.SortOrder ?? "asc");
                    p.Add("@FillingYear", pagination.fillingYear);
                    p.Add("@TypeFilter", pagination.TypeFilter);
                    // SP uses these to restrict results for AM, AMSupervisor, DA, DASupervisor roles
                    p.Add("@UserId", string.IsNullOrEmpty(userId) ? (object)DBNull.Value : int.Parse(userId));
                    p.Add("@RoleName", roleName ?? "");

                    using (var multi = await db.QueryMultipleAsync("sp_EmployersOnlyList", p, commandType: CommandType.StoredProcedure))
                    {
                        employers = (await multi.ReadAsync<Employer>()).ToList();

                        var metaRow = await multi.ReadFirstOrDefaultAsync();
                        if (metaRow != null)
                        {
                            paginationMetadata.TotalCount = (int)metaRow.RowCount;
                            paginationMetadata.PageNumber = pagination.PageIndex;
                            paginationMetadata.PageSize = pagination.PageSize;
                        }
                    }
                }

                return (employers, paginationMetadata);
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(GetEmployersListViewAsync), nameof(EmployerService), "SQL error retrieving employer list view", "Service");
                return (new List<Employer>(), new PaginationMetadata());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetEmployersListViewAsync), nameof(EmployerService), "Unexpected error retrieving employer list view", "Service");
                return (new List<Employer>(), new PaginationMetadata());
            }
        }
        #endregion

        #region 3. SERVICE INFO & 1094 METHODS
        
        // ===================================================================
        // 3. SERVICE INFO & 1094 METHODS
        // ===================================================================

        public async Task<List<ServiceListItem>> GetServiceList(string employerId, string planYear)
        {
            try
            {
                using (var db = Connection)
                {
                    var p = new DynamicParameters();
                    p.Add("@Action", "List");
                    p.Add("@EmployerId", employerId);
                    p.Add("@PlanYear", planYear);

                    var result = await db.QueryAsync<dynamic>(
                        "sp_ServiceMainDisplayList_tracker",
                        p,
                        commandType: CommandType.StoredProcedure);

                    return result.Select(r => new ServiceListItem
                    {
                        ServiceId = r.serviceid.ToString(),
                        ServiceName = r.servicename,
                        Status = r._status
                    }).ToList();
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(GetServiceList), nameof(EmployerService), $"SQL error fetching service list for EmployerId {employerId}, PlanYear {planYear}", "Service");
                return new List<ServiceListItem>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetServiceList), nameof(EmployerService), $"Unexpected error fetching service list for EmployerId {employerId}, PlanYear {planYear}", "Service");
                return new List<ServiceListItem>();
            }
        }

        public async Task<ServiceDetail> GetServiceDetail(string employerId, string planYear, string serviceId)
        {
            try
            {
                using (var db = Connection)
                {
                    var p = new DynamicParameters();
                    p.Add("@Action", "Detail");
                    p.Add("@EmployerId", employerId);
                    p.Add("@PlanYear", planYear);
                    p.Add("@ServiceId", serviceId);

                    var r = await db.QuerySingleOrDefaultAsync<dynamic>(
                        "sp_ServiceMainDisplayList_tracker",
                        p,
                        commandType: CommandType.StoredProcedure);

                    if (r == null) return new ServiceDetail();

                    // FIX: Return type changed to string? so null returns are valid
                    static string? FormatDate(object? dateObj)
                    {
                        if (dateObj == null || dateObj == DBNull.Value) return null;
                        if (DateTime.TryParse(dateObj.ToString(), out DateTime dt))
                            return dt.ToString("yyyy-MM-dd");
                        return null;
                    }

                    return new ServiceDetail
                    {
                        ServiceId = r.serviceid?.ToString(),
                        ServiceName = r.servicename?.ToString(),
                        Status = r._status?.ToString(),
                        ServiceListId = r.servicelistid?.ToString(),
                        ProposalSent = FormatDate(r._proposalsent),
                        ProposalSigned = FormatDate(r._proposalreturned),
                        ContractSent = FormatDate(r._contractsent),
                        ContractSigned = FormatDate(r._contractsigned),
                        ImplementationProcess = await ResolveWebNameFromProcessNameAsync(r._implementationprocess?.ToString(), 1),
                        LastUpdated = FormatDate(r._implementationproclastupdated),
                        FollowUpDate = FormatDate(r.FollowUpDate),
                        ProcessStep1094 = r._1094processstep?.ToString(),
                        FTETrackingStep_lastupdated = FormatDate(r.FTETrackingStep_lastupdated),
                        FTETrackingStep_nextfollowup = FormatDate(r.FTETrackingStep_nextfollowup),
                        StateFilingProcessStep = r._stateFilingprocessstep?.ToString(),
                        StateFilingProcessStepUpdated = FormatDate(r.StateFilingProcessStepUpdated),
                        StateFiling_FollowUpDate = FormatDate(r.StateFiling_FollowUpDate),
                        ExtensionFiled = FormatDate(r._entensionFiled),
                        AuditBy = r._auditBy?.ToString(),
                        ReceiptId = r.ReceiptID?.ToString()
                    };
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(GetServiceDetail), nameof(EmployerService), $"SQL error fetching service detail for EmployerId {employerId}, ServiceId {serviceId}", "Service");
                return new ServiceDetail();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetServiceDetail), nameof(EmployerService), $"Unexpected error fetching service detail for EmployerId {employerId}, ServiceId {serviceId}", "Service");
                return new ServiceDetail();
            }
        }

        private async Task<string?> ResolveWebNameFromProcessNameAsync(string? processName, int categoryId)
        {
            if (string.IsNullOrWhiteSpace(processName)) return null;

            using (var db = Connection)
            {
                return await db.QueryFirstOrDefaultAsync<string?>(
                    @"SELECT TOP 1 WebName 
              FROM Tracker_Process 
              WHERE ProcessName = @Name 
                AND CategoryId = @CategoryId
                AND IsDeleted <> 1
                AND IsDoNotDisplay <> 1",

                    new { Name = processName.Trim(), CategoryId = categoryId });
            }
        }

        public async Task<List<Process_steps>> GetProcessesAsync(string category)
        {
            try
            {
                using (var db = Connection)
                {
                    var result = await db.QueryAsync<dynamic>(
                        "sp_GetProcessesByCategory",
                        new { Category = category },
                        commandType: CommandType.StoredProcedure);

                    return result.Select(r => new Process_steps
                    {
                        ProcessId = r.ProcessId.ToString(),
                        ProcessName = r.ProcessName,
                        FollowUpDays = (int)r.FollowUpDays,
                        Category = r.Category,
                        DisplayOrder = (int)r.DisplayOrder
                    }).ToList();
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(GetProcessesAsync), nameof(EmployerService), $"SQL error fetching processes for category '{category}'", "Service");
                return new List<Process_steps>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetProcessesAsync), nameof(EmployerService), $"Unexpected error fetching processes for category '{category}'", "Service");
                return new List<Process_steps>();
            }
        }

        public async Task<List<Process_steps>> GetWebProcessesAsync(string category)
        {
            try
            {
                using (var db = Connection)
                {
                    var result = await db.QueryAsync<dynamic>(
                        "sp_GetWebProcessesByCategory",
                        new { Category = category },
                        commandType: CommandType.StoredProcedure);

                    return result.Select(r => new Process_steps
                    {
                        ProcessId = r.ProcessId.ToString(),
                        ProcessName = r.ProcessName,
                        FollowUpDays = int.TryParse(r.FollowUpDays?.ToString(), out int fd) ? fd : 0,
                        Category = r.CategoryName,
                        DisplayOrder = (int)r.DisplayOrder
                    }).ToList();
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(GetWebProcessesAsync), nameof(EmployerService), $"SQL error fetching web processes for category '{category}'", "Service");
                return new List<Process_steps>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetWebProcessesAsync), nameof(EmployerService), $"Unexpected error fetching web processes for category '{category}'", "Service");
                return new List<Process_steps>();
            }
        }

        public async Task<List<AuditUser>> GetAuditByDropdownAsync()
        {
            try
            {
                using (var db = Connection)
                {
                    var result = await db.QueryAsync<dynamic>(
                        "sp_ServiceMainDisplayList",
                        new { Action = "GetAuditByDropdown" },
                        commandType: CommandType.StoredProcedure);

                    return result.Select(r => new AuditUser
                    {
                        Id = r.AuditUserId.ToString(),
                        Name = r.AuditBy
                    }).ToList();
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(GetAuditByDropdownAsync), nameof(EmployerService), "SQL error fetching audit-by dropdown", "Service");
                return new List<AuditUser>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetAuditByDropdownAsync), nameof(EmployerService), "Unexpected error fetching audit-by dropdown", "Service");
                return new List<AuditUser>();
            }
        }

        // FIX: Return type changed to Employer? — method returns null on not-found/error.
        public async Task<Employer?> GetEmployer1094DetailsAsync(string employerId)
        {
            try
            {
                using (var db = Connection)
                {
                    var r = await db.QuerySingleOrDefaultAsync<dynamic>(
                        "sp_EmployerDetails",
                        new { id = employerId },
                        commandType: CommandType.StoredProcedure);

                    if (r == null) return null;

                    var employer = new Employer
                    {
                        Id = employerId,
                        FilingYear = r.filingYear?.ToString(),
                        EIN = r.taxid,
                        Name = r.name,
                        FormType = r.FormType,
                        OriginCode = r.OriginCode,
                        CertA = r.qualifyingOfferMethod == 1,
                        CertB = r.isAggregatedAle == 1,
                        CertC = r.isAuthoritative == 1,
                        CertD = r.offer98Method == 1,
                        DisableAutoCounts = r.disableChanges != null && r.disableChanges.ToString() == "1"
                    };

                    var rowDict = (IDictionary<string, object>)r;
                    for (int i = 0; i < 13; i++)
                    {
                        string? min = rowDict[$"minimum_{i}"]?.ToString();
                        employer.MinimumCoverage[i] = int.TryParse(min, out var minVal) ? minVal : 0;

                        string? agg = rowDict[$"group_{i}"]?.ToString();
                        employer.AggregateGroup[i] = int.TryParse(agg, out var aggVal) ? aggVal : 0;

                        string? ft = rowDict[$"fullTime_{i}"]?.ToString();
                        employer.FullTime[i] = int.TryParse(ft, out var v) ? v : 0;

                        string? tot = rowDict[$"total_{i}"]?.ToString();
                        employer.Total[i] = int.TryParse(tot, out var v2) ? v2 : 0;

                        string? sec = rowDict[$"S4980H_{i}"]?.ToString();
                        employer.Sec4980H[i] = int.TryParse(sec, out var v3) ? v3 : 0;
                    }

                    return employer;
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(GetEmployer1094DetailsAsync), nameof(EmployerService), $"SQL error fetching 1094 details for EmployerId {employerId}", "Service");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetEmployer1094DetailsAsync), nameof(EmployerService), $"Unexpected error fetching 1094 details for EmployerId {employerId}", "Service");
                return null;
            }
        }

        public async Task<List<AuditLog>> GetEmployerAuditLogAsync(int employerId)
        {
            try
            {
                using (var db = Connection)
                {
                    using (var multi = await db.QueryMultipleAsync(
                        "sp_EmployerDetails",
                        new { id = employerId },
                        commandType: CommandType.StoredProcedure))
                    {
                        // Skip first two result sets
                        // FIX: Removed bare catch{} swallowing unknown exceptions.
                        //      Read() on a missing result set throws InvalidOperationException,
                        //      not a general Exception, so we let it propagate naturally here.
                        //      If the SP always returns 3+ result sets this block is safe as-is.
                        multi.Read();
                        multi.Read();

                        var logs = multi.Read<dynamic>();
                        return logs.Select(r => new AuditLog
                        {
                            Operation = r.operation,
                            OldValue = r.oldValue,
                            NewValue = r.newValue,
                            Date = r.date,
                            UserId = r.userId,
                            UserName = r.username
                        }).ToList();
                    }
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(GetEmployerAuditLogAsync), nameof(EmployerService), $"SQL error fetching audit log for EmployerId {employerId}", "Service");
                return new List<AuditLog>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetEmployerAuditLogAsync), nameof(EmployerService), $"Unexpected error fetching audit log for EmployerId {employerId}", "Service");
                return new List<AuditLog>();
            }
        }

        public async Task<EmployerImportantInfo> GetEmployerImportantInfoAsync(string employerId)
        {
            try
            {
                using (var db = Connection)
                {
                    var result = await db.QueryFirstOrDefaultAsync<EmployerImportantInfo>(
                        "sp_GetEmployer_OtherInfo",
                        new { EmployerId = employerId },
                        commandType: CommandType.StoredProcedure);

                    if (result == null)
                        return new EmployerImportantInfo { EmployerId = employerId };

                    result.EmployerId = employerId;
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetEmployerImportantInfoAsync), nameof(EmployerService), $"Error fetching important info for EmployerId {employerId}", "Service");
                return new EmployerImportantInfo { EmployerId = employerId };
            }
        }
        #endregion

        #region 4.GET & SEARCH METHODS & private
        public async Task<int?> GetPrimaryEmployerIdAsync(string employerId)
        {
            try
            {
                using (var db = Connection)
                {
                    return await db.ExecuteScalarAsync<int?>(
                        "SELECT companyId FROM Employer WHERE id = @id AND IsDeleted = 0",
                        new { id = employerId });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetPrimaryEmployerIdAsync), nameof(EmployerService), $"EmployerId:{employerId}");
                return null;
            }
        }

        public async Task<List<Employer>> GetEmployerFamilyAsync(string employerId)
        {
            try
            {
                using (var db = Connection)
                {
                    var result = await db.QueryAsync<Employer>(
                        "sp_GetEmployerFamily",
                        new { EmployerId = employerId },
                        commandType: CommandType.StoredProcedure);
                    return result.ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetEmployerFamilyAsync), nameof(EmployerService), $"EmployerId:{employerId}");
                return new List<Employer>();
            }
        }

        public async Task<AffiliateDto?> GetAffiliateByIdAsync(int id)
        {
            try
            {
                using var db = Connection;
                var p = new DynamicParameters();
                p.Add("@Action", "LoadAffiliateData");
                p.Add("@AffiliateId", id);

                return await db.QueryFirstOrDefaultAsync<AffiliateDto>("sp_Affiliate", p,
                               commandType: CommandType.StoredProcedure);
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(GetAffiliateByIdAsync), nameof(EmployerService),
                                 $"SQL error. Id:{id}", "Service");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetAffiliateByIdAsync), nameof(EmployerService),
                                 $"Unexpected error. Id:{id}", "Service");
                return null;
            }
        }

        public async Task<AffiliateDto?> GetPrimaryEmployerDataAsync(int employerId)
        {
            try
            {
                using var db = Connection;
                var p = new DynamicParameters();
                p.Add("@Action", "LoadPrimaryData");
                p.Add("@EmployerId", employerId);

                return await db.QueryFirstOrDefaultAsync<AffiliateDto>(
                    "sp_Affiliate", p,
                    commandType: CommandType.StoredProcedure);
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(GetPrimaryEmployerDataAsync), nameof(EmployerService),
                                 $"SQL error. EmployerId:{employerId}", "Service");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetPrimaryEmployerDataAsync), nameof(EmployerService),
                                 $"Unexpected error. EmployerId:{employerId}", "Service");
                return null;
            }
        }

        // collapse only when the ENTIRE row is identical across all 12
        // months (indices 1..12), and not all-zero. On collapse, write the value to index 0 ("All")
        // and blank the months. If months are mixed, blank "All" and keep the months. If no months
        // are used (a direct "All" entry), leave everything as entered.
        private static void CollapseAllTwelveMonths(Employer e)
        {
            if (e == null) return;
            var min = e.MinimumCoverage ?? (e.MinimumCoverage = new int[13]);
            var ft = e.FullTime ?? (e.FullTime = new int[13]);
            var tot = e.Total ?? (e.Total = new int[13]);
            var agg = e.AggregateGroup ?? (e.AggregateGroup = new int[13]);
            var sec = e.Sec4980H ?? (e.Sec4980H = new int[13]);
            if (new[] { min, ft, tot, agg, sec }.Any(a => a.Length < 13)) return;

            // Each 1094-C Part III column has its OWN "All 12 Months" line, independent of
            // the other columns (K-System / IRS form). So roll up per-metric, not whole-row:
            // e.g. Min Coverage can collapse to "All" even when Full-Time varies month to month.
            CollapseMetric(min);
            CollapseMetric(ft);
            CollapseMetric(tot);
            CollapseMetric(agg);
            CollapseMetric(sec);
        }

        // Collapse ONE metric's 13-slot array (index 0 = "All 12 Months", 1..12 = months):
        //  - months all zero  -> leave as entered (direct "All" entry or empty)
        //  - months uniform    -> put the value on "All", blank Jan..Dec
        //  - months vary        -> keep the months, clear any stale "All"
        private static void CollapseMetric(int[] a)
        {
            if (a == null || a.Length < 13) return;
            bool used = false;
            for (int i = 1; i <= 12; i++) if (a[i] != 0) { used = true; break; }
            if (!used) return;                       // direct "All" entry (a[0]) or empty - leave as is

            bool uniform = true;
            for (int i = 2; i <= 12 && uniform; i++) if (a[i] != a[1]) uniform = false;

            if (uniform) { a[0] = a[1]; for (int i = 1; i <= 12; i++) a[i] = 0; }
            else { a[0] = 0; }
        }

        private async Task<string?> ResolveProcessNameFromWebNameAsync( IDbConnection db, string? webName)
        {
            if (string.IsNullOrWhiteSpace(webName)) return null;

            var direct = await db.QueryFirstOrDefaultAsync<string?>(
                "SELECT TOP 1 ProcessName FROM Tracker_Process WHERE ProcessName = @Name AND IsDeleted <> 1",
                new { Name = webName.Trim() });

            if (direct != null) return direct;

            var resolved = await db.QueryFirstOrDefaultAsync<string?>(
                @"SELECT TOP 1 ProcessName 
          FROM Tracker_Process 
          WHERE WebName = @WebName 
            AND IsDeleted <> 1 
            AND IsDoNotDisplay <> 1
          ORDER BY DisplayOrder ASC",
                new { WebName = webName.Trim() });

            return resolved;
        }

        // FIX: Parameter changed to string? — callers pass nullable date strings from
        //      ServiceDetail properties. Accepting string? silences all 10 null-arg warnings.
        private DateTime? ToDbDate(string? dateStr)
        {
            if (string.IsNullOrWhiteSpace(dateStr))
                return null;

            if (DateTime.TryParse(dateStr, out DateTime parsedDate))
                return parsedDate;

            return null;
        }

        public async Task<int> GetFollowUpDaysAsync(string processName, int categoryId)
        {
            if (string.IsNullOrWhiteSpace(processName))
                return 0;
            string cleanName = processName.Trim();
            try
            {
                using (var db = Connection)
                {
                    int? days = await db.QueryFirstOrDefaultAsync<int?>(
                        "sp_GetFollowUpDaysByProcessName",
                        new { Name = cleanName, CategoryId = categoryId },
                        commandType: CommandType.StoredProcedure);
                    return days ?? 0;
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(GetFollowUpDaysAsync), nameof(EmployerService), $"SQL error fetching follow-up days for process '{cleanName}' category '{categoryId}'", "Service");
                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetFollowUpDaysAsync), nameof(EmployerService), $"Unexpected error fetching follow-up days for process '{cleanName}' category '{categoryId}'", "Service");
                return 0;
            }
        }

        public async Task<IEnumerable<ProcessHistoryDto>> GetProcessHistoryAsync(int employerServiceId, string categoryName)
        {
            try
            {
                using (var db = Connection)
                {
                    var history = await db.QueryAsync<ProcessHistoryDto>(
                        "sp_GetProcessHistory",
                        new
                        {
                            EmployerServiceId = employerServiceId,
                            CategoryName = categoryName
                        },
                        commandType: CommandType.StoredProcedure);

                    return history;
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(GetProcessHistoryAsync), nameof(EmployerService), $"SQL error fetching process history for EmployerServiceId {employerServiceId}, Category '{categoryName}'", "Service");
                return Enumerable.Empty<ProcessHistoryDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetProcessHistoryAsync), nameof(EmployerService), $"Unexpected error fetching process history for EmployerServiceId {employerServiceId}, Category '{categoryName}'", "Service");
                return Enumerable.Empty<ProcessHistoryDto>();
            }
        }

        #endregion

        #region 5. ADD/UPDATE/DELETE METHODS
        // ===================================================================
        // 5. ADD/UPDATE/DELETE METHODS
        // ===================================================================

        public async Task<(int EmployerId, int TrackerEmployerId)> AddEmployerCommonAsync(Employer employer, string? companyIdFromSession)
        {
            try
            {
                int newEmployerId = 0;
                int newTrackerId = 0;
                using (var db = Connection)
                {
                    db.Open();
                    using (var transaction = db.BeginTransaction())
                    {
                        var p = new DynamicParameters();
                        p.Add("@EIN", employer.TaxId);
                        p.Add("@Type", employer.Type);
                        p.Add("@Name", employer.Name);
                        p.Add("@FilingYear", employer.FilingYear);
                        p.Add("@Address1", employer.Address1);
                        p.Add("@Address2", employer.Address2);
                        p.Add("@City", employer.City);
                        p.Add("@State", employer.State);
                        p.Add("@StateId", employer.StateId);
                        p.Add("@Zip", employer.ZipCode);
                        p.Add("@Country", employer.Country);
                        p.Add("@IsForeignAddress", employer.IsForeignAddress);
                        p.Add("@IsCorrected", employer.IsCorrected);
                        p.Add("@companyid", companyIdFromSession);
                        p.Add("@EmployerId", dbType: DbType.Int32, direction: ParameterDirection.Output);
                        p.Add("@TrackerEmployerId", dbType: DbType.Int32, direction: ParameterDirection.Output);

                        await db.ExecuteAsync(
                            "sp_AddEmployerCommon",
                            p,
                            transaction: transaction,
                            commandType: CommandType.StoredProcedure);

                        transaction.Commit();
                        newEmployerId = p.Get<int>("@EmployerId");
                        newTrackerId = p.Get<int>("@TrackerEmployerId");
                    }
                }
                // AUDIT FIX: Log the Insert
                if (newEmployerId > 0)
                {
                    await _auditLogger.CaptureAndLogChangeAsync("Employer", newEmployerId.ToString(), "INSERT", null, employer);
                }
                return (newEmployerId, newTrackerId);
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(AddEmployerCommonAsync), nameof(EmployerService), $"SQL error adding employer '{employer.Name}'", "Service");
                return (0, 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(AddEmployerCommonAsync), nameof(EmployerService), $"Unexpected error adding employer '{employer.Name}'", "Service");
                return (0, 0);
            }
        }

        public async Task<int> AddEmployerInfo_TrackerAsync(EmployerInfoModel model,int currentUserId = 0)
        {
            try
            {
                // AUDIT FIX: Attempt to fetch old info if doing an upsert logic
                var oldRecord = await GetEmployerInfoAsync(model.EmployerId);
                string actionType = oldRecord == null ? "INSERT" : "UPDATE";
                using (var db = Connection)
                {
                    db.Open();
                    using (var transaction = db.BeginTransaction())
                    {
                        var p = new DynamicParameters();
                        p.Add("@EmployerId", model.EmployerId);
                        p.Add("@FirmId", model.FirmId);
                        p.Add("@BrokerId", model.BrokerId);
                        p.Add("@AffiliateId", model.AffiliateId);
                        p.Add("@ACAContactId", model.ContactId);
                        p.Add("@ContactPhone", model.ContactPhone);
                        p.Add("@ContactEmail", model.ContactEmail);
                        p.Add("@BillingContactId", model.BillingContactId);
                        p.Add("@BillingPhone", model.BillingPhone);
                        p.Add("@BillingEmail", model.BillingEmail);
                        p.Add("@AccountManagerId", model.AccountManagerId);
                        p.Add("@SalesRepId", model.SalesRepId);
                        p.Add("@DataAnalyst", model.DataAnalyst);
                        p.Add("@Vendor", model.Vendor);
                        p.Add("@WaitingPeriod", model.WaitingPeriod);
                        p.Add("@BandingType", model.BandingType);
                        p.Add("@OverProposedFormCount", model.OverProposedFormCount);
                        p.Add("@OverProposedFormCountPrice", model.OverProposedFormCountPrice);

                        int newId = await db.ExecuteScalarAsync<int>(
                            "sp_AddEmployerInfoTracker_new",
                            p,
                            transaction: transaction,
                            commandType: CommandType.StoredProcedure);
                        if (newId > 0)
                        {
                            // AM / DA are read-only on the Employer Info tab (assigned
                            // elsewhere), so serialize() does NOT post them and they arrive
                            // NULL. Only overwrite when a value was actually posted — COALESCE
                            // keeps the existing AM / DA otherwise, instead of wiping them on
                            // every save (the previous verbatim UPDATE nulled them out).
                            bool amDaPosted = model.AccountManagerId.HasValue
                                              || !string.IsNullOrWhiteSpace(model.DataAnalyst);
                            if (amDaPosted)
                            {
                                await db.ExecuteAsync(
                                    @"UPDATE Tracker_EmployerExtension
SET AcctManagerid = COALESCE(@AM, AcctManagerid),
    _dataAnalyst  = COALESCE(NULLIF(@DA, ''), _dataAnalyst)
WHERE EmployerId = @Id",
                                    new { AM = model.AccountManagerId, DA = model.DataAnalyst, Id = newId },
                                    transaction: transaction);

                                // Keep the assignment tables in sync with the AM / DA saved above.
                                await db.ExecuteAsync(
                                    "sp_Sync_Tracker_To_Assignments",
                                    new { EmployerId = newId, CurrentUserId = currentUserId },
                                    transaction: transaction,
                                    commandType: CommandType.StoredProcedure);
                            }
                        }
                        transaction.Commit();
                        // AUDIT FIX: Log the change
                        await _auditLogger.CaptureAndLogChangeAsync("Tracker_EmployerExtension", model.EmployerId, actionType, oldRecord, model);
                        return newId;
                    }
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(AddEmployerInfo_TrackerAsync), nameof(EmployerService), $"SQL Error adding tracker info for EmployerId {model.EmployerId}", "Service");
                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(AddEmployerInfo_TrackerAsync), nameof(EmployerService), $"Unexpected Error adding tracker info for EmployerId {model.EmployerId}", "Service");
                return 0;
            }
        }        

        public async Task<int> AddAffiliateAsync(AffiliateDto affiliate)
        {
            try
            {
                using var db = Connection;
                var p = new DynamicParameters();
                p.Add("@Action", "AddAffiliate");
                p.Add("@ParentEmployerId", affiliate.ParentEmployerId);
                p.Add("@FilingYear", affiliate.FilingYear);
                p.Add("@AffiliateName", affiliate.AffiliateName);
                p.Add("@EIN", affiliate.EIN);
                p.Add("@Address", affiliate.Address);
                p.Add("@City", affiliate.City);
                p.Add("@State", affiliate.State);
                p.Add("@Zip", affiliate.Zip);
                p.Add("@Phone", affiliate.Phone);
                p.Add("@Email", affiliate.Email);
                p.Add("@NumberOfEmployees", affiliate.NumberOfEmployees);

                int newId = await db.ExecuteScalarAsync<int>("sp_Affiliate", p,
                               commandType: CommandType.StoredProcedure);
                // AUDIT FIX: Log the Insert
                if (newId > 0)
                {
                    await _auditLogger.CaptureAndLogChangeAsync("Affiliate", newId.ToString(), "INSERT", null, affiliate);
                }
                return newId;
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(AddAffiliateAsync), nameof(EmployerService),
                                 $"SQL error. ParentId:{affiliate.ParentEmployerId}", "Service");
                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(AddAffiliateAsync), nameof(EmployerService),
                                 $"Unexpected error. ParentId:{affiliate.ParentEmployerId}", "Service");
                return 0;
            }
        }
        
        public async Task SaveAffiliateAsync(AffiliateDto affiliate)
        {
            try
            {
                var oldRecord = await GetAffiliateByIdAsync(affiliate.AffiliateId);
                using var db = Connection;
                var p = new DynamicParameters();
                p.Add("@Action", "UpdateAffiliate");
                p.Add("@AffiliateId", affiliate.AffiliateId);
                p.Add("@FilingYear", affiliate.FilingYear);
                p.Add("@AffiliateName", affiliate.AffiliateName);
                p.Add("@EIN", affiliate.EIN);
                p.Add("@Address", affiliate.Address);
                p.Add("@City", affiliate.City);
                p.Add("@State", affiliate.State);
                p.Add("@Zip", affiliate.Zip);
                p.Add("@Phone", affiliate.Phone);
                p.Add("@Email", affiliate.Email);
                p.Add("@NumberOfEmployees", affiliate.NumberOfEmployees);

                await db.ExecuteAsync("sp_Affiliate", p,
                        commandType: CommandType.StoredProcedure);
                // AUDIT FIX: Log the Update
                await _auditLogger.CaptureAndLogChangeAsync("Affiliate", affiliate.AffiliateId.ToString(), "UPDATE", oldRecord, affiliate);
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(SaveAffiliateAsync), nameof(EmployerService),
                                 $"SQL error. AffiliateId:{affiliate.AffiliateId}", "Service");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(SaveAffiliateAsync), nameof(EmployerService),
                                 $"Unexpected error. AffiliateId:{affiliate.AffiliateId}", "Service");
            }
        }              

        public async Task<int> AddContactAsync(ContactDto contact)
        {
            try
            {
                int newId = 0;
                using (var db = Connection)
                {
                    db.Open();
                    using (var transaction = db.BeginTransaction())
                    {
                        var p = new DynamicParameters();
                        p.Add("@Action", "InsertUpdateContact");
                        p.Add("@EmployerId", contact.EmployerId);
                        p.Add("@ContactId", 0);
                        p.Add("@IsBilling", contact.IsBilling);
                        p.Add("@Name", contact.Name);
                        p.Add("@Phone", contact.Phone);
                        p.Add("@Email", contact.Email);

                        var result = await db.ExecuteScalarAsync<object>(
                            "sp_Contact",
                            p,
                            transaction: transaction,
                            commandType: CommandType.StoredProcedure);

                        transaction.Commit();
                        newId = result != null && int.TryParse(result.ToString(), out int parsedId) ? parsedId : 0;
                    }
                }
                // AUDIT FIX: Log the Insert
                if (newId > 0)
                {
                    await _auditLogger.CaptureAndLogChangeAsync("Contact", newId.ToString(), "INSERT", null, contact);
                }

                return newId;
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(AddContactAsync), nameof(EmployerService), $"SQL error adding contact for EmployerId {contact.EmployerId}", "Service");
                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(AddContactAsync), nameof(EmployerService), $"Unexpected error adding contact for EmployerId {contact.EmployerId}", "Service");
                return 0;
            }
        }

        public async Task<int> AddEmployerServiceAsync(ServiceDetail service)
        {
            try
            {
                int newId = 0;
                using (var db = Connection)
                {
                    db.Open();
                    using (var transaction = db.BeginTransaction())
                    {
                        var p = new DynamicParameters();
                        p.Add("@EmployerId", service.EmployerId);
                        p.Add("@ServiceListId", service.ServiceListId);
                        p.Add("@PlanYear", service.PlanYear);
                        p.Add("@Status", string.IsNullOrWhiteSpace(service.Status) ? null : service.Status);
                        p.Add("@ProposalSent", string.IsNullOrWhiteSpace(service.ProposalSent) ? null : service.ProposalSent);
                        p.Add("@ProposalReturned", string.IsNullOrWhiteSpace(service.ProposalSigned) ? null : service.ProposalSigned);
                        p.Add("@ContractSent", string.IsNullOrWhiteSpace(service.ContractSent) ? null : service.ContractSent);
                        p.Add("@ContractSigned", string.IsNullOrWhiteSpace(service.ContractSigned) ? null : service.ContractSigned);
                        p.Add("@ImplementationProcess", string.IsNullOrWhiteSpace(service.ImplementationProcess) ? null : service.ImplementationProcess);
                        p.Add("@ProcessStep1094", string.IsNullOrWhiteSpace(service.ProcessStep1094) ? null : service.ProcessStep1094);
                        p.Add("@StateFilingProcessStep", string.IsNullOrWhiteSpace(service.StateFilingProcessStep) ? null : service.StateFilingProcessStep);
                        p.Add("@AuditBy", string.IsNullOrWhiteSpace(service.AuditBy) ? null : service.AuditBy);
                        p.Add("@ExtensionFiled", string.IsNullOrWhiteSpace(service.ExtensionFiled) ? null : service.ExtensionFiled);
                        p.Add("@ReceiptId", string.IsNullOrWhiteSpace(service.ReceiptId) ? null : service.ReceiptId);

                           newId = await db.ExecuteScalarAsync<int>(
                            "sp_AddEmployerService",
                            p,
                            transaction: transaction,
                            commandType: CommandType.StoredProcedure);

                        transaction.Commit();
                        // AUDIT FIX: Log the Insert
                        if (newId > 0)
                        {
                            await _auditLogger.CaptureAndLogChangeAsync("EmployerService", newId.ToString(), "INSERT", null, service);
                        }

                        return newId;
                    }
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(AddEmployerServiceAsync), nameof(EmployerService), $"SQL error adding employer service for EmployerId {service.EmployerId}", "Service");
                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(AddEmployerServiceAsync), nameof(EmployerService), $"Unexpected error adding employer service for EmployerId {service.EmployerId}", "Service");
                return 0;
            }
        }

        public async Task<int> SaveEmployer1094DetailsAsync(Employer employer)
        {
            CollapseAllTwelveMonths(employer);
            try
            {
                // AUDIT FIX: Capture old state
                var oldRecord = await GetEmployer1094DetailsAsync(employer.Id);
                int result = 0;
                using (var db = Connection)
                {
                    db.Open();
                    using (var transaction = db.BeginTransaction())
                    {
                        var p = new DynamicParameters();
                        p.Add("@Id", employer.Id);
                        p.Add("@FormType", employer.FormType);
                        p.Add("@OriginCode", employer.OriginCode);
                        p.Add("@CertA", employer.CertA);
                        p.Add("@CertB", employer.CertB);
                        p.Add("@CertC", employer.CertC);
                        p.Add("@CertD", employer.CertD);
                        p.Add("@disableChanges", employer.DisableAutoCounts ? 1 : 0);
                        for (int i = 0; i < 13; i++)
                        {
                            int min = (employer.MinimumCoverage != null && i < employer.MinimumCoverage.Length && employer.MinimumCoverage[i] == 1) ? 1 : 0;
                            int? ft = (employer.FullTime != null && i < employer.FullTime.Length && employer.FullTime[i] != 0) ? employer.FullTime[i] : (int?)null;
                            int? tot = (employer.Total != null && i < employer.Total.Length && employer.Total[i] != 0) ? employer.Total[i] : (int?)null;
                            int agg = (employer.AggregateGroup != null && i < employer.AggregateGroup.Length && employer.AggregateGroup[i] == 1) ? 1 : 0;

                            // FIX: sec declared as string? — value can be null when array is null/short
                            string? sec = (employer.Sec4980H != null && i < employer.Sec4980H.Length) ? employer.Sec4980H[i].ToString() : null;

                            p.Add($"@minimum_{i}", min);
                            p.Add($"@fullTime_{i}", ft);
                            p.Add($"@total_{i}", tot);
                            p.Add($"@group_{i}", agg);
                            p.Add($"@S4980H_{i}", sec);
                        }

                        result = await db.ExecuteScalarAsync<int>(
                            "sp_SaveEmployer1094Details_1",
                            p,
                            transaction: transaction,
                            commandType: CommandType.StoredProcedure);
                        if (result > 0)
                        {
                            await db.ExecuteAsync(
                                "UPDATE Employer SET disableChanges = @dc WHERE Id = @id",
                                new { dc = employer.DisableAutoCounts ? 1 : 0, id = result },
                                transaction: transaction);
                        }
                        transaction.Commit();
                        // AUDIT FIX: Log the Update
                        await _auditLogger.CaptureAndLogChangeAsync("Employer_1094", employer.Id, "UPDATE", oldRecord, employer);

                        return result;
                    }
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(SaveEmployer1094DetailsAsync), nameof(EmployerService), $"SQL error saving 1094 details for EmployerId {employer.Id}", "Service");
                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(SaveEmployer1094DetailsAsync), nameof(EmployerService), $"Unexpected error saving 1094 details for EmployerId {employer.Id}", "Service");
                return 0;
            }
        }        

        public async Task<bool> SaveEmployerImportantInfoAsync(EmployerImportantInfo model)
        {
            if (!int.TryParse(model.EmployerId, out int parsedEmployerId))
                return false;

            try
            {
                // AUDIT FIX: Capture old state
                var oldRecord = await GetEmployerImportantInfoAsync(model.EmployerId);
                int rows = 0;
                using (var db = Connection)
                {
                    db.Open();
                    using (var transaction = db.BeginTransaction())
                    {
                        var p = new DynamicParameters();
                        p.Add("@EmployerId", parsedEmployerId);
                        p.Add("@mailMemo", model.MailMemo);

                        rows = await db.ExecuteAsync(
                            "sp_AddUpdateEmployer_OtherInfo",
                            p,
                            transaction: transaction,
                            commandType: CommandType.StoredProcedure);

                        transaction.Commit();
                        return rows > 0;
                    }
                }
                // AUDIT FIX: Log the Update
                if (rows > 0)
                {
                    await _auditLogger.CaptureAndLogChangeAsync("Employer_OtherInfo", model.EmployerId, "UPDATE", oldRecord, model);
                }

                return rows > 0;
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(SaveEmployerImportantInfoAsync), nameof(EmployerService), $"SQL error saving important info for EmployerId {model.EmployerId}", "Service");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(SaveEmployerImportantInfoAsync), nameof(EmployerService), $"Unexpected error saving important info for EmployerId {model.EmployerId}", "Service");
                return false;
            }
        }

        public async Task<bool> UpdateEmployerCommonAsync(Employer employer)
        {
            // AUDIT FIX: Capture old state
            var oldRecord = await GetEmployerDetailsByIdAsync(employer.Aca_Id.ToString());
            using (var db = Connection)
            {
                db.Open();
                using (var transaction = db.BeginTransaction())
                {
                    try
                    {
                        var p = new DynamicParameters();
                        p.Add("@EmployerId", employer.Id);
                        p.Add("@ACA_EmployerId", employer.Aca_Id ?? 0);
                        p.Add("@EIN", employer.EIN);
                        p.Add("@Type", employer.Type);
                        p.Add("@Name", employer.Name);
                        p.Add("@FilingYear", employer.FilingYear);
                        p.Add("@Address1", employer.Address1);
                        p.Add("@Address2", employer.Address2);
                        p.Add("@City", employer.City);
                        p.Add("@State", employer.State);
                        p.Add("@StateId", employer.StateId);
                        p.Add("@Zip", employer.ZipCode);
                        p.Add("@Country", employer.Country);
                        p.Add("@IsForeignAddress", employer.IsForeignAddress);
                        p.Add("@IsCorrected", employer.IsCorrected);
                        p.Add("@EnableCallCenter", employer.enableCallCenter);

                        int rows = await db.ExecuteScalarAsync<int>(
                            "sp_UpdateEmployerCommon",
                            p,
                            transaction: transaction,
                            commandType: CommandType.StoredProcedure);

                        transaction.Commit();
                        // AUDIT FIX: Log the Update
                        if (rows > 0)
                        {
                            await _auditLogger.CaptureAndLogChangeAsync("Employer", employer.Aca_Id.ToString(), "UPDATE", oldRecord, employer);
                        }

                        return rows > 0;
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        _logger.LogError(ex, nameof(UpdateEmployerCommonAsync), nameof(EmployerService),
                            $"Error updating employer common data for EmployerId {employer.Id}", "Service");

                        // Bubble up the exact DB/SP error message instead of swallowing it
                        throw new InvalidOperationException(ex.Message, ex);
                    }
                }
            }
        }

        public async Task<(bool Success, string Message, DateTime? NewFollowUpDate)> UpdateEmployerServiceAsync(ServiceDetail service, string updatedByUserId)
        {
            // AUDIT FIX: Capture old state
            var oldRecord = await GetServiceDetail(service.EmployerId, service.PlanYear, service.ServiceListId);
            using (var db = Connection)
            {
                var processName1095 = await ResolveProcessNameFromWebNameAsync(db, service.ImplementationProcess);
                var processNameFTE = service.ProcessStep1094;
                var processNameState = service.StateFilingProcessStep;

                var p = new DynamicParameters();
                p.Add("@EmployerServiceId", service.ServiceId);
                p.Add("@EmployerId", service.EmployerId);
                p.Add("@ServiceId", service.ServiceListId);
                p.Add("@Status", service.Status);
                p.Add("@PlanYear", service.PlanYear);

                // FIX: ToDbDate now accepts string? — all these properties may be null
                p.Add("@ProposalSentDate", ToDbDate(service.ProposalSent));
                p.Add("@ProposalSentDate_Provided", string.IsNullOrWhiteSpace(service.ProposalSent) ? 0 : 1);
                p.Add("@ProposalSignedDate", ToDbDate(service.ProposalSigned));
                p.Add("@ProposalSignedDate_Provided", string.IsNullOrWhiteSpace(service.ProposalSigned) ? 0 : 1);
                p.Add("@ContractSentDate", ToDbDate(service.ContractSent));
                p.Add("@ContractSentDate_Provided", string.IsNullOrWhiteSpace(service.ContractSent) ? 0 : 1);
                p.Add("@ContractSignedDate", ToDbDate(service.ContractSigned));
                p.Add("@ContractSignedDate_Provided", string.IsNullOrWhiteSpace(service.ContractSigned) ? 0 : 1);
                p.Add("@ProcessStep1095", processName1095);
                p.Add("@FTETrackingStep", processNameFTE);
                p.Add("@StateFilingStep", processNameState);
                p.Add("@LastUpdated1095", ToDbDate(service.LastUpdated), DbType.Date);
                p.Add("@LastUpdatedFTE", ToDbDate(service.FTETrackingStep_lastupdated), DbType.Date);
                p.Add("@LastUpdatedState", ToDbDate(service.StateFilingProcessStepUpdated), DbType.Date);
                p.Add("@NextFollowUp1095", ToDbDate(service.FollowUpDate), DbType.Date);
                p.Add("@NextFollowUpFTE", ToDbDate(service.FTETrackingStep_nextfollowup), DbType.Date);
                p.Add("@NextFollowUpState", ToDbDate(service.StateFiling_FollowUpDate), DbType.Date);
                p.Add("@AuditBy", string.IsNullOrWhiteSpace(service.AuditBy) ? null : service.AuditBy);
                p.Add("@ReceiptID", service.ReceiptId);
                p.Add("@UpdatedBy", updatedByUserId);

                try
                {
                    var result = await db.QueryFirstOrDefaultAsync<dynamic>(
                        "sp_UpdateEmployerService_Tracker",
                        p,
                        commandType: CommandType.StoredProcedure
                    );

                    if (result != null && result.Success == 1)
                    {
                        // AUDIT FIX: Log the Update
                        await _auditLogger.CaptureAndLogChangeAsync("EmployerService", service.ServiceId, "UPDATE", oldRecord, service);

                        return (true, "Service updated successfully.", null);
                    }

                    return (false, "Update failed without throwing an error.", null);
                }
                catch (SqlException ex)
                {
                    return (false, $"Database error: {ex.Message}", null);
                }
                catch (Exception ex)
                {
                    return (false, "An unexpected error occurred during the update.", null);
                }
            }
        }

        public async Task<bool> DeleteEmployerAsync(int employerId)
        {
            try
            {
                using (var db = Connection)
                {
                    db.Open();
                    using (var transaction = db.BeginTransaction())
                    {
                        var p = new DynamicParameters();
                        p.Add("@EmployerId", employerId);

                        await db.ExecuteAsync(
                            "sp_DeleteEmployer",
                            p,
                            transaction: transaction,
                            commandType: CommandType.StoredProcedure);

                        transaction.Commit();
                        return true;
                    }
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(DeleteEmployerAsync), nameof(EmployerService), $"SQL error deleting EmployerId {employerId}", "Service");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(DeleteEmployerAsync), nameof(EmployerService), $"Unexpected error deleting EmployerId {employerId}", "Service");
                return false;
            }
        }

        public async Task<bool> DeleteAffiliateAsync(int id)
        {
            try
            {
                // AUDIT FIX: Capture old state
                var oldRecord = await GetAffiliateByIdAsync(id);
                using var db = Connection;
                var p = new DynamicParameters();
                p.Add("@Action", "DeleteAffiliate");
                p.Add("@AffiliateId", id);

                var result = await db.QueryFirstOrDefaultAsync<dynamic>("sp_Affiliate", p,
                                 commandType: CommandType.StoredProcedure);

                bool success = result != null && (bool)result.Success;
                // AUDIT FIX: Log the Delete
                if (success && oldRecord != null)
                {
                    await _auditLogger.CaptureAndLogChangeAsync("Affiliate", id.ToString(), "DELETE", oldRecord, null);
                }

                return success;
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(DeleteAffiliateAsync), nameof(EmployerService),
                                 $"SQL error. Id:{id}", "Service");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(DeleteAffiliateAsync), nameof(EmployerService),
                                 $"Unexpected error. Id:{id}", "Service");
                return false;
            }
        }
        #endregion

        #region 6. DASHBOARD & PORTFOLIO METHODS
        // ===================================================================
        // 6. DASHBOARD & PORTFOLIO METHODS
        // ===================================================================
        // ===================================================================
        // 5. DASHBOARD & PORTFOLIO METHODS
        // ===================================================================

        // FIX: Return type changed to Employer360ViewModel? — method returns null on not-found/error.
        //public async Task<Employer360ViewModel?> GetEmployer360Async(int employerId, int year)
        //{
        //    try
        //    {
        //        using (var db = Connection)
        //        {
        //            var model = new Employer360ViewModel { FilingYear = year };

        //            model.Employer = await GetEmployerDetailsByIdAsync(employerId.ToString());
        //            if (model.Employer == null) return null;

        //            var p = new DynamicParameters();
        //            p.Add("@EmployerId", employerId);
        //            p.Add("@FilingYear", year);

        //            using (var multi = await db.QueryMultipleAsync("sp_GetEmployer360Stats", p, commandType: CommandType.StoredProcedure))
        //            {
        //                var coverageRows = multi.Read<dynamic>().ToList();
        //                foreach (var row in coverageRows)
        //                    model.MonthlyCoverage.Add((int)row.MonthNum, (string)row.Status);

        //                var stats = multi.ReadFirstOrDefault<dynamic>();
        //                if (stats != null)
        //                {
        //                    model.TotalEmployees = (int)stats.TotalEmployees;
        //                    model.FullTimeCount = (int)stats.FullTimeCount;
        //                    model.FailedFilesCount = (int)stats.FailedFiles;
        //                    model.LastImportDate = stats.LastImportDate as DateTime?;
        //                }
        //            }

        //            var recentFilesParams = new DynamicParameters();
        //            recentFilesParams.Add("@TaxID", model.Employer.TaxId);
        //            recentFilesParams.Add("@Year", year);

        //            model.RecentFiles = (await db.QueryAsync<UploadedFileLog>(
        //                "sp_GetRecentFilesByEmployer",
        //                recentFilesParams,
        //                commandType: CommandType.StoredProcedure)).ToList();

        //            int monthsWithData = model.MonthlyCoverage?.Count(x => x.Value == "Covered") ?? 0;
        //            int dataScore = (int)((monthsWithData / 12.0) * 50);

        //            var codeExistsParams = new DynamicParameters();
        //            codeExistsParams.Add("@Id", employerId);
        //            codeExistsParams.Add("@Year", year);

        //            int codesExist = await db.ExecuteScalarAsync<int>(
        //                "sp_GetEmployerCodeExists",
        //                codeExistsParams,
        //                commandType: CommandType.StoredProcedure,
        //                commandTimeout: 60);

        //            int codeScore = (codesExist == 1) ? 50 : 0;
        //            model.ReadinessScore = dataScore + codeScore;

        //            return model;
        //        }
        //    }
        //    catch (SqlException ex)
        //    {
        //        _logger.LogError(ex, nameof(GetEmployer360Async), nameof(EmployerService), $"SQL error building 360 view for EmployerId {employerId}, Year {year}", "Service");
        //        return null;
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, nameof(GetEmployer360Async), nameof(EmployerService), $"Unexpected error building 360 view for EmployerId {employerId}, Year {year}", "Service");
        //        return null;
        //    }
        //}
        // FIX: Return type changed to Employer360ViewModel? — method returns null on not-found/error.
        public async Task<Employer360ViewModel?> GetEmployer360Async(int employerId, int year)
        {
            try
            {
                using (var db = Connection)
                {
                    var model = new Employer360ViewModel { FilingYear = year };

                    model.Employer = await GetEmployerDetailsByIdAsync(employerId.ToString());
                    if (model.Employer == null) return null;

                    var p = new DynamicParameters();
                    p.Add("@EmployerId", employerId);
                    p.Add("@FilingYear", year);

                    using (var multi = await db.QueryMultipleAsync("sp_GetEmployer360Stats", p, commandType: CommandType.StoredProcedure))
                    {
                        var coverageRows = multi.Read<dynamic>().ToList();
                        foreach (var row in coverageRows)
                            model.MonthlyCoverage.Add((int)row.MonthNum, (string)row.Status);

                        var stats = multi.ReadFirstOrDefault<dynamic>();
                        if (stats != null)
                        {
                            model.TotalEmployees = (int)stats.TotalEmployees;
                            model.FullTimeCount = (int)stats.FullTimeCount;
                            model.FailedFilesCount = (int)stats.FailedFiles;
                            model.LastImportDate = stats.LastImportDate as DateTime?;

                            // NEW: Safely map new fields (prevents crashing if sp_GetEmployer360Stats hasn't been updated yet)
                            var statsDict = (IDictionary<string, object>)stats;
                            if (statsDict.ContainsKey("PartTimeCount")) model.PartTimeCount = (int)stats.PartTimeCount;
                            if (statsDict.ContainsKey("VariableHoursCount")) model.VariableHoursCount = (int)stats.VariableHoursCount;
                            if (statsDict.ContainsKey("COBRACount")) model.COBRACount = (int)stats.COBRACount;
                        }
                    }

                    // 1. Fetch Recent Files (Already implemented in your code)
                    var recentFilesParams = new DynamicParameters();
                    recentFilesParams.Add("@TaxID", model.Employer.TaxId);
                    recentFilesParams.Add("@Year", year);

                    model.RecentFiles = (await db.QueryAsync<UploadedFileLog>(
                        "sp_GetRecentFilesByEmployer",
                        recentFilesParams,
                        commandType: CommandType.StoredProcedure)).ToList();

                    // 2. NEW: Fetch Last Calculation Date for the Timeline
                    string calcDateQuery = @"
                        SELECT MAX(CalculatedDate) 
                        FROM EmployerPenaltyCalculation 
                        WHERE EmployerId = @EmployerId AND TaxYear = @Year";

                    model.LastCalculationDate = await db.ExecuteScalarAsync<DateTime?>(calcDateQuery, new { EmployerId = employerId, Year = year });

                    // 3. Readiness Score Calculation
                    int monthsWithData = model.MonthlyCoverage?.Count(x => x.Value == "Covered") ?? 0;
                    int dataScore = (int)((monthsWithData / 12.0) * 50);

                    var codeExistsParams = new DynamicParameters();
                    codeExistsParams.Add("@Id", employerId);
                    codeExistsParams.Add("@Year", year);

                    int codesExist = await db.ExecuteScalarAsync<int>(
                        "sp_GetEmployerCodeExists",
                        codeExistsParams,
                        commandType: CommandType.StoredProcedure,
                        commandTimeout: 60);

                    int codeScore = (codesExist == 1) ? 50 : 0;
                    model.ReadinessScore = dataScore + codeScore;
                    // 4. NEW: ALE Monthly Headcount / 4980H Trend
                    var aleParams = new DynamicParameters();
                    aleParams.Add("@EmployerId", employerId);
                    model.AleTrend = (await db.QueryAsync<AleTrendMonthDto>(
                        "sp_GetALEMonthlyTrend",
                        aleParams,
                        commandType: CommandType.StoredProcedure)).ToList();

                    return model;
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(GetEmployer360Async), nameof(EmployerService), $"SQL error building 360 view for EmployerId {employerId}, Year {year}", "Service");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetEmployer360Async), nameof(EmployerService), $"Unexpected error building 360 view for EmployerId {employerId}, Year {year}", "Service");
                return null;
            }
        }
       
        public async Task<List<AmClientPortfolioDto>> GetAmClientPortfolioAsync(string amUserId)
        {
            try
            {
                using (var db = Connection)
                {
                    DateTime startOfMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

                    var p = new DynamicParameters();
                    p.Add("@UserId", amUserId);
                    p.Add("@StartOfMonth", startOfMonth);

                    var result = await db.QueryAsync<AmClientPortfolioDto>(
                        "sp_GetAmClientPortfolio",
                        p,
                        commandType: CommandType.StoredProcedure);

                    return result.ToList();
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(GetAmClientPortfolioAsync), nameof(EmployerService), $"SQL error fetching AM portfolio for UserId {amUserId}", "Service");
                return new List<AmClientPortfolioDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetAmClientPortfolioAsync), nameof(EmployerService), $"Unexpected error fetching AM portfolio for UserId {amUserId}", "Service");
                return new List<AmClientPortfolioDto>();
            }
        }

        public async Task<List<AmClientPortfolioDto>> GetBrokerClientPortfolioAsync(string brokerUserId)
        {
            try
            {
                using (var db = Connection)
                {
                    var startOfMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

                    var p = new DynamicParameters();
                    p.Add("@UserId", brokerUserId);
                    p.Add("@StartOfMonth", startOfMonth);

                    var result = await db.QueryAsync<AmClientPortfolioDto>(
                        "sp_GetBrokerClientPortfolio",
                        p,
                        commandType: CommandType.StoredProcedure);

                    return result.ToList();
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(GetBrokerClientPortfolioAsync), nameof(EmployerService), $"SQL error fetching broker portfolio for UserId {brokerUserId}", "Service");
                return new List<AmClientPortfolioDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetBrokerClientPortfolioAsync), nameof(EmployerService), $"Unexpected error fetching broker portfolio for UserId {brokerUserId}", "Service");
                return new List<AmClientPortfolioDto>();
            }
        }
        #endregion

        #region 7. ADDITIONAL METHODS
        // ===================================================================
        // 7. ADDITIONAL METHODS
        // ===================================================================

        public async Task<List<EmployerAggregatedMember>> GetAggregatedMembersAsync(int employerId)
        {
            try
            {
                using (var db = Connection)
                {
                    return (await db.QueryAsync<EmployerAggregatedMember>(
                        "sp_GetAggregatedGroupMembers",
                        new { Id = employerId },
                        commandType: CommandType.StoredProcedure)).ToList();
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(GetAggregatedMembersAsync), nameof(EmployerService), $"SQL error fetching aggregated members for EmployerId {employerId}", "Service");
                return new List<EmployerAggregatedMember>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetAggregatedMembersAsync), nameof(EmployerService), $"Unexpected error fetching aggregated members for EmployerId {employerId}", "Service");
                return new List<EmployerAggregatedMember>();
            }
        }

        public async Task<Employer1094BViewModel> GetEmployer1094BDetailsAsync(string employerId)
        {
            try
            {
                using (var db = Connection)
                {
                    var emp = await GetEmployerDetailsByIdAsync(employerId);

                    int totalForms = await db.ExecuteScalarAsync<int>(
                        "sp_GetEmployerFormCount",
                        new { Id = employerId },
                        commandType: CommandType.StoredProcedure);

                    return new Employer1094BViewModel
                    {
                        Employer = emp,
                        TotalForms = totalForms,
                        SignatureDate = DateTime.Now.ToShortDateString()
                    };
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(GetEmployer1094BDetailsAsync), nameof(EmployerService), $"SQL error fetching 1094B details for EmployerId {employerId}", "Service");
                return new Employer1094BViewModel { SignatureDate = DateTime.Now.ToShortDateString() };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetEmployer1094BDetailsAsync), nameof(EmployerService), $"Unexpected error fetching 1094B details for EmployerId {employerId}", "Service");
                return new Employer1094BViewModel { SignatureDate = DateTime.Now.ToShortDateString() };
            }
        }

        public async Task<List<ServiceListDropdown>> GetServiceDropdownList()
        {
            try
            {
                using (var db = Connection)
                {
                    var result = await db.QueryAsync<dynamic>(
                        "sp_GetDropdownDataService",
                        new { Action = "GetDropdownData" },
                        commandType: CommandType.StoredProcedure);

                    return result.Select(r => new ServiceListDropdown
                    {
                        ServiceListId = r.ServiceId?.ToString(),
                        Name = r.ServiceName?.ToString()
                    }).ToList();
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, nameof(GetServiceDropdownList), nameof(EmployerService), "SQL error fetching service dropdown list", "Service");
                return new List<ServiceListDropdown>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetServiceDropdownList), nameof(EmployerService), "Unexpected error fetching service dropdown list", "Service");
                return new List<ServiceListDropdown>();
            }
        }
        public async Task<string?> GetEmployerIdForYearAsync(string currentEmployerId, string filingYear)
        {
            try
            {
                using var db = Connection;
                var targetId = await db.QuerySingleOrDefaultAsync<int?>(
                    @"SELECT TOP 1 d.id
                   FROM dbo.Employer c
                   JOIN dbo.Employer d
                       ON d.taxid = c.taxid
                      AND d.filingYear = @Year
                      AND ISNULL(d.IsDeleted, 0) = 0
                   WHERE c.id = @Id
                   ORDER BY CASE
                                WHEN (d.companyId IS NULL AND c.companyId IS NULL)
                                  OR (d.companyId IS NOT NULL AND c.companyId IS NOT NULL)
                                THEN 0 ELSE 1
                            END, d.id",
                    new { Id = currentEmployerId, Year = filingYear });
                return targetId?.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetEmployerIdForYearAsync), nameof(EmployerService), $"currentId {currentEmployerId}, year {filingYear}", "Service");
                return null;
            }
        }
        #endregion
    }
}