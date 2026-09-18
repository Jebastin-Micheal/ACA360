using ACA360.Core.Models;
using ACA360.Data.Helpers;
using ACA360.Logging.Interfaces;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using ACA360.BusinessLogic.Interfaces;

namespace ACA360.BusinessLogic.Services
{
    public class ReportService : IReportService
    {
        private readonly string _connectionString;
        private readonly ILoggerService _logger;

        public ReportService(string connectionString, ILoggerService logger)
        {
            _connectionString = connectionString;
            _logger = logger;
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        public async Task<List<Employer_ReportModel>> GetEmployerReport(string reportType)
        {
            try
            {
                using var db = Connection;

                // Dapper automatically maps all the properties (FilingYear, Minimum_0, Group_12, etc.)
                // as long as the SQL column names match the C# property names (case-insensitive).
                var reports = await db.QueryAsync<Employer_ReportModel>(
                    "usp_GetEmployer_Report",
                    new { Report_Type = reportType },
                    commandType: CommandType.StoredProcedure);

                return reports.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "GetEmployerReport",
                    "Service",
                    "Failed to fetch the Employer report",
                    "Server or DB"
                );
                throw;
            }
        }

        #region  ExportEFile_Report      
        private string GetSafeString(DataRow row, string columnName, string defaultValue = "")
        {
            // 1. Check if Column Exists
            if (!row.Table.Columns.Contains(columnName))
            {
                return defaultValue;
            }

            // 2. Check if Value is DBNull
            if (row[columnName] == DBNull.Value)
            {
                return defaultValue;
            }

            // 3. Return trimmed string
            return row[columnName].ToString().Trim();
        }

        public AcaReportViewModel GetAcaData(string taxId, string ssn, string year)
        {
            var model = new AcaReportViewModel
            {
                SearchTaxId = taxId,
                SearchSsn = ssn,
                SearchYear = year,
                Employees = new List<Employee1095Data>()
            };

            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    connection.Open();

                    using (var multi = connection.QueryMultiple(
                        "dbo.ExportEFileData",
                        new
                        {
                            EmployerTaxId = string.IsNullOrEmpty(taxId) ? DBNull.Value : (object)taxId,
                            SSN = string.IsNullOrEmpty(ssn) ? DBNull.Value : (object)ssn,
                            FilingYear = string.IsNullOrEmpty(year) ? DBNull.Value : (object)year
                        },
                        commandType: CommandType.StoredProcedure))
                    {
                        // Read multiple result sets
                        var employerTable = multi.Read<dynamic>().ToList();
                        var employeeTable = multi.Read<dynamic>().ToList();

                        // Map Employer Data (first row)
                        if (employerTable.Any())
                        {
                            var row = employerTable.First();
                            model.Employer = MapEmployerData(row);
                        }

                        // Map Employee Data
                        foreach (var row in employeeTable)
                        {
                            var emp = MapEmployeeData(row);
                            model.Employees.Add(emp);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                model.ErrorMessage = "DB Error: " + ex.Message;
            }

            return model;
        }

        private Employer1094Data MapEmployerData(dynamic row)
        {
            return new Employer1094Data
            {
                // 1. Basic Employer Info
                EmployerEIN = GetDynamicString(row, "EmployerEIN"),
                BusinessNameLine1Txt = GetDynamicString(row, "BusinessNameLine1Txt"),
                BusinessNameLine2Txt = GetDynamicString(row, "BusinessNameLine2Txt"),
                PersonFirstNm = GetDynamicString(row, "PersonFirstNm"),
                PersonMiddleNm = GetDynamicString(row, "PersonMiddleNm"),
                PersonLastNm = GetDynamicString(row, "PersonLastNm"),
                SuffixNm = GetDynamicString(row, "SuffixNm"),
                ContactPhoneNum = GetDynamicString(row, "ContactPhoneNum"),
                ForeignAddressInd = GetDynamicString(row, "ForeignAddressInd"),
                AddressLine1Txt = GetDynamicString(row, "AddressLine1Txt"),
                AddressLine2Txt = GetDynamicString(row, "AddressLine2Txt"),
                CityNm = GetDynamicString(row, "CityNm"),
                USStateCd = GetDynamicString(row, "USStateCd"),
                USZIPCd = GetDynamicString(row, "USZIPCd"),
                USZIPExtensionCd = GetDynamicString(row, "USZIPExtensionCd"),

                // 2. Foreign Address Info
                ForeignAddressLine1Txt = GetDynamicString(row, "ForeignAddressLine1Txt"),
                ForeignAddressLine2Txt = GetDynamicString(row, "ForeignAddressLine2Txt"),
                ForeignCityNm = GetDynamicString(row, "ForeignCityNm"),
                ForeignProvinceNm = GetDynamicString(row, "ForeignProvinceNm"),
                ForeignPostalCd = GetDynamicString(row, "ForeignPostalCd"),
                ForeignCountryCd = GetDynamicString(row, "ForeignCountryCd"),
                CorrectedInd = GetDynamicString(row, "CorrectedInd"),

                // 3. GovEntity Columns
                GovEntity_BusinessNameLine1Txt = GetDynamicString(row, "GovEntity_BusinessNameLine1Txt"),
                GovEntity_BusinessNameLine2Txt = GetDynamicString(row, "GovEntity_BusinessNameLine2Txt"),
                GovEntity_EmployerEIN = GetDynamicString(row, "GovEntity_EmployerEIN"),
                GovEntity_ForeignAddressInd = GetDynamicString(row, "GovEntity_ForeignAddressInd"),
                GovEntity_AddressLine1Txt = GetDynamicString(row, "GovEntity_AddressLine1Txt"),
                GovEntity_AddressLine2Txt = GetDynamicString(row, "GovEntity_AddressLine2Txt"),
                GovEntity_CityNm = GetDynamicString(row, "GovEntity_CityNm"),
                GovEntity_USStateCd = GetDynamicString(row, "GovEntity_USStateCd"),
                GovEntity_USZIPCd = GetDynamicString(row, "GovEntity_USZIPCd"),
                GovEntity_USZIPExtensionCd = GetDynamicString(row, "GovEntity_USZIPExtensionCd"),
                GovEntity_ForeignAddressLine1Txt = GetDynamicString(row, "GovEntity_ForeignAddressLine1Txt"),
                GovEntity_ForeignAddressLine2Txt = GetDynamicString(row, "GovEntity_ForeignAddressLine2Txt"),
                GovEntity_ForeignCityNm = GetDynamicString(row, "GovEntity_ForeignCityNm"),
                GovEntity_ForeignProvinceNm = GetDynamicString(row, "GovEntity_ForeignProvinceNm"),
                GovEntity_ForeignPostalCd = GetDynamicString(row, "GovEntity_ForeignPostalCd"),
                GovEntity_ForeignCountryCd = GetDynamicString(row, "GovEntity_ForeignCountryCd"),
                GovEntity_PersonFirstNm = GetDynamicString(row, "GovEntity_PersonFirstNm"),
                GovEntity_PersonMiddleNm = GetDynamicString(row, "GovEntity_PersonMiddleNm"),
                GovEntity_PersonLastNm = GetDynamicString(row, "GovEntity_PersonLastNm"),
                GovEntity_SuffixNm = GetDynamicString(row, "GovEntity_SuffixNm"),
                GovEntity_ContactPhoneNum = GetDynamicString(row, "GovEntity_ContactPhoneNum"),

                // 4. Form Status Indicators
                AuthoritativeTransmittalInd = GetDynamicString(row, "AuthoritativeTransmittalInd"),
                TotalForm1095CALEMemberCnt = GetDynamicString(row, "TotalForm1095CALEMemberCnt"),
                AggregatedGroupMemberInd = GetDynamicString(row, "AggregatedGroupMemberInd"),
                QualifyingOfferMethodInd = GetDynamicString(row, "QualifyingOfferMethodInd"),
                Section4980HReliefInd = GetDynamicString(row, "Section4980HReliefInd"),
                NinetyEightPctOfferMethodInd = GetDynamicString(row, "NinetyEightPctOfferMethodInd"),
                PersonTitleTxt = GetDynamicString(row, "PersonTitleTxt"),
                SignatureDt = GetDynamicString(row, "SignatureDt"),

                // 5. Monthly Data (Yearly)
                Yearly_MinEssentialCvrOffrInd = GetDynamicString(row, "Yearly_MinEssentialCvrOffrInd"),
                Yearly_ALEMemberFTECnt = GetDynamicString(row, "Yearly_ALEMemberFTECnt"),
                Yearly_TotalEmployeeCnt = GetDynamicString(row, "Yearly_TotalEmployeeCnt"),
                Yearly_AggregatedGroupInd = GetDynamicString(row, "Yearly_AggregatedGroupInd"),
                Yearly_ALESect4980HTrnstReliefCd = GetDynamicString(row, "Yearly_ALESect4980HTrnstReliefCd"),

                // Jan
                Jan_MinEssentialCvrOffrInd = GetDynamicString(row, "Jan_MinEssentialCvrOffrInd"),
                Jan_ALEMemberFTECnt = GetDynamicString(row, "Jan_ALEMemberFTECnt"),
                Jan_TotalEmployeeCnt = GetDynamicString(row, "Jan_TotalEmployeeCnt"),
                Jan_AggregatedGroupInd = GetDynamicString(row, "Jan_AggregatedGroupInd"),
                Jan_ALESect4980HTrnstReliefCd = GetDynamicString(row, "Jan_ALESect4980HTrnstReliefCd"),

                // Feb
                Feb_MinEssentialCvrOffrInd = GetDynamicString(row, "Feb_MinEssentialCvrOffrInd"),
                Feb_ALEMemberFTECnt = GetDynamicString(row, "Feb_ALEMemberFTECnt"),
                Feb_TotalEmployeeCnt = GetDynamicString(row, "Feb_TotalEmployeeCnt"),
                Feb_AggregatedGroupInd = GetDynamicString(row, "Feb_AggregatedGroupInd"),
                Feb_ALESect4980HTrnstReliefCd = GetDynamicString(row, "Feb_ALESect4980HTrnstReliefCd"),

                // Mar
                Mar_MinEssentialCvrOffrInd = GetDynamicString(row, "Mar_MinEssentialCvrOffrInd"),
                Mar_ALEMemberFTECnt = GetDynamicString(row, "Mar_ALEMemberFTECnt"),
                Mar_TotalEmployeeCnt = GetDynamicString(row, "Mar_TotalEmployeeCnt"),
                Mar_AggregatedGroupInd = GetDynamicString(row, "Mar_AggregatedGroupInd"),
                Mar_ALESect4980HTrnstReliefCd = GetDynamicString(row, "Mar_ALESect4980HTrnstReliefCd"),

                // Apr
                Apr_MinEssentialCvrOffrInd = GetDynamicString(row, "Apr_MinEssentialCvrOffrInd"),
                Apr_ALEMemberFTECnt = GetDynamicString(row, "Apr_ALEMemberFTECnt"),
                Apr_TotalEmployeeCnt = GetDynamicString(row, "Apr_TotalEmployeeCnt"),
                Apr_AggregatedGroupInd = GetDynamicString(row, "Apr_AggregatedGroupInd"),
                Apr_ALESect4980HTrnstReliefCd = GetDynamicString(row, "Apr_ALESect4980HTrnstReliefCd"),

                // May
                May_MinEssentialCvrOffrInd = GetDynamicString(row, "May_MinEssentialCvrOffrInd"),
                May_ALEMemberFTECnt = GetDynamicString(row, "May_ALEMemberFTECnt"),
                May_TotalEmployeeCnt = GetDynamicString(row, "May_TotalEmployeeCnt"),
                May_AggregatedGroupInd = GetDynamicString(row, "May_AggregatedGroupInd"),
                May_ALESect4980HTrnstReliefCd = GetDynamicString(row, "May_ALESect4980HTrnstReliefCd"),

                // Jun
                Jun_MinEssentialCvrOffrInd = GetDynamicString(row, "Jun_MinEssentialCvrOffrInd"),
                Jun_ALEMemberFTECnt = GetDynamicString(row, "Jun_ALEMemberFTECnt"),
                Jun_TotalEmployeeCnt = GetDynamicString(row, "Jun_TotalEmployeeCnt"),
                Jun_AggregatedGroupInd = GetDynamicString(row, "Jun_AggregatedGroupInd"),
                Jun_ALESect4980HTrnstReliefCd = GetDynamicString(row, "Jun_ALESect4980HTrnstReliefCd"),

                // Jul
                Jul_MinEssentialCvrOffrInd = GetDynamicString(row, "Jul_MinEssentialCvrOffrInd"),
                Jul_ALEMemberFTECnt = GetDynamicString(row, "Jul_ALEMemberFTECnt"),
                Jul_TotalEmployeeCnt = GetDynamicString(row, "Jul_TotalEmployeeCnt"),
                Jul_AggregatedGroupInd = GetDynamicString(row, "Jul_AggregatedGroupInd"),
                Jul_ALESect4980HTrnstReliefCd = GetDynamicString(row, "Jul_ALESect4980HTrnstReliefCd"),

                // Aug
                Aug_MinEssentialCvrOffrInd = GetDynamicString(row, "Aug_MinEssentialCvrOffrInd"),
                Aug_ALEMemberFTECnt = GetDynamicString(row, "Aug_ALEMemberFTECnt"),
                Aug_TotalEmployeeCnt = GetDynamicString(row, "Aug_TotalEmployeeCnt"),
                Aug_AggregatedGroupInd = GetDynamicString(row, "Aug_AggregatedGroupInd"),
                Aug_ALESect4980HTrnstReliefCd = GetDynamicString(row, "Aug_ALESect4980HTrnstReliefCd"),

                // Sep (Note: using Sept_ prefix)
                Sep_MinEssentialCvrOffrInd = GetDynamicString(row, "Sept_MinEssentialCvrOffrInd"),
                Sep_ALEMemberFTECnt = GetDynamicString(row, "Sept_ALEMemberFTECnt"),
                Sep_TotalEmployeeCnt = GetDynamicString(row, "Sept_TotalEmployeeCnt"),
                Sep_AggregatedGroupInd = GetDynamicString(row, "Sept_AggregatedGroupInd"),
                Sep_ALESect4980HTrnstReliefCd = GetDynamicString(row, "Sept_ALESect4980HTrnstReliefCd"),

                // Oct
                Oct_MinEssentialCvrOffrInd = GetDynamicString(row, "Oct_MinEssentialCvrOffrInd"),
                Oct_ALEMemberFTECnt = GetDynamicString(row, "Oct_ALEMemberFTECnt"),
                Oct_TotalEmployeeCnt = GetDynamicString(row, "Oct_TotalEmployeeCnt"),
                Oct_AggregatedGroupInd = GetDynamicString(row, "Oct_AggregatedGroupInd"),
                Oct_ALESect4980HTrnstReliefCd = GetDynamicString(row, "Oct_ALESect4980HTrnstReliefCd"),

                // Nov
                Nov_MinEssentialCvrOffrInd = GetDynamicString(row, "Nov_MinEssentialCvrOffrInd"),
                Nov_ALEMemberFTECnt = GetDynamicString(row, "Nov_ALEMemberFTECnt"),
                Nov_TotalEmployeeCnt = GetDynamicString(row, "Nov_TotalEmployeeCnt"),
                Nov_AggregatedGroupInd = GetDynamicString(row, "Nov_AggregatedGroupInd"),
                Nov_ALESect4980HTrnstReliefCd = GetDynamicString(row, "Nov_ALESect4980HTrnstReliefCd"),

                // Dec
                Dec_MinEssentialCvrOffrInd = GetDynamicString(row, "Dec_MinEssentialCvrOffrInd"),
                Dec_ALEMemberFTECnt = GetDynamicString(row, "Dec_ALEMemberFTECnt"),
                Dec_TotalEmployeeCnt = GetDynamicString(row, "Dec_TotalEmployeeCnt"),
                Dec_AggregatedGroupInd = GetDynamicString(row, "Dec_AggregatedGroupInd"),
                Dec_ALESect4980HTrnstReliefCd = GetDynamicString(row, "Dec_ALESect4980HTrnstReliefCd"),

                // 6. Other ALE Members (1-30)
                BusinessNameLine1Txt_01 = GetDynamicString(row, "BusinessNameLine1Txt_01"),
                BusinessNameLine2Txt_01 = GetDynamicString(row, "BusinessNameLine2Txt_01"),
                EIN_01 = GetDynamicString(row, "EIN_01"),
                BusinessNameLine1Txt_02 = GetDynamicString(row, "BusinessNameLine1Txt_02"),
                BusinessNameLine2Txt_02 = GetDynamicString(row, "BusinessNameLine2Txt_02"),
                EIN_02 = GetDynamicString(row, "EIN_02"),
                BusinessNameLine1Txt_03 = GetDynamicString(row, "BusinessNameLine1Txt_03"),
                BusinessNameLine2Txt_03 = GetDynamicString(row, "BusinessNameLine2Txt_03"),
                EIN_03 = GetDynamicString(row, "EIN_03"),
                BusinessNameLine1Txt_04 = GetDynamicString(row, "BusinessNameLine1Txt_04"),
                BusinessNameLine2Txt_04 = GetDynamicString(row, "BusinessNameLine2Txt_04"),
                EIN_04 = GetDynamicString(row, "EIN_04"),
                BusinessNameLine1Txt_05 = GetDynamicString(row, "BusinessNameLine1Txt_05"),
                BusinessNameLine2Txt_05 = GetDynamicString(row, "BusinessNameLine2Txt_05"),
                EIN_05 = GetDynamicString(row, "EIN_05"),
                BusinessNameLine1Txt_06 = GetDynamicString(row, "BusinessNameLine1Txt_06"),
                BusinessNameLine2Txt_06 = GetDynamicString(row, "BusinessNameLine2Txt_06"),
                EIN_06 = GetDynamicString(row, "EIN_06"),
                BusinessNameLine1Txt_07 = GetDynamicString(row, "BusinessNameLine1Txt_07"),
                BusinessNameLine2Txt_07 = GetDynamicString(row, "BusinessNameLine2Txt_07"),
                EIN_07 = GetDynamicString(row, "EIN_07"),
                BusinessNameLine1Txt_08 = GetDynamicString(row, "BusinessNameLine1Txt_08"),
                BusinessNameLine2Txt_08 = GetDynamicString(row, "BusinessNameLine2Txt_08"),
                EIN_08 = GetDynamicString(row, "EIN_08"),
                BusinessNameLine1Txt_09 = GetDynamicString(row, "BusinessNameLine1Txt_09"),
                BusinessNameLine2Txt_09 = GetDynamicString(row, "BusinessNameLine2Txt_09"),
                EIN_09 = GetDynamicString(row, "EIN_09"),
                BusinessNameLine1Txt_10 = GetDynamicString(row, "BusinessNameLine1Txt_10"),
                BusinessNameLine2Txt_10 = GetDynamicString(row, "BusinessNameLine2Txt_10"),
                EIN_10 = GetDynamicString(row, "EIN_10"),
                BusinessNameLine1Txt_11 = GetDynamicString(row, "BusinessNameLine1Txt_11"),
                BusinessNameLine2Txt_11 = GetDynamicString(row, "BusinessNameLine2Txt_11"),
                EIN_11 = GetDynamicString(row, "EIN_11"),
                BusinessNameLine1Txt_12 = GetDynamicString(row, "BusinessNameLine1Txt_12"),
                BusinessNameLine2Txt_12 = GetDynamicString(row, "BusinessNameLine2Txt_12"),
                EIN_12 = GetDynamicString(row, "EIN_12"),
                BusinessNameLine1Txt_13 = GetDynamicString(row, "BusinessNameLine1Txt_13"),
                BusinessNameLine2Txt_13 = GetDynamicString(row, "BusinessNameLine2Txt_13"),
                EIN_13 = GetDynamicString(row, "EIN_13"),
                BusinessNameLine1Txt_14 = GetDynamicString(row, "BusinessNameLine1Txt_14"),
                BusinessNameLine2Txt_14 = GetDynamicString(row, "BusinessNameLine2Txt_14"),
                EIN_14 = GetDynamicString(row, "EIN_14"),
                BusinessNameLine1Txt_15 = GetDynamicString(row, "BusinessNameLine1Txt_15"),
                BusinessNameLine2Txt_15 = GetDynamicString(row, "BusinessNameLine2Txt_15"),
                EIN_15 = GetDynamicString(row, "EIN_15"),
                BusinessNameLine1Txt_16 = GetDynamicString(row, "BusinessNameLine1Txt_16"),
                BusinessNameLine2Txt_16 = GetDynamicString(row, "BusinessNameLine2Txt_16"),
                EIN_16 = GetDynamicString(row, "EIN_16"),
                BusinessNameLine1Txt_17 = GetDynamicString(row, "BusinessNameLine1Txt_17"),
                BusinessNameLine2Txt_17 = GetDynamicString(row, "BusinessNameLine2Txt_17"),
                EIN_17 = GetDynamicString(row, "EIN_17"),
                BusinessNameLine1Txt_18 = GetDynamicString(row, "BusinessNameLine1Txt_18"),
                BusinessNameLine2Txt_18 = GetDynamicString(row, "BusinessNameLine2Txt_18"),
                EIN_18 = GetDynamicString(row, "EIN_18"),
                BusinessNameLine1Txt_19 = GetDynamicString(row, "BusinessNameLine1Txt_19"),
                BusinessNameLine2Txt_19 = GetDynamicString(row, "BusinessNameLine2Txt_19"),
                EIN_19 = GetDynamicString(row, "EIN_19"),
                BusinessNameLine1Txt_20 = GetDynamicString(row, "BusinessNameLine1Txt_20"),
                BusinessNameLine2Txt_20 = GetDynamicString(row, "BusinessNameLine2Txt_20"),
                EIN_20 = GetDynamicString(row, "EIN_20"),
                BusinessNameLine1Txt_21 = GetDynamicString(row, "BusinessNameLine1Txt_21"),
                BusinessNameLine2Txt_21 = GetDynamicString(row, "BusinessNameLine2Txt_21"),
                EIN_21 = GetDynamicString(row, "EIN_21"),
                BusinessNameLine1Txt_22 = GetDynamicString(row, "BusinessNameLine1Txt_22"),
                BusinessNameLine2Txt_22 = GetDynamicString(row, "BusinessNameLine2Txt_22"),
                EIN_22 = GetDynamicString(row, "EIN_22"),
                BusinessNameLine1Txt_23 = GetDynamicString(row, "BusinessNameLine1Txt_23"),
                BusinessNameLine2Txt_23 = GetDynamicString(row, "BusinessNameLine2Txt_23"),
                EIN_23 = GetDynamicString(row, "EIN_23"),
                BusinessNameLine1Txt_24 = GetDynamicString(row, "BusinessNameLine1Txt_24"),
                BusinessNameLine2Txt_24 = GetDynamicString(row, "BusinessNameLine2Txt_24"),
                EIN_24 = GetDynamicString(row, "EIN_24"),
                BusinessNameLine1Txt_25 = GetDynamicString(row, "BusinessNameLine1Txt_25"),
                BusinessNameLine2Txt_25 = GetDynamicString(row, "BusinessNameLine2Txt_25"),
                EIN_25 = GetDynamicString(row, "EIN_25"),
                BusinessNameLine1Txt_26 = GetDynamicString(row, "BusinessNameLine1Txt_26"),
                BusinessNameLine2Txt_26 = GetDynamicString(row, "BusinessNameLine2Txt_26"),
                EIN_26 = GetDynamicString(row, "EIN_26"),
                BusinessNameLine1Txt_27 = GetDynamicString(row, "BusinessNameLine1Txt_27"),
                BusinessNameLine2Txt_27 = GetDynamicString(row, "BusinessNameLine2Txt_27"),
                EIN_27 = GetDynamicString(row, "EIN_27"),
                BusinessNameLine1Txt_28 = GetDynamicString(row, "BusinessNameLine1Txt_28"),
                BusinessNameLine2Txt_28 = GetDynamicString(row, "BusinessNameLine2Txt_28"),
                EIN_28 = GetDynamicString(row, "EIN_28"),
                BusinessNameLine1Txt_29 = GetDynamicString(row, "BusinessNameLine1Txt_29"),
                BusinessNameLine2Txt_29 = GetDynamicString(row, "BusinessNameLine2Txt_29"),
                EIN_29 = GetDynamicString(row, "EIN_29"),
                BusinessNameLine1Txt_30 = GetDynamicString(row, "BusinessNameLine1Txt_30"),
                BusinessNameLine2Txt_30 = GetDynamicString(row, "BusinessNameLine2Txt_30"),
                EIN_30 = GetDynamicString(row, "EIN_30")
            };
        }

        private Employee1095Data MapEmployeeData(dynamic row)
        {
            return new Employee1095Data
            {
                // Identity
                Employer_EIN = GetDynamicString(row, "Employer_EIN"),
                Employee_SSN = GetDynamicString(row, "Employee_SSN"),
                Member_SSN = GetDynamicString(row, "Member_SSN"),
                First_Name = GetDynamicString(row, "First Name") ?? GetDynamicString(row, "First_Name"),
                Middle_Name = GetDynamicString(row, "Middle Name") ?? GetDynamicString(row, "Middle_Name"),
                Last_Name = GetDynamicString(row, "Last Name") ?? GetDynamicString(row, "Last_Name"),
                Suffix = GetDynamicString(row, "Suffix"),
                CorrectedInd = GetDynamicString(row, "CorrectedInd"),

                // Address Info
                Employee_ForeignAddressInd = GetDynamicString(row, "Employee_ForeignAddressInd"),
                Employee_AddressLine1Txt = GetDynamicString(row, "Employee_AddressLine1Txt"),
                Employee_AddressLine2Txt = GetDynamicString(row, "Employee_AddressLine2Txt"),
                Employee_CityNm = GetDynamicString(row, "Employee_CityNm"),
                Employee_USStateCd = GetDynamicString(row, "Employee_USStateCd"),
                Employee_USZIPCd = GetDynamicString(row, "Employee_USZIPCd"),
                Employee_USZIPExtensionCd = GetDynamicString(row, "Employee_USZIPExtensionCd"),
                Employee_ForeignAddressLine1Txt = GetDynamicString(row, "Employee_ForeignAddressLine1Txt"),
                Employee_ForeignAddressLine2Txt = GetDynamicString(row, "Employee_ForeignAddressLine2Txt"),
                Employee_ForeignCityNm = GetDynamicString(row, "Employee_ForeignCityNm"),
                Employee_ForeignProvinceNm = GetDynamicString(row, "Employee_ForeignProvinceNm"),
                Employee_ForeignPostalCd = GetDynamicString(row, "Employee_ForeignPostalCd"),
                Employee_ForeignCountryCd = GetDynamicString(row, "Employee_ForeignCountryCd"),

                // Plan Info
                PlanStartMonth = GetDynamicString(row, "PlanStartMonth"),

                // Annual Data
                AnnualOfferOfCoverageCd = GetDynamicString(row, "AnnualOfferOfCoverageCd"),
                AnnualShrLowestCostMthlyPremAmt = GetDynamicString(row, "AnnualShrLowestCostMthlyPremAmt"),
                AnnualSafeHarborCd = GetDynamicString(row, "AnnualSafeHarborCd"),

                // Monthly Data (Offer Codes, Amounts, Safe Harbor Codes)
                JanOfferCd = GetDynamicString(row, "JanOfferCd"),
                JanuaryAmt = GetDynamicString(row, "JanuaryAmt"),
                JanSafeHarborCd = GetDynamicString(row, "JanSafeHarborCd"),

                FebOfferCd = GetDynamicString(row, "FebOfferCd"),
                FebruaryAmt = GetDynamicString(row, "FebruaryAmt"),
                FebSafeHarborCd = GetDynamicString(row, "FebSafeHarborCd"),

                MarOfferCd = GetDynamicString(row, "MarOfferCd"),
                MarchAmt = GetDynamicString(row, "MarchAmt"),
                MarSafeHarborCd = GetDynamicString(row, "MarSafeHarborCd"),

                AprOfferCd = GetDynamicString(row, "AprOfferCd"),
                AprilAmt = GetDynamicString(row, "AprilAmt"),
                AprSafeHarborCd = GetDynamicString(row, "AprSafeHarborCd"),

                MayOfferCd = GetDynamicString(row, "MayOfferCd"),
                MayAmt = GetDynamicString(row, "MayAmt"),
                MaySafeHarborCd = GetDynamicString(row, "MaySafeHarborCd"),

                JunOfferCd = GetDynamicString(row, "JunOfferCd"),
                JuneAmt = GetDynamicString(row, "JuneAmt"),
                JuneSafeHarborCd = GetDynamicString(row, "JuneSafeHarborCd"),

                JulOfferCd = GetDynamicString(row, "JulOfferCd"),
                JulyAmt = GetDynamicString(row, "JulyAmt"),
                JulSafeHarborCd = GetDynamicString(row, "JulSafeHarborCd"),

                AugOfferCd = GetDynamicString(row, "AugOfferCd"),
                AugustAmt = GetDynamicString(row, "AugustAmt"),
                AugSafeHarborCd = GetDynamicString(row, "AugSafeHarborCd"),

                SepOfferCd = GetDynamicString(row, "SepOfferCd"),
                SeptemberAmt = GetDynamicString(row, "SeptemberAmt"),
                SepSafeHarborCd = GetDynamicString(row, "SepSafeHarborCd"),

                OctOfferCd = GetDynamicString(row, "OctOfferCd"),
                OctoberAmt = GetDynamicString(row, "OctoberAmt"),
                OctSafeHarborCd = GetDynamicString(row, "OctSafeHarborCd"),

                NovOfferCd = GetDynamicString(row, "NovOfferCd"),
                NovemberAmt = GetDynamicString(row, "NovemberAmt"),
                NovSafeHarborCd = GetDynamicString(row, "NovSafeHarborCd"),

                DecOfferCd = GetDynamicString(row, "DecOfferCd"),
                DecemberAmt = GetDynamicString(row, "DecemberAmt"),
                DecSafeHarborCd = GetDynamicString(row, "DecSafeHarborCd"),

                // Dependent / Covered Individuals
                CoveredIndividualInd = GetDynamicString(row, "CoveredIndividualInd"),
                Dependent_BirthDt = GetDynamicString(row, "Dependent_BirthDt"),
                Dependent_CoveredIndividualAnnualInd = GetDynamicString(row, "Dependent_CoveredIndividualAnnualInd"),

                Dependent_JanuaryInd = GetDynamicString(row, "Dependent_JanuaryInd"),
                Dependent_FebruaryInd = GetDynamicString(row, "Dependent_FebruaryInd"),
                Dependent_MarchInd = GetDynamicString(row, "Dependent_MarchInd"),
                Dependent_AprilInd = GetDynamicString(row, "Dependent_AprilInd"),
                Dependent_MayInd = GetDynamicString(row, "Dependent_MayInd"),
                Dependent_JuneInd = GetDynamicString(row, "Dependent_JuneInd"),
                Dependent_JulyInd = GetDynamicString(row, "Dependent_JulyInd"),
                Dependent_AugustInd = GetDynamicString(row, "Dependent_AugustInd"),
                Dependent_SeptemberInd = GetDynamicString(row, "Dependent_SeptemberInd"),
                Dependent_OctoberInd = GetDynamicString(row, "Dependent_OctoberInd"),
                Dependent_NovemberInd = GetDynamicString(row, "Dependent_NovemberInd"),
                Dependent_DecemberInd = GetDynamicString(row, "Dependent_DecemberInd"),

                // Parent Info (Note the spaces in the Stored Procedure column names)
                Parent_First_Name = GetDynamicString(row, "Parent First Name") ?? GetDynamicString(row, "Parent_First_Name"),
                Parent_Middle_Name = GetDynamicString(row, "Parent Middle Name") ?? GetDynamicString(row, "Parent_Middle_Name"),
                Parent_Last_Name = GetDynamicString(row, "Parent Last Name") ?? GetDynamicString(row, "Parent_Last_Name"),
                Parent_Suffix = GetDynamicString(row, "Parent Suffix") ?? GetDynamicString(row, "Parent_Suffix"),
                Parent_Birth_Date = GetDynamicString(row, "Parent Birth Date") ?? GetDynamicString(row, "Parent_Birth_Date")
            };
        }

        // Helper method to safely get string values from dynamic objects

        #endregion

        #region ExportMailFulfillment_Report
        public async Task<ExportMailFulfillmentViewModel> GetExportMailFulfillmentData(string employerTaxId, string filingYear, string ssn = null)
        {
            var result = new ExportMailFulfillmentViewModel
            {
                Employers = new List<MailFulfillment_EmployerModel>(),
                Employees = new List<MailFulfillment_EmployeeModel>()
            };

            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    using (var multi = await connection.QueryMultipleAsync(
                        "ExportMailFulfillment",
                        new
                        {
                            EmployerTaxId = string.IsNullOrEmpty(employerTaxId) ? DBNull.Value : (object)employerTaxId,
                            SSN = string.IsNullOrEmpty(ssn) ? DBNull.Value : (object)ssn,
                            FilingYear = string.IsNullOrEmpty(filingYear) ? DBNull.Value : (object)filingYear
                        },
                        commandType: CommandType.StoredProcedure))
                    {
                        // 1. Employer Data - Dapper can map directly
                        result.Employers = (await multi.ReadAsync<MailFulfillment_EmployerModel>()).ToList();

                        // 2. Employee Data - Need custom mapping for address logic
                        var employeeRows = await multi.ReadAsync<dynamic>();

                        foreach (var row in employeeRows)
                        {
                            var employee = new MailFulfillment_EmployeeModel
                            {
                                Employer_EIN = GetDynamicString(row, "Employer_EIN"),
                                Employee_SSN = GetDynamicString(row, "Employee_SSN"),
                                First_Name = GetDynamicString(row, "First Name") ?? GetDynamicString(row, "First_Name"),
                                Last_Name = GetDynamicString(row, "Last Name") ?? GetDynamicString(row, "Last_Name"),
                                Mail_Memo = GetDynamicString(row, "Mail_Memo"),

                                // Address Logic with fallback
                                AddressLine1Txt = GetDynamicString(row, "Employee_AddressLine1Txt") ?? GetDynamicString(row, "AddressLine1Txt"),
                                AddressLine2Txt = GetDynamicString(row, "Employee_AddressLine2Txt") ?? GetDynamicString(row, "AddressLine2Txt"),
                                CityNm = GetDynamicString(row, "Employee_CityNm") ?? GetDynamicString(row, "CityNm"),
                                USStateCd = GetDynamicString(row, "Employee_USStateCd") ?? GetDynamicString(row, "USStateCd"),
                                USZIPCd = GetDynamicString(row, "Employee_USZIPCd") ?? GetDynamicString(row, "USZIPCd"),

                                PlanStartMonth = GetDynamicString(row, "PlanStartMonth"),
                                CorrectedInd = GetDynamicString(row, "CorrectedInd"),

                                // Offers
                                AnnualOfferOfCoverageCd = GetDynamicString(row, "AnnualOfferOfCoverageCd"),
                                JanOfferCd = GetDynamicString(row, "JanOfferCd"),
                                FebOfferCd = GetDynamicString(row, "FebOfferCd"),
                                MarOfferCd = GetDynamicString(row, "MarOfferCd"),
                                AprOfferCd = GetDynamicString(row, "AprOfferCd"),
                                MayOfferCd = GetDynamicString(row, "MayOfferCd"),
                                JunOfferCd = GetDynamicString(row, "JunOfferCd"),
                                JulOfferCd = GetDynamicString(row, "JulOfferCd"),
                                AugOfferCd = GetDynamicString(row, "AugOfferCd"),
                                SepOfferCd = GetDynamicString(row, "SepOfferCd"),
                                OctOfferCd = GetDynamicString(row, "OctOfferCd"),
                                NovOfferCd = GetDynamicString(row, "NovOfferCd"),
                                DecOfferCd = GetDynamicString(row, "DecOfferCd"),

                                // Amounts
                                AnnualShrLowestCostMthlyPremAmt = GetDynamicString(row, "AnnualShrLowestCostMthlyPremAmt"),
                                JanuaryAmt = GetDynamicString(row, "JanuaryAmt"),
                                FebruaryAmt = GetDynamicString(row, "FebruaryAmt"),
                                MarchAmt = GetDynamicString(row, "MarchAmt"),
                                AprilAmt = GetDynamicString(row, "AprilAmt"),
                                MayAmt = GetDynamicString(row, "MayAmt"),
                                JuneAmt = GetDynamicString(row, "JuneAmt"),
                                JulyAmt = GetDynamicString(row, "JulyAmt"),
                                AugustAmt = GetDynamicString(row, "AugustAmt"),
                                SeptemberAmt = GetDynamicString(row, "SeptemberAmt"),
                                OctoberAmt = GetDynamicString(row, "OctoberAmt"),
                                NovemberAmt = GetDynamicString(row, "NovemberAmt"),
                                DecemberAmt = GetDynamicString(row, "DecemberAmt"),

                                // Safe Harbor
                                AnnualSafeHarborCd = GetDynamicString(row, "AnnualSafeHarborCd"),
                                JanSafeHarborCd = GetDynamicString(row, "JanSafeHarborCd"),
                                FebSafeHarborCd = GetDynamicString(row, "FebSafeHarborCd"),
                                MarSafeHarborCd = GetDynamicString(row, "MarSafeHarborCd"),
                                AprSafeHarborCd = GetDynamicString(row, "AprSafeHarborCd"),
                                MaySafeHarborCd = GetDynamicString(row, "MaySafeHarborCd"),
                                JuneSafeHarborCd = GetDynamicString(row, "JuneSafeHarborCd"),
                                JulSafeHarborCd = GetDynamicString(row, "JulSafeHarborCd"),
                                AugSafeHarborCd = GetDynamicString(row, "AugSafeHarborCd"),
                                SepSafeHarborCd = GetDynamicString(row, "SepSafeHarborCd"),
                                OctSafeHarborCd = GetDynamicString(row, "OctSafeHarborCd"),
                                NovSafeHarborCd = GetDynamicString(row, "NovSafeHarborCd"),
                                DecSafeHarborCd = GetDynamicString(row, "DecSafeHarborCd"),

                                // Covered Individuals
                                CoveredIndividualInd = GetDynamicString(row, "CoveredIndividualInd"),

                                // Dependent Information
                                Dependent_BirthDt = GetDynamicString(row, "Dependent_BirthDt"),

                                // Dependent Covered Individual Indicators
                                Dependent_CoveredIndividualAnnualInd = GetDynamicString(row, "Dependent_CoveredIndividualAnnualInd"),
                                Dependent_JanuaryInd = GetDynamicString(row, "Dependent_JanuaryInd"),
                                Dependent_FebruaryInd = GetDynamicString(row, "Dependent_FebruaryInd"),
                                Dependent_MarchInd = GetDynamicString(row, "Dependent_MarchInd"),
                                Dependent_AprilInd = GetDynamicString(row, "Dependent_AprilInd"),
                                Dependent_MayInd = GetDynamicString(row, "Dependent_MayInd"),
                                Dependent_JuneInd = GetDynamicString(row, "Dependent_JuneInd"),
                                Dependent_JulyInd = GetDynamicString(row, "Dependent_JulyInd"),
                                Dependent_AugustInd = GetDynamicString(row, "Dependent_AugustInd"),
                                Dependent_SeptemberInd = GetDynamicString(row, "Dependent_SeptemberInd"),
                                Dependent_OctoberInd = GetDynamicString(row, "Dependent_OctoberInd"),
                                Dependent_NovemberInd = GetDynamicString(row, "Dependent_NovemberInd"),
                                Dependent_DecemberInd = GetDynamicString(row, "Dependent_DecemberInd")
                            };

                            result.Employees.Add(employee);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetExportMailFulfillmentData", "Service", "Failed", "DB");
                throw;
            }

            return result;
        }

        // Helper method to safely get string values from dynamic objects
        private string GetDynamicString(dynamic obj, string propertyName)
        {
            try
            {
                var value = ((IDictionary<string, object>)obj)[propertyName];
                return value?.ToString();
            }
            catch
            {
                return null;
            }
        }

        #endregion


    }
}
