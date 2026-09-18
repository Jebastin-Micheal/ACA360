using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Models;
using Dapper;
using DocumentFormat.OpenXml.Vml.Office;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Services
{
    public class ACALogicService : IACALogicService
    {
        private readonly string _connectionString;
        private readonly ILogger<ACALogicService> _logger;
        public ACALogicService(string connectionString, ILogger<ACALogicService> logger)
        {
            try
            {
                _connectionString = connectionString;
                _logger = logger;
            }
            catch (Exception ex)
            {
                if (_logger != null)
                {
                    _logger.LogError(ex, "Error initializing ACALogicService.");
                }
                throw;
            }
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        // ============================================================
        // MAIN ENGINE
        // ============================================================

        /// <summary>
        /// Purpose: Generates 1095-C codes and calculates flags for the specified employer.
        /// Input parameters: int employerId, int year
        /// Output/return value: Task
        /// </summary>
        public async Task GenerateCodesForEmployerAsync(int employerId, int year)
        {
            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                // STEP 1: Initialization (Safety Net)
                await db.ExecuteAsync("sp_InitEmployeeCodeSafetyNet",
                    new { EmployerId = employerId, Year = year },
                    transaction: tx,
                    commandType: CommandType.StoredProcedure);

                // STEP 2: Run the Code Generation Engine
                await db.ExecuteAsync("sp_Generate1095Codes",
                    new
                    {
                        EmployerId = employerId,
                        FilingYear = year,
                        CurrentUserId = "Admin-ReCalc"
                    },
                    transaction: tx,
                    commandType: CommandType.StoredProcedure,
                    commandTimeout: 300);
                // STEP 2.5: Recompute "Will Receive Form" (getForm). The generation
                // engine never sets it, so it is recalculated here from the employee's
                // actual status/enrollment/form-type.
                await db.ExecuteAsync("sp_UpdateReceiveForm",
                    new
                    {
                        EmployerId = employerId,
                        FilingYear = year
                    },
                    transaction: tx,
                    commandType: CommandType.StoredProcedure,
                    commandTimeout: 300);
                // STEP 3: Run the Flag Engine
                await db.ExecuteAsync("sp_RunDynamicFlagEngine",
                    new
                    {
                        EmployerId = employerId,
                        TaxYear = year
                    },
                    transaction: tx,
                    commandType: CommandType.StoredProcedure,
                    commandTimeout: 300);

                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in GenerateCodesForEmployerAsync for EmployerId: {EmployerId}", employerId);
                throw;
            }
        }
        public async Task RecalculateFlagsForEmployeeAsync(int year, int employeeId)
        {
            try
            {
                using var db = Connection;
                await db.ExecuteAsync("sp_RunDynamicFlagEngine",
                    new { TaxYear = year, EmployeeId = employeeId },
                    commandType: CommandType.StoredProcedure, commandTimeout: 120);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error in RecalculateFlagsForEmployeeAsync for EmployeeId: {EmployeeId}", employeeId);
                throw;
            }
        }
        private async Task<FilingYearRules> GetFilingYearRulesAsync(int year)
        {
            try
            {
                using var db = Connection;
                var rules = await db.QueryFirstOrDefaultAsync<FilingYearRules>(
                    "SELECT filingYear, fpgPremium, fpgPercent, ropHours, offerPercent, PenaltyA_Annual, PenaltyB_Annual " +
                    "FROM FilingYear WHERE filingYear = @Year",
                    new { Year = year });

                // Provide IRS safe-harbor defaults if the year row is missing
                if (rules == null)
                {
                    _logger.LogWarning("No FilingYear rules found for {Year}. Using system defaults.", year);
                    return new FilingYearRules
                    {
                        FilingYear = year,
                        FpgPremium = 113.20m,  // Standard default
                        FpgPercent = 0.0902m,  // 9.02% default
                        RopHours = 130,
                        OfferPercent = 0.95m
                    };
                }

                return rules;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve FilingYear rules for {Year}.", year);
                throw;
            }
        }
        /// <summary>
        /// Purpose: Retrieves a list of employee codes for review.
        /// Input parameters: int employerId, int year
        /// Output/return value: Task of List of EmployeeCodeReviewDto
        /// </summary>
        public async Task<List<EmployeeCodeReviewDto>> GetEmployeeCodesListAsync(int employerId, int year)
        {
            try
            {
                using var db = Connection;
                var result = await db.QueryAsync<EmployeeCodeReviewDto>(
                    "sp_GetEmployeeCodesList",
                    new { EmployerId = employerId, Year = year },
                    commandType: CommandType.StoredProcedure);
                return result.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetEmployeeCodesListAsync for EmployerId: {EmployerId}", employerId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Retrieves specific codes for a single employee.
        /// Input parameters: int employeeId, int year
        /// Output/return value: Task of EmployeeCode
        /// </summary>
        public async Task<EmployeeCode?> GetCodesForEmployeeAsync(int employeeId, int year)
        {
            try
            {
                using var db = Connection;
                return await db.QuerySingleOrDefaultAsync<EmployeeCode>(
                    "sp_GetCodesForEmployee",
                    new { Id = employeeId, Year = year },
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetCodesForEmployeeAsync for EmployeeId: {EmployeeId}", employeeId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Updates manual codes for an employee and logs audit changes.
        /// Input parameters: EmployeeCode updated, string userId
        /// Output/return value: Task
        /// </summary>
        public async Task UpdateManualCodesAsync(EmployeeCode updated, string userId)
        {
            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                var existing = await db.QuerySingleOrDefaultAsync<EmployeeCode>(
                    "sp_GetEmployeeCodeForUpdate",
                    new { EmpId = updated.EmployeeId, Year = updated.FilingYear },
                    transaction: tx,
                    commandType: CommandType.StoredProcedure
                );

                string[] months = { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };

                foreach (var m in months)
                {
                    await LogIfChanged(existing, updated, m, "COC", 14, "MANUAL", "Manual Line 14 override");
                    await LogIfChanged(existing, updated, m, "LCMP", 15, "MANUAL", "Manual Line 15 premium override");
                    await LogIfChanged(existing, updated, m, "SHC", 16, "MANUAL", "Manual Line 16 safe harbor override");
                }

               
                await db.ExecuteAsync(
                    "sp_UpdateManualCodes",
                    new
                    {
                        EmployeeId = updated.EmployeeId,
                        FilingYear = updated.FilingYear,
                        IsLocked = updated.IsLocked,                         
                        JAN_COC = updated.JAN_COC,
                        FEB_COC = updated.FEB_COC,
                        MAR_COC = updated.MAR_COC,
                        APR_COC = updated.APR_COC,
                        MAY_COC = updated.MAY_COC,
                        JUN_COC = updated.JUN_COC,
                        JUL_COC = updated.JUL_COC,
                        AUG_COC = updated.AUG_COC,
                        SEP_COC = updated.SEP_COC,
                        OCT_COC = updated.OCT_COC,
                        NOV_COC = updated.NOV_COC,
                        DEC_COC = updated.DEC_COC,
                        JAN_LCMP = updated.JAN_LCMP,
                        FEB_LCMP = updated.FEB_LCMP,
                        MAR_LCMP = updated.MAR_LCMP,
                        APR_LCMP = updated.APR_LCMP,
                        MAY_LCMP = updated.MAY_LCMP,
                        JUN_LCMP = updated.JUN_LCMP,
                        JUL_LCMP = updated.JUL_LCMP,
                        AUG_LCMP = updated.AUG_LCMP,
                        SEP_LCMP = updated.SEP_LCMP,
                        OCT_LCMP = updated.OCT_LCMP,
                        NOV_LCMP = updated.NOV_LCMP,
                        DEC_LCMP = updated.DEC_LCMP,
                        JAN_SHC = updated.JAN_SHC,
                        FEB_SHC = updated.FEB_SHC,
                        MAR_SHC = updated.MAR_SHC,
                        APR_SHC = updated.APR_SHC,
                        MAY_SHC = updated.MAY_SHC,
                        JUN_SHC = updated.JUN_SHC,
                        JUL_SHC = updated.JUL_SHC,
                        AUG_SHC = updated.AUG_SHC,
                        SEP_SHC = updated.SEP_SHC,
                        OCT_SHC = updated.OCT_SHC,
                        NOV_SHC = updated.NOV_SHC,
                        DEC_SHC = updated.DEC_SHC
                    },
                    transaction: tx,
                    commandType: CommandType.StoredProcedure
                );
                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in UpdateManualCodesAsync for EmployeeId: {EmployeeId}", updated?.EmployeeId);
                throw;
            }

            // ---------------- LOCAL FUNCTION ----------------
            async Task LogIfChanged(
                EmployeeCode oldRec,
                EmployeeCode newRec,
                string month,
                string suffix,
                int line,
                string source,
                string reason)
            {
                try
                {
                    string field = $"{month}_{suffix}";
                    var oldVal = GetFieldValue(oldRec, field);
                    var newVal = GetFieldValue(newRec, field);

                    if (oldVal == newVal) return;

                    await LogAuditAsync(new EmployeeCodeAuditDto
                    {
                        EmployeeId = oldRec.EmployeeId,
                        EmployerId = oldRec.EmployerId,
                        FilingYear = oldRec.FilingYear.ToString(),

                        MonthCode = month,
                        AppliesToAllMonths = false,

                        LineNumber = line,
                        FieldName = field,

                        OldValue = oldVal,
                        NewValue = newVal,

                        ChangeSource = "MANUAL",
                        ChangeReason = reason,
                        SafeHarborApplied = line == 16 ? newVal : null,

                        ChangedByUserId = userId,
                        IsSystemGenerated = false
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in LogIfChanged local function.");
                    throw;
                }
            }
        }

        // ============================================================
        // CALCULATION
        // ============================================================

        /// <summary>
        /// Purpose: Calculates the monthly ACA code based on employee context.
        /// Input parameters: CalculationContext emp, int year, int month
        /// Output/return value: MonthlyCodeResult
        /// </summary>
        /// <summary>
        /// Purpose: Calculates the monthly ACA code based on employee context.
        /// Input parameters: CalculationContext emp, int year, int month, FilingYearRules rules
        /// Output/return value: MonthlyCodeResult
        /// </summary>
        private MonthlyCodeResult CalculateMonthlyCode(CalculationContext emp, int year, int month, FilingYearRules rules)
        {
            try
            {
                var result = new MonthlyCodeResult();
                var first = new DateTime(year, month, 1);
                var last = first.AddMonths(1).AddDays(-1);

                bool isEmployed =
                    emp.HireDate <= last &&
                    (!emp.TerminationDate.HasValue || emp.TerminationDate.Value >= first);

                bool isFullTime = emp.Status == 1;

                bool isEnrolled =
                    emp.CoverageElected &&
                    emp.CoverageStartDate.HasValue &&
                    emp.CoverageStartDate.Value <= first &&
                    (!emp.CoverageEndDate.HasValue || emp.CoverageEndDate.Value >= last);

                if (!isEmployed)
                {
                    result.Line14 = "1H";
                    result.Line16 = "2A";
                    return result;
                }

                if (!isFullTime && !isEnrolled)
                {
                    result.Line14 = "1H";
                    result.Line16 = "2B";
                    return result;
                }

                if (isFullTime && !isEnrolled &&
                    emp.DateEligible.HasValue &&
                    emp.DateEligible.Value > first)
                {
                    result.Line14 = "1H";
                    result.Line16 = "2D";
                    return result;
                }

                if (!emp.PlanId.HasValue)
                    result.Line14 = "1H";
                else if (emp.MinimumValue && emp.OfferedToSpouse && emp.OfferedToDependents &&
                         emp.LowestCostPremium.HasValue &&
                         emp.LowestCostPremium.Value <= rules.FpgPremium)
                    result.Line14 = "1A";
                else if (emp.OfferedToSpouse && emp.OfferedToDependents)
                    result.Line14 = "1E";
                else if (emp.OfferedToSpouse)
                    result.Line14 = "1D";
                else if (emp.OfferedToDependents)
                    result.Line14 = "1C";
                else
                    result.Line14 = "1B";

                if (result.Line14 != "1H" && result.Line14 != "1A")
                    result.Line15 = emp.LowestCostPremium;

                if (isEnrolled)
                    result.Line16 = "2C";
                else if (result.Line14 != "1H")
                    result.Line16 = DetermineSafeHarbor(emp, rules); // Passing rules downstream

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in CalculateMonthlyCode.");
                throw;
            }
        }

        // ============================================================
        // DATA FETCH
        // ============================================================

        /// <summary>
        /// Purpose: Fetches calculation context data for an employer.
        /// Input parameters: int employerId, int year
        /// Output/return value: Task of List of CalculationContext
        /// </summary>
        private async Task<List<CalculationContext>> FetchCalculationDataAsync(int employerId, int year)
        {
            try
            {
                using var db = Connection;
                var results = await db.QueryAsync<CalculationContext>(
                    "sp_FetchCalculationData",
                    new { EmployerId = employerId, Year = year },
                    commandType: CommandType.StoredProcedure);
                return results.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in FetchCalculationDataAsync for EmployerId: {EmployerId}", employerId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Gets covered individuals for a specific employee.
        /// Input parameters: int employeeId, int year
        /// Output/return value: Task of List of CoveredIndividualModel
        /// </summary>
        public async Task<List<CoveredIndividualModel>> GetCoveredIndividualsAsync(int employeeId, int year)
        {
            try
            {
                var results = new List<CoveredIndividualModel>();

                using var db = Connection;

                // 1. Funding Type Check
                string? fundingType = await db.QuerySingleOrDefaultAsync<string>(
                    "sp_GetEmployeeFundingType",
                    new { EmpId = employeeId },
                    commandType: CommandType.StoredProcedure);

                // =========================================================
                // SOURCE 1: DEPENDENTS (With Robust Null Handling)
                // =========================================================
                var dependentsRaw = await db.QueryAsync<dynamic>(
                    "sp_GetCoveredDependents",
                    new { EmpId = employeeId, Year = year },
                    commandType: CommandType.StoredProcedure);

                // 3. Fetch Employee Enrollment
                var enrollment = await db.QuerySingleOrDefaultAsync<dynamic>(
                    "sp_GetEmployeeEnrollmentDates",
                    new { EmpId = employeeId },
                    commandType: CommandType.StoredProcedure);
                DateTime? empStart = enrollment?.CoverageStartDate;
                DateTime? empEnd = enrollment?.CoverageEndDate;

                // 4. Process Dependents
                foreach (var row in dependentsRaw)
                {
                    var model = new CoveredIndividualModel
                    {
                        Id = Convert.ToInt32(row.id),
                        EmployeeCodeId = Convert.ToInt32(row.employeeCodeId),
                        FirstName = row.firstName,
                        MiddleName = row.middleName,
                        LastName = row.lastName,
                        SSN = row.ssn,
                        Suffix = row.suffix,
                        Birthday = row.birthday,
                        CoverageStartDate = row.coverageStartDate,
                        CoverageEndDate = row.coverageEndDate
                    };

                    int hasManual = Convert.ToInt32(row.HasManualCode);

                    if (hasManual == 1)
                    {
                        model.AllM = Convert.ToInt32(row.AllM) != 0;
                        model.Jan = Convert.ToInt32(row.Jan) != 0;
                        model.Feb = Convert.ToInt32(row.Feb) != 0;
                        model.Mar = Convert.ToInt32(row.Mar) != 0;
                        model.Apr = Convert.ToInt32(row.Apr) != 0;
                        model.May = Convert.ToInt32(row.May) != 0;
                        model.Jun = Convert.ToInt32(row.Jun) != 0;
                        model.Jul = Convert.ToInt32(row.Jul) != 0;
                        model.Aug = Convert.ToInt32(row.Aug) != 0;
                        model.Sep = Convert.ToInt32(row.Sep) != 0;
                        model.Oct = Convert.ToInt32(row.Oct) != 0;
                        model.Nov = Convert.ToInt32(row.Nov) != 0;
                        model.Dec = Convert.ToInt32(row.Dec) != 0;
                    }
                    else
                    {
                        bool[] coveredMonths = CalculateMonths(year, empStart, empEnd, model.CoverageStartDate ?? empStart, model.CoverageEndDate ?? empEnd);
                        MapMonthsToModel(model, coveredMonths);
                    }

                    results.Add(model);
                }

                // =========================================================
                // SOURCE 2: THE EMPLOYEE (Self)
                // =========================================================
                if (enrollment != null)
                {
                    var employee = await db.QuerySingleOrDefaultAsync<CoveredIndividualModel>(
                        "sp_GetEmployeeAsCoveredIndividual",
                        new { Id = employeeId },
                        commandType: CommandType.StoredProcedure);

                    if (employee != null && !results.Any(r => r.SSN == employee.SSN))
                    {
                        bool[] empMonths = CalculateMonths(year, empStart, empEnd, empStart, empEnd);
                        MapMonthsToModel(employee, empMonths);
                        results.Insert(0, employee);
                    }
                }

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetCoveredIndividualsAsync for EmployeeId: {EmployeeId}", employeeId);
                throw;
            }
        }

        // --- Helper Functions to keep code clean ---

        /// <summary>
        /// Purpose: Calculates true/false array for covered months.
        /// Input parameters: int year, DateTime? subStart, DateTime? subEnd, DateTime? indivStart, DateTime? indivEnd
        /// Output/return value: bool[]
        /// </summary>
        private bool[] CalculateMonths(int year, DateTime? subStart, DateTime? subEnd, DateTime? indivStart, DateTime? indivEnd)
        {
            try
            {
                bool[] covered = new bool[13];

                // The covered window is the intersection of the subscriber's span and
                // the individual's own. A null bound means that side places no limit,
                // so it must not collapse the window — which is what the previous
                // expression did: comparing a real date against a null subStart is
                // false in C#, so it fell through to DateTime.MaxValue and reported
                // the dependent as covered for no months at all. Harmless only while
                // dependents had no dates of their own; once the import carries them
                // (F-07), any employee without an EmployeeEnrollment row would have
                // hit it.
                DateTime? effectiveStart =
                    indivStart.HasValue && subStart.HasValue
                        ? (indivStart > subStart ? indivStart : subStart)
                        : indivStart ?? subStart;

                DateTime? effectiveEnd =
                    indivEnd.HasValue && subEnd.HasValue
                        ? (indivEnd < subEnd ? indivEnd : subEnd)
                        : indivEnd ?? subEnd;

                // No start on either side is no evidence of coverage.
                if (effectiveStart == null)
                    return covered;

                // Neither bound varies by month, so both are resolved once.
                for (int m = 1; m <= 12; m++)
                {
                    var firstDayOfMonth = new DateTime(year, m, 1);
                    var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);

                    // A month counts as covered if the window touches any part of it,
                    // matching the IRS treatment of a partial month of coverage.
                    if (effectiveStart <= lastDayOfMonth
                        && (effectiveEnd == null || effectiveEnd >= firstDayOfMonth))
                    {
                        covered[m] = true;
                    }
                }

                return covered;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in CalculateMonths.");
                throw;
            }
        }

        /// <summary>
        /// Purpose: Maps array of booleans to the covered individual model.
        /// Input parameters: CoveredIndividualModel model, bool[] months
        /// Output/return value: void
        /// </summary>
        private void MapMonthsToModel(CoveredIndividualModel model, bool[] months)
        {
            try
            {
                model.Jan = months[1];
                model.Feb = months[2];
                model.Mar = months[3];
                model.Apr = months[4];
                model.May = months[5];
                model.Jun = months[6];
                model.Jul = months[7];
                model.Aug = months[8];
                model.Sep = months[9];
                model.Oct = months[10];
                model.Nov = months[11];
                model.Dec = months[12];
                model.AllM = months.Skip(1).All(x => x);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in MapMonthsToModel.");
                throw;
            }
        }

        /// <summary>
        /// Purpose: Forces a recalculation of dynamic flags.
        /// Input parameters: int employerId, int year
        /// Output/return value: Task
        /// </summary>
        public async Task RecalculateFlagsAsync(int employerId, int year)
        {
            try
            {
                using var db = Connection;
                await db.ExecuteAsync("sp_RunDynamicFlagEngine",
                    new { EmployerId = employerId, TaxYear = year },
                    commandType: CommandType.StoredProcedure,commandTimeout: 300);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in RecalculateFlagsAsync for EmployerId: {EmployerId}", employerId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Calculates overall penalty risk for an employer.
        /// Input parameters: int employerId, int year
        /// Output/return value: Task of PenaltyRiskDto
        /// </summary>
        public async Task<PenaltyRiskDto> CalculatePenaltyRiskAsync(int employerId, int year)
        {
            try
            {
                using var db = Connection;
                // minimum_1..12 drives the 4980H(a) test, so refresh it first �
                // otherwise a stale 1094-C silently produces a zero assessment.
                await db.ExecuteAsync("sp_Generate1094CValues",
                    new { EmployerId = employerId, FilingYear = year },
                    commandType: CommandType.StoredProcedure);
                // 1. RUN THE ENGINE
                await db.ExecuteAsync("sp_CalculatePotentialPenalties",
                    new { EmployerId = employerId, TaxYear = year },
                    commandType: CommandType.StoredProcedure);

                // 2. FETCH SUMMARY (Map DB Table to DTO)
                var dto = await db.QueryFirstOrDefaultAsync<PenaltyRiskDto>(
                    "sp_GetEmployerPenaltySummary",
                    new { Id = employerId, Year = year },
                    commandType: CommandType.StoredProcedure);

                if (dto == null) return new PenaltyRiskDto { TaxYear = year };

                // 3. FETCH DETAILS (The Grid)
                var employees = await db.QueryAsync<AtRiskEmployeeDto>(
                    "sp_GetEmployeePenaltyDetails",
                    new
                    {
                        Id = employerId,
                        Year = year,
                        RateB = dto.TypeB_MonthlyRate
                    },
                    commandType: CommandType.StoredProcedure);

                dto.AtRiskEmployees = employees.ToList();

                return dto;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in CalculatePenaltyRiskAsync for EmployerId: {EmployerId}", employerId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Retrieves current penalty risk summary and details for an employer.
        /// Input parameters: int employerId, int year
        /// Output/return value: Task of PenaltyRiskDto
        /// </summary>
        public async Task<PenaltyRiskDto> GetPenaltyRiskAsync(int employerId, int year)
        {
            try
            {
                using var db = Connection;

                // 1. SAFETY CHECK
                int codeCount = await db.ExecuteScalarAsync<int>(
                    "sp_GetEmployeeCodeCount",
                    new { Id = employerId, Year = year },
                    commandType: CommandType.StoredProcedure);

                if (codeCount == 0)
                {
                    await db.ExecuteAsync("sp_Generate1095Codes",
                        new { EmployerId = employerId, FilingYear = year },
                        commandType: CommandType.StoredProcedure);
                    // Recompute "Will Receive Form" (getForm) � see sp_UpdateReceiveForm.
                    await db.ExecuteAsync("sp_UpdateReceiveForm",
                        new { EmployerId = employerId, FilingYear = year },
                        commandType: CommandType.StoredProcedure);
                    await RecalculateFlagsAsync(employerId, year);
                    await CalculatePenaltyRiskAsync(employerId, year);
                }

                // 1. FETCH SUMMARY
                var dto = await db.QueryFirstOrDefaultAsync<PenaltyRiskDto>(
                    "sp_GetEmployerPenaltySummary",
                    new { Id = employerId, Year = year },
                    commandType: CommandType.StoredProcedure);

                if (dto == null) return new PenaltyRiskDto { TaxYear = year };

                // 2. FETCH DETAILS
                var employees = await db.QueryAsync<AtRiskEmployeeDto>(
                    "sp_GetDistinctBadMonthsDetails",
                    new
                    {
                        Id = employerId,
                        Year = year,
                        RateB = dto.TypeB_MonthlyRate
                    },
                    commandType: CommandType.StoredProcedure);

                dto.AtRiskEmployees = employees.ToList();

                // 2. FETCH TYPE A DETAILS
                if (dto.TypeA_Triggered)
                {
                    var typeARisks = await db.QueryAsync<AtRiskEmployeeDto>(
                        "sp_GetTypeAAtRiskEmployees",
                        new { Id = employerId, Year = year },
                        commandType: CommandType.StoredProcedure);

                    dto.TypeA_AtRiskEmployees = typeARisks.ToList();
                }

                // --- NEW: FETCH MISSING PLAN START COUNT ---
                dto.MissingPlanStartCount = await db.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM EmployeeCode WHERE EmployerId = @Id AND FilingYear = @Year AND PlanStartMonth IS NULL",
                    new { Id = employerId, Year = year }
                );
                return dto;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetPenaltyRiskAsync for EmployerId: {EmployerId}", employerId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Automatically sets missing Plan Start Month for all unlocked employees.
        /// Input parameters: int employerId, int year, string startMonth
        /// Output/return value: Task of int (rows affected)
        /// </summary>
        //public async Task<int> AutoFixPlanStartMonthAsync(int employerId, int year, string startMonth)
        //{
        //    using var db = Connection;
        //    db.Open();
        //    using var tx = db.BeginTransaction();
        //    try
        //    {
        //        int result = await db.ExecuteAsync(
        //            "sp_AutoFixPlanStartMonth",
        //            new { EmpId = employerId, Year = year, Month = startMonth },
        //            transaction: tx,
        //            commandType: CommandType.StoredProcedure);

        //        tx.Commit();
        //        return result;
        //    }
        //    catch (Exception ex)
        //    {
        //        tx.Rollback();
        //        _logger.LogError(ex, "Error occurred in AutoFixPlanStartMonthAsync for EmployerId: {EmployerId}", employerId);
        //        throw;
        //    }
        //}
        public async Task<int> AutoFixPlanStartMonthAsync(int employerId, int year, string startMonth)
        {
            try
            {
                using var db = Connection;
                // Dapper will automatically open/close the connection if it isn't already open

                // ExecuteAsync returns the number of affected rows
                int result = await db.ExecuteAsync(
                    "sp_AutoFixPlanStartMonth",
                    new { EmpId = employerId, Year = year, Month = startMonth },
                    commandType: CommandType.StoredProcedure);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in AutoFixPlanStartMonthAsync for EmployerId: {EmployerId}", employerId);
                throw;
            }
        }
        /// <summary>
        /// Purpose: Retrieves all at-risk employees.
        /// Input parameters: int employerId, int year
        /// Output/return value: Task of List of AtRiskEmployeeDto
        /// </summary>
        public async Task<List<AtRiskEmployeeDto>> GetAtRiskEmployeesAsync(int employerId, int year)
        {
            try
            {
                using var db = Connection;
                var allRows = await db.QueryAsync<AtRiskEmployeeDto>(
                    "sp_GetAtRiskEmployees",
                    new { EmployerId = employerId, Year = year },
                    commandType: CommandType.StoredProcedure);

                var riskyEmployees = allRows.Where(x => x.BadMonthCount > 0).ToList();

                foreach (var emp in riskyEmployees)
                {
                    emp.EstimatedPenalty = emp.BadMonthCount * 360m;
                }

                return riskyEmployees.OrderByDescending(x => x.EstimatedPenalty).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetAtRiskEmployeesAsync for EmployerId: {EmployerId}", employerId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Generates dynamic flags from raw file logs.
        /// Input parameters: int fileLogId
        /// Output/return value: Task
        /// </summary>
        //public async Task GenerateFlagsAsync(int fileLogId)
        //{
        //    try
        //    {
        //        using var db = Connection;
        //        await db.ExecuteAsync("sp_RunDynamicFlagEngine",
        //            new { FileLogId = fileLogId },
        //            commandType: CommandType.StoredProcedure,
        //            commandTimeout: 300);
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Error occurred in GenerateFlagsAsync for FileLogId: {FileLogId}", fileLogId);
        //        throw;
        //    }
        //}

        /// <summary>
        /// Purpose: Retrieves the total portfolio penalty exposure risk for an account manager.
        /// Input parameters: string amUserId, int year
        /// Output/return value: Task of decimal
        /// </summary>
        public async Task<decimal> GetPortfolioRiskAsync(string amUserId, int year)
        {
            try
            {
                using var db = Connection;
                var result = await db.ExecuteScalarAsync<decimal?>(
                    "sp_GetPortfolioRisk",
                    new { UserId = amUserId, Year = year },
                    commandType: CommandType.StoredProcedure);
                return result ?? 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetPortfolioRiskAsync for UserId: {UserId}", amUserId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Applies the designated safe harbor code automatically to appropriate months.
        /// Input parameters: int employeeId, string filingYear, string safeHarborCode, string userId
        /// Output/return value: Task
        /// </summary>
        public async Task ApplySafeHarborAsync(int employeeId, string filingYear, string safeHarborCode, bool isLocked, string userId)
        {
            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                var record = await db.QuerySingleOrDefaultAsync<EmployeeCode>(
                    "sp_GetEmployeeCodeForUpdate",
                    new { EmpId = employeeId, Year = filingYear },
                    transaction: tx,
                    commandType: CommandType.StoredProcedure);

                if (record == null)
                {
                    tx.Rollback();
                    return;
                }

                string[] months = { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };

                foreach (var m in months)
                {
                    var coc = GetFieldValue(record, $"{m}_COC");
                    var shc = GetFieldValue(record, $"{m}_SHC");

                    if (coc == "1H" && string.IsNullOrWhiteSpace(shc))
                    {
                        await LogAuditAsync(new EmployeeCodeAuditDto
                        {
                            EmployeeId = employeeId,
                            FilingYear = filingYear,
                            IsLocked = isLocked,
                            MonthCode = m,
                            AppliesToAllMonths = false,

                            LineNumber = 16,
                            FieldName = $"{m}_SHC",

                            OldValue = null,
                            NewValue = safeHarborCode,

                            ChangeSource = "AUTOFIX",
                            ChangeReason = $"Affordability Safe Harbor {safeHarborCode} auto-applied",
                            SafeHarborApplied = safeHarborCode,
                            ChangedByUserId = userId,
                            IsSystemGenerated = true
                        });
                    }
                }

                await db.ExecuteAsync(
                    "sp_UpdateEmployeeCodeSafeHarbor",
                    new { EmpId = employeeId, Year = filingYear, Code = safeHarborCode , IsLocked = isLocked},
                    transaction: tx,
                    commandType: CommandType.StoredProcedure);

                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in ApplySafeHarborAsync for EmployeeId: {EmployeeId}", employeeId);
                throw;
            }
        }

        // ============================================================
        // SAFE HARBOR
        // ============================================================

        /// <summary>
        /// Purpose: Determines appropriate safe harbor code based on affordability tests.
        /// Input parameters: CalculationContext emp, FilingYearRules rules
        /// Output/return value: string
        /// </summary>
        private string DetermineSafeHarbor(CalculationContext emp, FilingYearRules rules)
        {
            try
            {
                if (!emp.LowestCostPremium.HasValue)
                    return "2F";

                decimal premium = emp.LowestCostPremium.Value;

                if (premium <= rules.FpgPremium)
                    return "2G";

                if (emp.HourlyRate.HasValue && emp.MonthlyHours.HasValue)
                {
                    decimal pay = emp.HourlyRate.Value * emp.MonthlyHours.Value;
                    if (premium <= pay * rules.FpgPercent)
                        return "2H";
                }

                return "2F";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in DetermineSafeHarbor.");
                throw;
            }
        }

        /// <summary>
        /// Purpose: Logs an audit record for employee code changes.
        /// Input parameters: EmployeeCodeAuditDto audit
        /// Output/return value: Task
        /// </summary>
        private async Task LogAuditAsync(EmployeeCodeAuditDto audit)
        {
            using var db = Connection;
            db.Open();
            using var tx = db.BeginTransaction();
            try
            {
                await db.ExecuteAsync(
                    "dbo.usp_InsertEmployeeCodeAudit",
                    new
                    {
                        audit.EmployeeId,
                        audit.FilingYear,

                        audit.MonthCode,
                        audit.AppliesToAllMonths,

                        audit.LineNumber,
                        audit.FieldName,

                        audit.OldValue,
                        audit.NewValue,

                        audit.ChangeSource,
                        audit.ChangeReason,
                        audit.SafeHarborApplied,

                        audit.ChangedByUserId,
                        audit.ChangedByUserName,

                        audit.IsSystemGenerated
                    },
                    transaction: tx,
                    commandType: CommandType.StoredProcedure
                );

                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Error occurred in LogAuditAsync.");
                throw;
            }
        }

        /// <summary>
        /// Purpose: Uses reflection to retrieve the property value of an object as a string.
        /// Input parameters: EmployeeCode obj, string fieldName
        /// Output/return value: string
        /// </summary>
        private static string GetFieldValue(EmployeeCode obj, string fieldName)
        {
            try
            {
                var prop = typeof(EmployeeCode).GetProperty(fieldName);
                var val = prop?.GetValue(obj);
                return val?.ToString() ?? string.Empty;
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Purpose: Retrieves the timeline of audit events for an employee.
        /// Input parameters: int employeeId, int year
        /// Output/return value: Task of List of EmployeeCodeAuditTimelineDto
        /// </summary>
        public async Task<List<EmployeeCodeAuditTimelineDto>> GetAuditTimelineAsync(int employeeId, int year)
        {
            try
            {
                using var db = Connection;
                var result = await db.QueryAsync<EmployeeCodeAuditTimelineDto>(
                    "dbo.usp_GetEmployeeCodeAuditTimeline",
                    new { EmployeeId = employeeId, FilingYear = year },
                    commandType: CommandType.StoredProcedure
                );

                return result.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetAuditTimelineAsync for EmployeeId: {EmployeeId}", employeeId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Gets the penalty risk calculated strictly for one employee.
        /// Input parameters: int employeeId, int year
        /// Output/return value: Task of AtRiskEmployeeDto
        /// </summary>
        public async Task<AtRiskEmployeeDto?> GetSingleEmployeeRiskAsync(int employeeId, int year)
        {
            try
            {
                var allRisks = await GetAtRiskEmployeesAsync(0, year);
                return allRisks.FirstOrDefault(x => x.EmployeeId == employeeId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetSingleEmployeeRiskAsync for EmployeeId: {EmployeeId}", employeeId);
                throw;
            }
        }

        // ============================================================
        // AFFORDABILITY EXTRACT
        // ============================================================

        /// <summary>
        /// Purpose: Builds the Affordability extract workbook. Every employee for
        /// the employer/year is sorted into one of three worksheets based on
        /// whether the cheapest self-only premium offered is affordable under the
        /// W-2 and/or Rate-of-Pay safe harbors for that filing year.
        /// Input parameters: int employerId, int year
        /// Output/return value: Task of byte[] (an .xlsx file)
        /// </summary>
        public async Task<byte[]> GenerateAffordabilityExportAsync(int employerId, int year)
        {
            try
            {
                List<AffordabilityExportRow> rows;
                using (var db = Connection)
                {
                    rows = (await db.QueryAsync<AffordabilityExportRow>(
                        "sp_AffordabilityExport",
                        new { EmployerId = employerId, Year = year },
                        commandType: CommandType.StoredProcedure)).ToList();
                }

                // FIX: Await the database lookup to get the exact percentage for the year
                var rules = await GetFilingYearRulesAsync(year);
                decimal pct = rules.FpgPercent;

                var affordable = new List<AffordabilityExportRow>();
                var w2Unaffordable = new List<AffordabilityExportRow>();
                var ropUnaffordable = new List<AffordabilityExportRow>();

                foreach (var r in rows)
                {
                    switch (ClassifyAffordability(r, pct))
                    {
                        case AffordabilityBucket.W2Unaffordable:
                            w2Unaffordable.Add(r);
                            break;
                        case AffordabilityBucket.RateOfPayUnaffordable:
                            ropUnaffordable.Add(r);
                            break;
                        default:
                            affordable.Add(r);
                            break;
                    }
                }

                using var package = new ExcelPackage();
                // Sheet order mirrors the reference workbook.
                WriteAffordabilitySheet(package, "W2 unaffordable", w2Unaffordable);
                WriteAffordabilitySheet(package, "Rate of Pay unaffordable", ropUnaffordable);
                WriteAffordabilitySheet(package, "Rate of Pay or W2 affordable", affordable);

                return package.GetAsByteArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GenerateAffordabilityExportAsync for EmployerId: {EmployerId}", employerId);
                throw;
            }
        }

        /// <summary>
        /// Purpose: Runs the W-2 and Rate-of-Pay affordability tests for one
        /// employee and decides which worksheet bucket they belong to.
        /// Input parameters: AffordabilityExportRow r, decimal pct
        /// Output/return value: AffordabilityBucket
        /// </summary>
        private static AffordabilityBucket ClassifyAffordability(AffordabilityExportRow r, decimal pct)
        {
            // No premium (no plan offered / no employee cost) cannot fail an
            // affordability test, so the employee is treated as affordable.
            if (!r.LowestCostPremium.HasValue || r.LowestCostPremium.Value <= 0m)
                return AffordabilityBucket.Affordable;

            decimal premium = r.LowestCostPremium.Value;

            bool w2Known = r.W2Wages.HasValue && r.W2Wages.Value > 0m;
            bool ropKnown = r.HourlyRate.HasValue && r.MonthlyHours.HasValue
                            && r.HourlyRate.Value > 0m && r.MonthlyHours.Value > 0m;

            // W-2 safe harbor: annual wages x pct, expressed monthly.
            bool w2Affordable = w2Known && premium <= (r.W2Wages.Value * pct) / 12m;
            // Rate-of-Pay safe harbor: (hourly rate x monthly hours) x pct.
            bool ropAffordable = ropKnown && premium <= (r.HourlyRate.Value * r.MonthlyHours.Value) * pct;

            if (w2Affordable || ropAffordable)
                return AffordabilityBucket.Affordable;

            if (w2Known)
                return AffordabilityBucket.W2Unaffordable;

            if (ropKnown)
                return AffordabilityBucket.RateOfPayUnaffordable;

            // No wage data to judge affordability against; default to affordable.
            return AffordabilityBucket.Affordable;
        }

        /// <summary>
        /// Purpose: Writes a single Affordability worksheet with the standard
        /// SSN / First Name / Last Name / Tax ID / Employer columns.
        /// Input parameters: ExcelPackage package, string sheetName, List rows
        /// Output/return value: void
        /// </summary>
        private static void WriteAffordabilitySheet(ExcelPackage package, string sheetName, List<AffordabilityExportRow> rows)
        {
            var sheet = package.Workbook.Worksheets.Add(sheetName);

            string[] headers = { "SSN", "First Name", "Last Name", "Tax ID", "Employer" };
            for (int c = 0; c < headers.Length; c++)
            {
                var cell = sheet.Cells[1, c + 1];
                cell.Value = headers[c];
                cell.Style.Font.Bold = true;
            }

            int row = 2;
            foreach (var r in rows)
            {
                sheet.Cells[row, 1].Value = r.SSN;
                sheet.Cells[row, 2].Value = r.FirstName;
                sheet.Cells[row, 3].Value = r.LastName;
                sheet.Cells[row, 4].Value = r.TaxId;
                sheet.Cells[row, 5].Value = r.Employer;
                row++;
            }

            sheet.Cells[sheet.Dimension?.Address ?? "A1"].AutoFitColumns();
        }

        // ============================================================
        // CONSTANTS
        // ============================================================
    }
}