using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using ACA360.Security.Encryption;
using ACA360.Security.Interfaces;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using ACA360.BusinessLogic.Interfaces;
using Dapper;
using Microsoft.Data.SqlClient;

namespace ACA360.BusinessLogic.Services
{
    public class PlanService : IPlanService
    {
        private readonly string _connectionString;
        private readonly ILoggerService _logger;
        private readonly IEncryptionService _encrypt;
        private readonly IDataAuditlogService _auditLogger;

        public PlanService(string connectionString, ILoggerService logger, IEncryptionService encrypt, IDataAuditlogService auditLogger)
        {
            _connectionString = connectionString;
            _logger = logger;
            _encrypt = encrypt;
            _auditLogger = auditLogger;
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);        

        #region 1.Plan Listing and Details

        public async Task<(List<Plan> Plans, PaginationViewEntity? PageRequest)> GetPlanList(string? empId, string? FillingYear, PaginationEntity paginationEntity, string? planType, string? fundingType)
        {
            var plans = new List<Plan>();
            PaginationViewEntity metadata = null;

            try
            {
                using var db = Connection;

                var parameters = new DynamicParameters();
                parameters.Add("@Employer_ID", empId);
                parameters.Add("@FillingYear", string.IsNullOrEmpty(FillingYear) ? DateTime.Now.Year.ToString() : FillingYear);
                parameters.Add("@PageIndex", paginationEntity.PageIndex);
                parameters.Add("@PageSize", paginationEntity.PageSize);
                parameters.Add("@Search", paginationEntity.Search);
                parameters.Add("@SortColumn", paginationEntity.SortColumn);
                parameters.Add("@SortOrder", paginationEntity.SortOrder);
                parameters.Add("@User_ID", null);
                parameters.Add("@FilterPlanType", string.IsNullOrWhiteSpace(planType) || planType == "Select Type" ? null : planType);
                parameters.Add("@FilterFundingType", string.IsNullOrWhiteSpace(fundingType) || fundingType == "Select Funding" ? null : fundingType);

                using var multi = await db.QueryMultipleAsync("sp_PlanList_New", parameters, commandType: CommandType.StoredProcedure);

                var planRecords = await multi.ReadAsync<dynamic>();
                foreach (var row in planRecords)
                {
                    plans.Add(new Plan
                    {
                        RowNum = row.RowNum ?? 0,
                        ide = row.id != null ? _encrypt.Encrypt(row.id.ToString()) : null,
                        Name = row.name,
                        BandingType = row.BandingTypeName,
                        EligibleFirstOfMonth = row.WaitingPeriodName,
                        FundingType = row.FundingTypeName,
                        MedicalPlan = row.PlanTypeName,
                        PlanRenewal = row.planRenewal
                    });
                }

                if (!multi.IsConsumed)
                {
                    int totalItems = await multi.ReadSingleOrDefaultAsync<int>();
                    int pageSize = paginationEntity.PageSize;
                    int currentPage = paginationEntity.PageIndex;

                    metadata = new PaginationViewEntity
                    {
                        TotalItems = totalItems,
                        PageSize = pageSize,
                        CurrentPage = currentPage,
                        TotalPages = (int)Math.Ceiling((double)totalItems / pageSize),
                        StartPage = (currentPage - 1) * pageSize + 1,
                        EndPage = Math.Min(currentPage * pageSize, totalItems),
                        SortColumn = paginationEntity.SortColumn,
                        SortOrder = paginationEntity.SortOrder,
                        filingYear = paginationEntity.fillingYear,
                        RecordCount = totalItems,
                        PageNumber = currentPage,
                        TotalCount = totalItems
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetPlanList", "Service", $"Failed to fetch plans with EmpId: {empId}", "Server or DB");
                throw;
            }

            return (plans, metadata);
        }

        public async Task<Plan> getPlan(string planId)
        {
            Plan plan = null;

            try
            {
                using var db = Connection;
                // Decrypt planId since frontend likely sends the encrypted version
                string decId = planId;
                try { decId = _encrypt.Decrypt(planId); } catch { /* Ignore if already decrypted */ }

                var parameters = new { Id = decId, Action = "View" };

                using var multi = await db.QueryMultipleAsync("sp_PlanDetails", parameters, commandType: CommandType.StoredProcedure);

                var planRecord = await multi.ReadSingleOrDefaultAsync<dynamic>();

                if (planRecord != null)
                {
                    plan = new Plan
                    {
                        ide = planId, // Keep encrypted string for frontend consistency
                        Name = planRecord.name?.ToString(),
                        MedicalPlan = planRecord.medicalPlan?.ToString(),
                        BandingType = planRecord.bandingType?.ToString(),

                        OfferedSpouse = ParseSqlSmallIntToBool(planRecord.offeredSpouse),
                        ConditionallyOffSpouse = ParseSqlSmallIntToBool(planRecord.conditionallyOffSpouse),
                        OfferedDependents = ParseSqlSmallIntToBool(planRecord.offeredDependents),

                        WaitingDays = !string.IsNullOrWhiteSpace(planRecord.waitingDays?.ToString()) ? Convert.ToInt32(planRecord.waitingDays) : 0,
                        EligibleFirstOfMonth = planRecord.eligibile1stOfMonth?.ToString(),
                        FundingType = planRecord.fundingType?.ToString(),

                        PlanRenewal = !string.IsNullOrWhiteSpace(planRecord.planRenewal?.ToString()) ? Convert.ToInt32(planRecord.planRenewal) : 0,
                        PlanTermTermination = ParseSqlSmallIntToBool(planRecord.planTermTermination),
                        MinimumValue = ParseSqlSmallIntToBool(planRecord.minimumValue),

                        PremiumCap = !string.IsNullOrWhiteSpace(planRecord.premiumCap?.ToString()) ? Convert.ToSingle(planRecord.premiumCap) : (float?)null,

                        CodeOneA = ParseSqlSmallIntToBool(planRecord.code1A),
                        CodeTwoF = ParseSqlSmallIntToBool(planRecord.code2F),
                        CodeTwoG = ParseSqlSmallIntToBool(planRecord.code2G),
                        CodeTwoH = ParseSqlSmallIntToBool(planRecord.code2H),

                        IsIchra = ParseSqlSmallIntToBool(planRecord.isIchra),
                        IchraLocationBasis = planRecord.ichraLocationBasis?.ToString(),
                        IchraSelfOnlyAllow = !string.IsNullOrWhiteSpace(planRecord.ichraSelfOnlyAllow?.ToString()) ? Convert.ToDecimal(planRecord.ichraSelfOnlyAllow) : (decimal?)null,

                        Benefits = new List<PlanBenefit>()
                    };

                    if (!multi.IsConsumed)
                    {
                        var benefitRecords = await multi.ReadAsync<dynamic>();
                        foreach (var b in benefitRecords)
                        {
                            plan.Benefits.Add(new PlanBenefit
                            {
                                Plan_start_value = b.bandingValueStart?.ToString(),
                                Plan_end_value = b.bandingValueEnd?.ToString(),
                                Premium_start = !string.IsNullOrWhiteSpace(b.bandingStartDate?.ToString()) ? Convert.ToDateTime(b.bandingStartDate) : null,
                                Premium_end = !string.IsNullOrWhiteSpace(b.bandingEndDate?.ToString()) ? Convert.ToDateTime(b.bandingEndDate) : null,
                                Amount = !string.IsNullOrWhiteSpace(b.amount?.ToString()) ? Convert.ToDecimal(b.amount) : 0
                            });
                        }
                    }
                }

                return plan;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "getPlan", "Service", $"Failed to fetch plan with PlanId: {planId}", "Server or DB");
                throw;
            }
        }
        #endregion

        #region 2.Drop_Down
        public async Task<List<SelectListItem>> GetAll_Plan_Banding_TypeAsync()
        {
            try
            {
                using var db = Connection;
                var result = await db.QueryAsync<Plan_Banding_Type>("usp_select_Plan_Banding_Type", commandType: CommandType.StoredProcedure);
                return result.Select(c => new SelectListItem { Text = c.Plan_Banding_Type_Name, Value = c.Plan_Banding_Type_ID.ToString() }).ToList();
            }
            catch (Exception) { return new List<SelectListItem>(); }
        }

        public async Task<List<SelectListItem>> GetAll_Plan_Waiting_PeriodAsync()
        {
            try
            {
                using var db = Connection;
                var result = await db.QueryAsync<Plan_Waiting_Period>("usp_select_Plan_Waiting_Period", commandType: CommandType.StoredProcedure);
                return result.Select(c => new SelectListItem { Text = c.Plan_Waiting_Period_Name, Value = c.Plan_Waiting_Period_ID.ToString() }).ToList();
            }
            catch (Exception) { return new List<SelectListItem>(); }
        }

        public async Task<List<SelectListItem>> GetAll_Plan_TypeAsync()
        {
            try
            {
                using var db = Connection;
                var result = await db.QueryAsync<Plan_Type>("usp_select_Plan_Type", commandType: CommandType.StoredProcedure);
                return result.Select(c => new SelectListItem { Text = c.Plan_Type_Name, Value = c.Plan_Type_ID.ToString() }).ToList();
            }
            catch (Exception) { return new List<SelectListItem>(); }
        }

        public async Task<List<SelectListItem>> GetAll_Plan_Funding_TypeAsync()
        {
            try
            {
                using var db = Connection;
                var result = await db.QueryAsync<Plan_Funding_Type>("usp_select_Plan_Funding_Type", commandType: CommandType.StoredProcedure);
                return result.Select(c => new SelectListItem { Text = c.Plan_Funding_Type_Name, Value = c.Plan_Funding_Type_ID.ToString() }).ToList();
            }
            catch (Exception) { return new List<SelectListItem>(); }
        }
        #endregion
        
        #region 3. Insert/Update Plan
        public async Task<bool> InsertOrUpdate(Plan plan, DataTable benefitsTable)
        {
            try
            {
                using var db = Connection;
                var parameters = new DynamicParameters();

                // 1. Add all your base parameters
                parameters.Add("@name", plan.Name);
                parameters.Add("@medicalPlan", plan.MedicalPlan);
                parameters.Add("@bandingType", plan.BandingType);
                parameters.Add("@offeredSpouse", plan.OfferedSpouse ? 1 : 0);
                parameters.Add("@conditionallyOffSpouse", plan.ConditionallyOffSpouse ? 1 : 0);
                parameters.Add("@offeredDependents", plan.OfferedDependents ? 1 : 0);
                parameters.Add("@waitingDays", plan.WaitingDays);
                parameters.Add("@eligibile1stOfMonth", plan.EligibleFirstOfMonth);
                parameters.Add("@fundingType", plan.FundingType);
                parameters.Add("@planRenewal", plan.PlanRenewal);
                parameters.Add("@planTermTermination", plan.PlanTermTermination ? 1 : 0);
                parameters.Add("@minimumValue", plan.MinimumValue ? 1 : 0);
                parameters.Add("@premiumCap", plan.PremiumCap);
                parameters.Add("@code1A", plan.CodeOneA ? 1 : 0);
                parameters.Add("@code2F", plan.CodeTwoF ? 1 : 0);
                parameters.Add("@code2G", plan.CodeTwoG ? 1 : 0);
                parameters.Add("@code2H", plan.CodeTwoH ? 1 : 0);
                parameters.Add("@isIchra", plan.IsIchra ? 1 : 0);
                parameters.Add("@ichraLocationBasis", string.IsNullOrWhiteSpace(plan.IchraLocationBasis) ? null : plan.IchraLocationBasis);
                parameters.Add("@ichraSelfOnlyAllow", plan.IchraSelfOnlyAllow);
                parameters.Add("@PlanBenefits", benefitsTable.AsTableValuedParameter("dbo.PlanBenefits"));

                string actionType = "INSERT";
                string recordId = "";
                Plan oldPlan = null;

                // 2. Handle Insert vs Update Logic
                if (string.IsNullOrEmpty(plan.ide) || plan.ide == "0")
                {
                    parameters.Add("@Action", "Create");
                    parameters.Add("@employerId", plan.EmployerId);

                    // Execute AND capture the newly created ID in one line
                    var newIdResult = await db.QueryFirstOrDefaultAsync<string>("sp_PlanDetails", parameters, commandType: CommandType.StoredProcedure);
                    recordId = newIdResult ?? "Unknown-ID";
                }
                else
                {
                    actionType = "UPDATE";
                    oldPlan = await getPlan(plan.ide);

                    // Cleanly decrypt the ID on one line
                    try { recordId = _encrypt.Decrypt(plan.ide); } catch { recordId = plan.ide; }

                    parameters.Add("@Id", recordId);
                    parameters.Add("@Action", "Update");

                    // Execute normally for updates
                    await db.ExecuteAsync("sp_PlanDetails", parameters, commandType: CommandType.StoredProcedure);
                }

                // 3. PREVENT FALSE POSITIVES: Sync references if benefits haven't actually changed
                if (actionType == "UPDATE" && oldPlan != null)
                {
                    // Check if the data is mathematically and logically identical
                    if (AreBenefitsEqual(oldPlan.Benefits, plan.Benefits))
                    {
                        // By assigning the old reference to the new plan, your delta logger 
                        // will see the exact same object in memory and ignore it.
                        plan.Benefits = oldPlan.Benefits;
                    }
                }

                // 4. Log the change using the Delta algorithm
                await _auditLogger.CaptureAndLogChangeAsync(
                    tableName: "Plan",
                    recordId: recordId,
                    actionType: actionType,
                    oldRecord: oldPlan,
                    newRecord: plan
                );

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "InsertOrUpdate", "Service", $"Failed to save plan: {plan.ide}", "Server or DB");
                throw;
            }
        }
        #endregion

        #region 4. Compare Benefits Helper
        // Helper to safely parse SQL SMALLINT (0/1) or Boolean strings to C# bool
        private bool ParseSqlSmallIntToBool(object dbValue)
        {
            if (dbValue == null || dbValue == DBNull.Value || string.IsNullOrWhiteSpace(dbValue.ToString())) return false;
            string val = dbValue.ToString().Trim().ToLower();
            if (val == "1" || val == "true") return true;
            if (val == "0" || val == "false") return false;

            if (int.TryParse(val, out int iRes)) return iRes > 0;
            if (bool.TryParse(val, out bool bRes)) return bRes;
            return false;
        }
       
        private bool AreBenefitsEqual(IEnumerable<PlanBenefit> oldBenefits, IEnumerable<PlanBenefit> newBenefits)
        {
            // Handle null scenarios
            if (oldBenefits == null && newBenefits == null) return true;
            if (oldBenefits == null || newBenefits == null) return false;

            var oldList = oldBenefits.ToList();
            var newList = newBenefits.ToList();

            // If the number of rows changed, it's definitely a change
            if (oldList.Count != newList.Count) return false;

            // Compare each row manually to bypass JSON trailing zero issues entirely
            for (int i = 0; i < oldList.Count; i++)
            {
                var oldItem = oldList[i];
                var newItem = newList[i];

                if (oldItem.Plan_start_value != newItem.Plan_start_value ||
                    oldItem.Plan_end_value != newItem.Plan_end_value ||
                    oldItem.Premium_start != newItem.Premium_start ||
                    oldItem.Premium_end != newItem.Premium_end ||
                    // Mathematically compare the amounts (108.50m == 108.5m will return true)
                    oldItem.Amount != newItem.Amount)
                {
                    return false; // A difference was found!
                }
            }

            return true; // Everything matched perfectly
        }
        #endregion

        #region 5.Delete Plan
        public async Task<bool> PlanDelete(string planId)
        {
            try
            {
                using var db = Connection;
                var decryptedId = _encrypt.Decrypt(planId);
                var oldRecord = await getPlan(planId);
                await db.ExecuteAsync("sp_PlanDeletee", new { PlanId = decryptedId }, commandType: CommandType.StoredProcedure);

                if (oldRecord != null)
                {
                    await _auditLogger.CaptureAndLogChangeAsync(
                        tableName: "Plan", // Put the actual SQL table name here
                        recordId: planId,
                        actionType: "DELETE",
                        oldRecord: oldRecord,
                        newRecord: null
                    );
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PlanDelete", "Service", $"Failed to delete plan: {planId}", "Server or DB");
                throw;
            }
        }
        #endregion


        public async Task<string?> GetPlanIdForCompareAsync(string currentPlanId, string? targetEmployerId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(targetEmployerId)) return null;
                var cur = await getPlan(currentPlanId);
                if (cur == null || string.IsNullOrWhiteSpace(cur.Name)) return null;
                var pe = new PaginationEntity { PageIndex = 1, PageSize = 1000 };
                var (plans, _) = await GetPlanList(targetEmployerId, null, pe, null, null);
                var match = plans?.FirstOrDefault(p => string.Equals((p.Name ?? "").Trim(), cur.Name.Trim(), StringComparison.OrdinalIgnoreCase));
                return match?.ide;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetPlanIdForCompareAsync", "Service", $"planId {currentPlanId}, targetEmp {targetEmployerId}", "Server or DB");
                return null;
            }
        }


    }
}