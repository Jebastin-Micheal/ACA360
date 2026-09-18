using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Models;
using ACA360.Core.Models.ACA360.Core.Models;
using ACA360.Repositories.Interfaces;
using Azure.Storage.Blobs;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Services
{
    public class PdfService : IPdfService
    {
        private readonly string _connectionString;
        private readonly ISmartPdfService _smartPdf;
        private readonly IACALogicService _acaLogic;
        private readonly IEmployerService _employerService;
        private readonly INotificationService _notificationService;
        private readonly IWebHostEnvironment _environment;
        private readonly IPdfMergeService _mergeService;
        private readonly IConfiguration _configuration;
        private readonly IAzureBlobService _azureBlobService;
        private readonly string _generatedReportsFolder;
        public PdfService(
      string connectionString,
      ISmartPdfService smartPdf,
      IACALogicService acaLogic,
      IEmployerService employerService,
      INotificationService notificationService,
      IWebHostEnvironment environment,
      IPdfMergeService mergeService,
      IConfiguration configuration, IAzureBlobService azureBlobService)
        {
            _connectionString = connectionString;
            _smartPdf = smartPdf;
            _acaLogic = acaLogic;
            _employerService = employerService;
            _notificationService = notificationService;
            _environment = environment;
            _mergeService = mergeService;
            _configuration = configuration;
            _azureBlobService = azureBlobService;
            _generatedReportsFolder = configuration["AzureBlob:GeneratedReports"] ?? "GeneratedReports";
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        // =========================================================
        // 1. FORM 1095-C (ALE Employee)
        // =========================================================
        public async Task<byte[]> Generate1095CForEmployeeAsync(int employeeId, int year, bool suppressSSN = false)
        {
            // 1. Get Template & Mappings
            var (templatePath, mappings) = await GetTemplateAndMappingsAsync(year, "1095-C");

            // 2. Fetch Data
            var codes = await _acaLogic.GetCodesForEmployeeAsync(employeeId, year);
            if (codes == null) throw new Exception("ACA Codes not calculated.");

            var fullData = await GetFullPdfDataAsync(employeeId);

            // 3. Bind Data
            var dataMap = FlattenEmployeeData(fullData, suppressSSN);

            
            MapCodesToDictionary(dataMap, codes);

            // Line 17 — the ICHRA affordability ZIP, which belongs to the 1L-1U
            // codes and has nothing to do with 1A. The value is the one
            // sp_Generate1095Codes already resolved into the *_ZIP columns
            // (ICHRA record, then work-site ZIP, then residence ZIP), so the
            // printed form and the AIR XML report the same thing.
            //
            // This previously wrote the EMPLOYER's address ZIP whenever the month
            // was coded 1A, and then blanked Employer.Zip — the Part I address
            // field — for everyone else.
            dataMap["Line17.ALL"] = Line17Zip(codes.ALLM_COC, codes.ALLM_ZIP);
            dataMap["Line17.JAN"] = Line17Zip(codes.JAN_COC, codes.JAN_ZIP);
            dataMap["Line17.FEB"] = Line17Zip(codes.FEB_COC, codes.FEB_ZIP);
            dataMap["Line17.MAR"] = Line17Zip(codes.MAR_COC, codes.MAR_ZIP);
            dataMap["Line17.APR"] = Line17Zip(codes.APR_COC, codes.APR_ZIP);
            dataMap["Line17.MAY"] = Line17Zip(codes.MAY_COC, codes.MAY_ZIP);
            dataMap["Line17.JUN"] = Line17Zip(codes.JUN_COC, codes.JUN_ZIP);
            dataMap["Line17.JUL"] = Line17Zip(codes.JUL_COC, codes.JUL_ZIP);
            dataMap["Line17.AUG"] = Line17Zip(codes.AUG_COC, codes.AUG_ZIP);
            dataMap["Line17.SEP"] = Line17Zip(codes.SEP_COC, codes.SEP_ZIP);
            dataMap["Line17.OCT"] = Line17Zip(codes.OCT_COC, codes.OCT_ZIP);
            dataMap["Line17.NOV"] = Line17Zip(codes.NOV_COC, codes.NOV_ZIP);
            dataMap["Line17.DEC"] = Line17Zip(codes.DEC_COC, codes.DEC_ZIP);

            // Employer.Zip is the Part I employer address and is set correctly by
            // FlattenEmployeeData. It is deliberately not touched here.
            // 4. Handle Part III (Covered Individuals) - Logic moved from Legacy App
            var coveredIndividuals = await _acaLogic.GetCoveredIndividualsAsync(employeeId, year);

            // Map the rows (assuming your PDF Map uses "Covered.Name.1", "Covered.Jan.1", etc.)
            for (int i = 0; i < coveredIndividuals.Count && i < 13; i++) // Max 13 rows per page usually
            {
                var ci = coveredIndividuals[i];
                int rowNum = i + 1; // 1-based index

                dataMap[$"Covered.FirstName.{rowNum}"] = ci.FirstName;
                dataMap[$"Covered.MiddleName.{rowNum}"] = ci.MiddleName ?? ""; // Handle nulls safely
                dataMap[$"Covered.LastName.{rowNum}"] = ci.LastName;

                dataMap[$"Covered.Name.{rowNum}"] = $"{ci.FirstName} {ci.LastName}";
                dataMap[$"Covered.SSN.{rowNum}"] = MaskSSN(ci.SSN, suppressSSN);
                dataMap[$"Covered.DOB.{rowNum}"] = ci.Birthday?.ToString("MM/dd/yyyy") ?? "";

                // Map Checkboxes (Legacy logic usually maps to "X" or "1")
                if (ci.AllM) dataMap[$"Covered.All12.{rowNum}"] = "X";
                if (ci.Jan) dataMap[$"Covered.Jan.{rowNum}"] = "X";
                if (ci.Feb) dataMap[$"Covered.Feb.{rowNum}"] = "X";
                if (ci.Mar) dataMap[$"Covered.Mar.{rowNum}"] = "X";
                if (ci.Apr) dataMap[$"Covered.Apr.{rowNum}"] = "X";
                if (ci.May) dataMap[$"Covered.May.{rowNum}"] = "X";
                if (ci.Jun) dataMap[$"Covered.Jun.{rowNum}"] = "X";
                if (ci.Jul) dataMap[$"Covered.Jul.{rowNum}"] = "X";
                if (ci.Aug) dataMap[$"Covered.Aug.{rowNum}"] = "X";
                if (ci.Sep) dataMap[$"Covered.Sep.{rowNum}"] = "X";
                if (ci.Oct) dataMap[$"Covered.Oct.{rowNum}"] = "X";
                if (ci.Nov) dataMap[$"Covered.Nov.{rowNum}"] = "X";
                if (ci.Dec) dataMap[$"Covered.Dec.{rowNum}"] = "X";
            }

            // 5. Fill PDF
            return _smartPdf.FillPdfForm(templatePath, MapToPdfFields(dataMap, mappings));
        }

        // =========================================================
        // 2. FORM 1095-B (Non-ALE Employee)
        // =========================================================
        public async Task<byte[]> Generate1095BForEmployeeAsync(int employeeId, int year, bool suppressSSN = false)
        {
            var (templatePath, mappings) = await GetTemplateAndMappingsAsync(year, "1095-B");
            var fullData = await GetFullPdfDataAsync(employeeId);

            //EmployeeCode Details
            var codes = await _acaLogic.GetCodesForEmployeeAsync(employeeId, year);


            // Fetch Coverage Data (Part IV)
            var coveredIndividuals = await _acaLogic.GetCoveredIndividualsAsync(employeeId, year);

            var dataMap = FlattenEmployeeData(fullData, suppressSSN);

            MapCodesToDictionary(dataMap, codes);
            

            // Map Part IV (Covered Individuals) - Same logic as Part III above
            for (int i = 0; i < coveredIndividuals.Count && i < 13; i++)
            {
                var ci = coveredIndividuals[i];
                int rowNum = i + 1;


                dataMap[$"Covered.FirstName.{rowNum}"] = ci.FirstName;
                dataMap[$"Covered.MiddleName.{rowNum}"] = ci.MiddleName ?? ""; // Handle nulls safely
                dataMap[$"Covered.LastName.{rowNum}"] = ci.LastName;

                dataMap[$"Covered.Name.{rowNum}"] = $"{ci.FirstName} {ci.LastName}";
                dataMap[$"Covered.SSN.{rowNum}"] = MaskSSN(ci.SSN, suppressSSN); // Mask Part IV here

                dataMap[$"Covered.DOB.{rowNum}"] = ci.Birthday?.ToString("MM/dd/yyyy") ?? "";
                if (ci.AllM) dataMap[$"Covered.All12.{rowNum}"] = "X";
                if (ci.Jan) dataMap[$"Covered.Jan.{rowNum}"] = "X";
                if (ci.Feb) dataMap[$"Covered.Feb.{rowNum}"] = "X";
                if (ci.Mar) dataMap[$"Covered.Mar.{rowNum}"] = "X";
                if (ci.Apr) dataMap[$"Covered.Apr.{rowNum}"] = "X";
                if (ci.May) dataMap[$"Covered.May.{rowNum}"] = "X";
                if (ci.Jun) dataMap[$"Covered.Jun.{rowNum}"] = "X";
                if (ci.Jul) dataMap[$"Covered.Jul.{rowNum}"] = "X";
                if (ci.Aug) dataMap[$"Covered.Aug.{rowNum}"] = "X";
                if (ci.Sep) dataMap[$"Covered.Sep.{rowNum}"] = "X";
                if (ci.Oct) dataMap[$"Covered.Oct.{rowNum}"] = "X";
                if (ci.Nov) dataMap[$"Covered.Nov.{rowNum}"] = "X";
                if (ci.Dec) dataMap[$"Covered.Dec.{rowNum}"] = "X";
            }

            return _smartPdf.FillPdfForm(templatePath, MapToPdfFields(dataMap, mappings));
        }

        // =========================================================
        // 3. FORM 1094-C (ALE Transmittal) - UPDATED
        // =========================================================

        public async Task<byte[]> Generate1094CForEmployerAsync(int employerId, int year)
        {
            var (templatePath, mappings) = await GetTemplateAndMappingsAsync(year, "1094-C");

            // Fetch 1094-C specific details (Certifications, Minimum Coverage, etc.)
            var employer1094 = await _employerService.GetEmployer1094DetailsAsync(employerId.ToString());
            if (employer1094 == null) throw new Exception("Employer 1094 details not found.");

            var aggregatedMembers = await _employerService.GetAggregatedMembersAsync(employerId);

            var dataMap = new Dictionary<string, string>();

            // ========================================================
            // FIX: DIRECT DB QUERY FOR EMPLOYER BASIC INFO 
            // ========================================================
            using var db = Connection;
            var empDbDetails = await db.QueryFirstOrDefaultAsync<dynamic>(
                @"SELECT name, taxid, address, city, state, zip, phoneNumber, contactName, 
                 signTitle, signDate,
                 ISNULL(isCorrected, 0) as isCorrected, 
                 ISNULL(totalNumberForms, 0) as totalNumberForms,
                 ISNULL(isAuthoritative, 0) as isAuthoritative,
                 ISNULL(isAggregatedAle, 0) as isAggregatedAle
          FROM Employer WHERE id = @Id",
                new { Id = employerId }
            );

            if (empDbDetails != null)
            {
                // Map directly from the DB columns
                dataMap["Employer.Name"] = empDbDetails.name;
                dataMap["Employer.EIN"] = empDbDetails.taxid;
                dataMap["Employer.Address"] = empDbDetails.address;
                dataMap["Employer.City"] = empDbDetails.city;
                dataMap["Employer.State"] = empDbDetails.state;
                dataMap["Employer.Zip"] = empDbDetails.zip;
                dataMap["Employer.Phone"] = empDbDetails.phoneNumber;
                dataMap["Employer.ContactName"] = empDbDetails.contactName;
                dataMap["TotalForms"] = empDbDetails.totalNumberForms.ToString();
                dataMap["Count.Total1095C.ALEAggregate"] = empDbDetails.totalNumberForms.ToString();
                dataMap["Header.SignatureName"] = empDbDetails.contactName;
                dataMap["Header.SignatureTitle"] = empDbDetails.signTitle;

                // Safely format the date if it exists
                dataMap["Header.SignatureDate"] = empDbDetails.signDate != null
                    ? Convert.ToDateTime(empDbDetails.signDate).ToString("MM/dd/yyyy")
                    : "";

                if (empDbDetails.isCorrected == 1)
                {
                    dataMap["Employer.isCorrected"] = "X";
                }
                // DB value dynamic check
                if (empDbDetails.isAuthoritative == 1)
                {
                    dataMap["Header.AuthoritativeTransmittal"] = "X";
                    dataMap["Authoritative"] = "X"; // Safe side fallback
                }
                else
                {
                    dataMap["Header.AuthoritativeTransmittal"] = "";
                    dataMap["Authoritative"] = "";
                }

                if (empDbDetails != null && empDbDetails.isAggregatedAle == 1)
                {
                    dataMap["Aggregated.ALEGroup.Indicator"] = "X";   // YES Checkbox-ku
                    dataMap["Aggregated.ALEGroup.Indicator.No"] = "";
                }
                else
                {
                    dataMap["Aggregated.ALEGroup.Indicator"] = "";
                    dataMap["Aggregated.ALEGroup.Indicator.No"] = "X"; // NO Checkbox-ku
                }
            }

            // Part II: Certifications
            if (employer1094.CertA) dataMap["Cert.QualifyingOffer"] = "X";
            if (employer1094.CertD) dataMap["Cert.98Percent"] = "X";

            // Aggregated Group Indicator (Line 21)
            if (aggregatedMembers.Any()) dataMap["IsAggregatedGroup"] = "X";

            // Part III: Monthly Counts (Jan-Dec)

            string[] months = { "All", "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

            for (int i = 0; i <= 12; i++)
            {
                // Columns (a) - (e)
                // (a) MEC Offer Indicator
                if (employer1094.MinimumCoverage[i] == 1)
                {
                    dataMap[$"Ind.MEC.Yes.{months[i]}"] = "X";
                    dataMap[$"Ind.MEC.No.{months[i]}"] = "";
                }
                else if (employer1094.MinimumCoverage[i] == 0)
                {
                    dataMap[$"Ind.MEC.Yes.{months[i]}"] = "";
                    dataMap[$"Ind.MEC.No.{months[i]}"] = "X";
                }
                else
                {
                    dataMap[$"Ind.MEC.Yes.{months[i]}"] = "";
                    dataMap[$"Ind.MEC.No.{months[i]}"] = "";
                }

                // (b) Full Time Count
                dataMap[$"Count.FullTime.{months[i]}"] = employer1094.FullTime[i].ToString();

                // (c) Total Employee Count
                dataMap[$"Count.Total.{months[i]}"] = employer1094.Total[i].ToString();

                // (d) Aggregated Group Indicator (Monthly)
                if (employer1094.AggregateGroup[i] == 1) dataMap[$"Group.Indicator.{months[i]}"] = "X";
            }


            // Part IV: Other ALE Members (Lines 36 - 65
            for (int i = 0; i < aggregatedMembers.Count && i < 30; i++)
            {
                var member = aggregatedMembers[i];
                int row = i + 1;

                dataMap[$"AggregateMember.Name.{row}"] = member.MemberName;
                dataMap[$"AggregateMember.EIN.{row}"] = member.MemberEIN;
            }

            return _smartPdf.FillPdfForm(templatePath, MapToPdfFields(dataMap, mappings));
        }

        // =========================================================
        // 4. FORM 1094-B (Non-ALE Transmittal) - UPDATED
        // =========================================================
        public async Task<byte[]> Generate1094BForEmployerAsync(int employerId, int year)
        {
            var (templatePath, mappings) = await GetTemplateAndMappingsAsync(year, "1094-B");

            var dataMap = new Dictionary<string, string>();

            using var db = Connection;
            var empDbDetails = await db.QueryFirstOrDefaultAsync<dynamic>(
                @"SELECT name, taxid, address, city, state, zip, phoneNumber, contactName, 
                 signTitle, signDate,
                 ISNULL(totalNumberForms, 0) as totalNumberForms
          FROM Employer WHERE id = @Id",
                new { Id = employerId }
            );

            if (empDbDetails != null)
            {
                dataMap["Employer.Name"] = empDbDetails.name;
                dataMap["Employer.EIN"] = empDbDetails.taxid;
                dataMap["Employer.Address"] = empDbDetails.address;
                dataMap["Employer.City"] = empDbDetails.city;
                dataMap["Employer.State"] = empDbDetails.state;
                dataMap["Employer.Zip"] = empDbDetails.zip;
                dataMap["Employer.Phone"] = empDbDetails.phoneNumber;
                dataMap["Employer.ContactName"] = empDbDetails.contactName;
                dataMap["TotalForms"] = empDbDetails.totalNumberForms.ToString();

                dataMap["Header.SignatureName"] = empDbDetails.contactName;
                dataMap["Header.SignatureTitle"] = empDbDetails.signTitle;


                dataMap["Header.SignatureDate"] = empDbDetails.signDate != null
                    ? Convert.ToDateTime(empDbDetails.signDate).ToString("MM/dd/yyyy")
                    : DateTime.Now.ToShortDateString();
            }

            return _smartPdf.FillPdfForm(templatePath, MapToPdfFields(dataMap, mappings));
        }

        // =========================================================
        // 5. BATCH PROCESSOR
        // =========================================================
        public async Task ProcessBatchAndNotifyAsync(int employerId, int year, string userId, string formType)
        {
            try
            {
                byte[] transmittalBytes = null;
                string transmittalName = "";
                bool isCForm = formType.Contains("1094-C");

                using var memoryStream = new MemoryStream();
                using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
                {
                    // A. Generate Transmittal (1094)
                    try
                    {
                        if (isCForm)
                        {
                            transmittalBytes = await Generate1094CForEmployerAsync(employerId, year);
                            transmittalName = $"1094C_{year}_{employerId}.pdf";
                        }
                        else
                        {
                            transmittalBytes = await Generate1094BForEmployerAsync(employerId, year);
                            transmittalName = $"1094B_{year}_{employerId}.pdf";
                        }

                        if (transmittalBytes != null)
                        {
                            var entry = archive.CreateEntry(transmittalName);
                            using var s = entry.Open();
                            s.Write(transmittalBytes, 0, transmittalBytes.Length);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Transmittal failure shouldn't stop individual forms, but note it
                        // Log(ex);
                    }

                    // B. Generate Employee Forms (1095)
                    var employees = await _acaLogic.GetEmployeeCodesListAsync(employerId, year);
                    employees = employees.Where(e => e.GetForm == 1).ToList();
                    foreach (var emp in employees)
                    {
                        try
                        {
                            byte[] empBytes;
                            string empFileName;

                            if (isCForm)
                            {
                                empBytes = await Generate1095CForEmployeeAsync(emp.EmployeeId, year);
                                empFileName = $"1095C_{emp.LastName}_{emp.FirstName}_{emp.EmployeeId}.pdf";
                            }
                            else
                            {
                                empBytes = await Generate1095BForEmployeeAsync(emp.EmployeeId, year);
                                empFileName = $"1095B_{emp.LastName}_{emp.FirstName}_{emp.EmployeeId}.pdf";
                            }

                            var entry = archive.CreateEntry(empFileName);
                            using var s = entry.Open();
                            s.Write(empBytes, 0, empBytes.Length);
                        }
                        catch
                        {
                            // Continue on error
                        }
                    }
                }

                // =====================================================
                // ? AZURE UPLOAD LOGIC
                // =====================================================
                string zipName = $"Filing_{formType}_{year}_{employerId}.zip";
                await _azureBlobService.UploadFileAsync(memoryStream, _generatedReportsFolder, zipName);



                // D. Notify
                await _notificationService.AddNotificationAsync(
                    userId,
                    "Batch Complete",
                    $"{formType} generated for Employer ID {employerId}.",
                    "success",
                    "/Report/GeneratedFiles"
                );
            }
            catch (Exception ex)
            {
                await _notificationService.AddNotificationAsync(userId, "Failed", $"Batch Error: {ex.Message}", "danger");
            }
        }


        public async Task<byte[]> GenerateEmployerBatchAsync(int employerId, int year)
        {
            // Fallback for legacy calls if any
            return null; // Implement if needed or redirect to ProcessBatchAndNotifyAsync
        }

        // =========================================================
        // HELPERS
        // =========================================================

        private async Task<(string Path, IEnumerable<PdfFieldMap> Maps)> GetTemplateAndMappingsAsync(int year, string type)
        {
            using var db = Connection;
            var template = await db.QuerySingleOrDefaultAsync<PdfTemplate>(
                "SELECT * FROM PdfTemplates WHERE TaxYear = @Year AND FormType = @Type AND IsActive = 1",
                new { Year = year, Type = type });

            if (template == null || !File.Exists(template.FilePath))
                throw new Exception($"Template {type} for {year} not found.");

            var maps = await db.QueryAsync<PdfFieldMap>(
                "SELECT * FROM PdfFieldMaps WHERE TemplateId = @Id",
                new { Id = template.TemplateId });

            return (template.FilePath, maps);
        }

        private Dictionary<string, string> MapToPdfFields(Dictionary<string, string> data, IEnumerable<PdfFieldMap> mappings)
        {
            var finalMap = new Dictionary<string, string>();
            foreach (var map in mappings)
            {
                if (data.ContainsKey(map.SystemDataKey))
                {
                    finalMap[map.PdfFieldName] = data[map.SystemDataKey];
                }
            }
            return finalMap;
        }

        private Dictionary<string, string> FlattenEmployeeData(FullPdfDataDto d, bool suppressSSN)
        {
            var dict = new Dictionary<string, string>
            {
                { "Employee.FirstName", d.EmployeeFirstName },
                { "Employee.MiddleName", d.EmployeeMiddleName ?? "" }, // NEW
                { "Employee.LastName", d.EmployeeLastName },
              { "Employee.SSN", MaskSSN(d.EmployeeSSN, suppressSSN) },
                { "Employee.DOB", d.EmployeeDOB?.ToString("MM/dd/yyyy") ?? "" }, // NEW
                { "Employee.Address", d.EmployeeAddress },
                { "Employee.City", d.EmployeeCity },
                { "Employee.State", d.EmployeeState },
                { "Employee.Zip", d.EmployeeZip },
                { "Employee.Country", d.EmployeeCountry },
                { "Employer.Name", d.EmployerName },
                { "Employer.EIN", d.EmployerEIN },
                { "Employer.Address", d.EmployerAddress },
                { "Employer.City", d.EmployerCity },
                { "Employer.State", d.EmployerState },
                { "Employer.Zip", d.EmployerZip },
                { "Employer.Phone", d.EmployerPhone },
                { "Coverage.OriginCode", d.OriginCode ?? "" }//New
            };

            dict["Line17.ALL"] = "";
            dict["Line17.JAN"] = "";
            dict["Line17.FEB"] = "";
            dict["Line17.MAR"] = "";
            dict["Line17.APR"] = "";
            dict["Line17.MAY"] = "";
            dict["Line17.JUN"] = "";
            dict["Line17.JUL"] = "";
            dict["Line17.AUG"] = "";
            dict["Line17.SEP"] = "";
            dict["Line17.OCT"] = "";
            dict["Line17.NOV"] = "";
            dict["Line17.DEC"] = "";

            dict["Employee.IsCorrected"] =
             d.IsCorrected?.ToString() ?? "0";
            // Calculate Age (Jan 1)
            if (d.EmployeeDOB.HasValue)
            {
                var jan1 = new DateTime(DateTime.Now.Year, 1, 1); // Use filing year in production
                var age = jan1.Year - d.EmployeeDOB.Value.Year;
                if (d.EmployeeDOB.Value.Date > jan1.AddYears(-age)) age--;
                dict["Code.Age"] = age.ToString();
            }

            return dict;
        }

        /// <summary>
        /// Line 14 codes that carry a Line 17 ZIP. These are the ICHRA offer codes;
        /// every other code leaves Line 17 blank.
        /// </summary>
        private static readonly HashSet<string> IchraOfferCodes =
            new(StringComparer.OrdinalIgnoreCase)
            { "1L", "1M", "1N", "1O", "1P", "1Q", "1R", "1S", "1T", "1U" };

        /// <summary>
        /// Returns the Line 17 ZIP for a month, or an empty string when the month
        /// carries no ICHRA offer. The code is checked as well as the ZIP because
        /// sp_UpdateManualCodes lets a reviewer change line 14 after generation:
        /// without this guard, switching an ICHRA month to a group-plan code would
        /// leave the stale ZIP printed against a code that must not have one.
        /// </summary>
        private static string Line17Zip(string? offerCode, string? zip)
        {
            if (string.IsNullOrWhiteSpace(zip) || string.IsNullOrWhiteSpace(offerCode))
                return "";

            return IchraOfferCodes.Contains(offerCode.Trim()) ? zip.Trim() : "";
        }

        private void MapCodesToDictionary(Dictionary<string, string> map, EmployeeCode c)
        {
            // CORRECTED and VOID are independent boxes and an ordinary statement
            // carries neither. Keys are only added when the box should be ticked:
            // MapToPdfFields copies across just the keys present here, so an absent
            // key leaves the checkbox at its unticked default.
            //
            // This previously read "if corrected, tick CORRECTED, ELSE tick VOID",
            // which stamped VOID on every ordinary 1095-C and 1095-B that went out.
            if (c.IsCorrected == 1)
            {
                map["Employeecode.header.iscorrect"] = "X";
            }

            if (c.IsVoid == 1)
            {
                map["Employeecode.header.void"] = "X";
            }

            map["Code.PlanStartMonth"] = c.PlanStartMonth.HasValue ? c.PlanStartMonth.Value.ToString("00") : "";

            map["Code.All.14"] = c.ALLM_COC;
            map["Code.Jan.14"] = c.JAN_COC; map["Code.Feb.14"] = c.FEB_COC; map["Code.Mar.14"] = c.MAR_COC;
            map["Code.Apr.14"] = c.APR_COC; map["Code.May.14"] = c.MAY_COC; map["Code.Jun.14"] = c.JUN_COC;
            map["Code.Jul.14"] = c.JUL_COC; map["Code.Aug.14"] = c.AUG_COC; map["Code.Sep.14"] = c.SEP_COC;
            map["Code.Oct.14"] = c.OCT_COC; map["Code.Nov.14"] = c.NOV_COC; map["Code.Dec.14"] = c.DEC_COC;

            string Fmt(decimal? d) => d.HasValue ? $"{d.Value:F2}" : "";
            map["Code.All.15"] = Fmt(c.ALLM_LCMP);
            map["Code.Jan.15"] = Fmt(c.JAN_LCMP); map["Code.Feb.15"] = Fmt(c.FEB_LCMP); map["Code.Mar.15"] = Fmt(c.MAR_LCMP);
            map["Code.Apr.15"] = Fmt(c.APR_LCMP); map["Code.May.15"] = Fmt(c.MAY_LCMP); map["Code.Jun.15"] = Fmt(c.JUN_LCMP);
            map["Code.Jul.15"] = Fmt(c.JUL_LCMP); map["Code.Aug.15"] = Fmt(c.AUG_LCMP); map["Code.Sep.15"] = Fmt(c.SEP_LCMP);
            map["Code.Oct.15"] = Fmt(c.OCT_LCMP); map["Code.Nov.15"] = Fmt(c.NOV_LCMP); map["Code.Dec.15"] = Fmt(c.DEC_LCMP);

            map["Code.All.16"] = c.ALLM_SHC;
            map["Code.Jan.16"] = c.JAN_SHC; map["Code.Feb.16"] = c.FEB_SHC; map["Code.Mar.16"] = c.MAR_SHC;
            map["Code.Apr.16"] = c.APR_SHC; map["Code.May.16"] = c.MAY_SHC; map["Code.Jun.16"] = c.JUN_SHC;
            map["Code.Jul.16"] = c.JUL_SHC; map["Code.Aug.16"] = c.AUG_SHC; map["Code.Sep.16"] = c.SEP_SHC;
            map["Code.Oct.16"] = c.OCT_SHC; map["Code.Nov.16"] = c.NOV_SHC; map["Code.Dec.16"] = c.DEC_SHC;
        }

        private async Task<FullPdfDataDto> GetFullPdfDataAsync(int employeeId)
        {
            using var db = Connection;
            string sql = @"
        SELECT 
            e.FirstName AS EmployeeFirstName, 
            e.MiddleName AS EmployeeMiddleName, -- NEW
            e.LastName AS EmployeeLastName, 
            e.SSN AS EmployeeSSN,
            e.Birthday AS EmployeeDOB,          -- NEW (For Age Calc)
            e.isCorrected AS IsCorrected,
            e.Address AS EmployeeAddress, 
            e.City AS EmployeeCity, 
            e.State AS EmployeeState, 
            e.Zip AS EmployeeZip,
            
            er.name AS EmployerName, 
            er.taxid AS EmployerEIN, 
             er.phoneNumber AS EmployerPhone,
            er.address AS EmployerAddress, 
            er.city AS EmployerCity, 
            er.state AS EmployerState, 
            er.zip AS EmployerZip,
            er.originCode AS OriginCode
        FROM Employee e
        INNER JOIN Employer er ON e.EmployerId = er.id
        WHERE e.id = @Id";

            return await db.QuerySingleOrDefaultAsync<FullPdfDataDto>(sql, new { Id = employeeId });
        }

        class FullPdfDataDto
        {
            public string EmployeeFirstName { get; set; }
            public string EmployeeMiddleName { get; set; } // NEW
            public DateTime? EmployeeDOB { get; set; }     // NEW
            public int? IsCorrected { get; set; }
            public string EmployeeLastName { get; set; }
            public string EmployeeSSN { get; set; }
            public string EmployeeAddress { get; set; }
            public string EmployeeCity { get; set; }
            public string EmployeeState { get; set; }
            public string EmployeeZip { get; set; }
            public string EmployeeCountry { get; set; }
            public string EmployerName { get; set; }
            public string EmployerEIN { get; set; }
            public string EmployerPhone { get; set; }
            public string EmployerAddress { get; set; }
            public string EmployerCity { get; set; }
            public string EmployerState { get; set; }
            public string EmployerZip { get; set; }

            public string OriginCode { get; set; }
        }


        //changes code:

        public async Task<string> GenerateBatchPdfAsync(int employerId, List<int> employeeIds, int year, string formType)
        {
            try
            {
                using var db = new SqlConnection(_connectionString);

                // 1. Fetch Employer
                var employer = await db.QueryFirstOrDefaultAsync<Employer>(
                    "SELECT * FROM Employer WHERE Id = @Id", new { Id = employerId });

                if (employer == null) throw new Exception("Employer not found");

                // 2. Fetch Employees (Filter Logic)
                string empSql;
                IEnumerable<Employee> employees;

                if (employeeIds != null && employeeIds.Any())
                {
                    // Case A: Specific Employees
                    empSql = @"SELECT * FROM Employee 
                 WHERE EmployerId = @EmpId 
                 AND Id IN @Ids 
                 AND IsDeleted = 0";
                    employees = await db.QueryAsync<Employee>(empSql, new { EmpId = employerId, Ids = employeeIds });
                }
                else
                {
                    // Case B: All Employees
                    empSql = @"SELECT * FROM Employee 
                 WHERE EmployerId = @EmpId 
                 AND IsDeleted = 0";
                    employees = await db.QueryAsync<Employee>(empSql, new { EmpId = employerId });
                }

                if (!employees.Any()) throw new Exception("No employees found to generate.");

                // 3. Generate ZIP
                bool isALE = formType.Contains("1095-C");

                using var memoryStream = new MemoryStream();
                using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
                {
                    foreach (var emp in employees)
                    {
                        if (emp.Id == null) continue; // Safety check

                        byte[] pdfBytes = null;

                        try
                        {
                            // === FIX IS HERE ===
                            // Remove '_smartPdf.' because these methods are in THIS class
                            if (isALE)
                            {
                                pdfBytes = await Generate1095CForEmployeeAsync(emp.Id.Value, year);
                            }
                            else
                            {
                                pdfBytes = await Generate1095BForEmployeeAsync(emp.Id.Value, year);
                            }
                            // ===================
                        }
                        catch (Exception ex)
                        {
                            // Optional: Log error
                            continue;
                        }

                        if (pdfBytes != null)
                        {
                            // Add to ZIP
                            string safeName = SanitizeFileName($"{formType}_{emp.LastName}_{emp.FirstName}_{emp.Id}.pdf");
                            var entry = archive.CreateEntry(safeName);

                            using var entryStream = entry.Open();
                            await entryStream.WriteAsync(pdfBytes, 0, pdfBytes.Length);
                        }
                    }
                }

                // =====================================================
                // ? AZURE UPLOAD LOGIC
                // =====================================================
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string zipName = $"Batch_{employerId}_{(isALE ? "1095C" : "1095B")}_{timestamp}.zip";

                await _azureBlobService.UploadFileAsync(memoryStream, _generatedReportsFolder, zipName);

                return zipName;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to generate batch: {ex.Message}");
            }
        }

        // Helper method to determine ALE status
        private async Task<bool> DetermineIfEmployerIsALE(int employerId, int year, string formType = null)
        {
            // Priority 1: Use provided form type if specified
            if (!string.IsNullOrEmpty(formType))
            {
                return formType.Contains("1095-C") || formType.Contains("1094-C");
            }

            // Priority 2: Check database for ALE determination
            using var db = Connection;

            // Check if employer has 1094-C records (ALE transmittal)
            try
            {
                var has1094C = await db.ExecuteScalarAsync<bool>(
                    @"SELECT CASE WHEN EXISTS (
                SELECT 1 FROM Employer1094Details 
                WHERE EmployerId = @EmployerId AND FilingYear = @Year
            ) THEN 1 ELSE 0 END",
                    new { EmployerId = employerId, Year = year });

                if (has1094C) return true;
            }
            catch
            {
                // Table might not exist, continue to next check
            }

            // Check employer type from database
            var employerType = await db.QueryFirstOrDefaultAsync<dynamic>(
                @"SELECT 
            e.ALEType,
            e.IsALE,
            -- Check employee count for the year
            (SELECT COUNT(*) FROM Employee WHERE EmployerId = e.id AND IsDeleted = 0) as EmployeeCount
          FROM Employer e
          WHERE e.id = @Id",
                new { Id = employerId });

            // Check different possible column names for ALE status
            if (employerType != null)
            {
                // Try common column names
                if (employerType.IsALE != null && Convert.ToBoolean(employerType.IsALE))
                    return true;

                if (employerType.ALEType != null)
                {
                    string aleType = employerType.ALEType.ToString();
                    return aleType.Equals("ALE", StringComparison.OrdinalIgnoreCase) ||
                           aleType.Equals("C", StringComparison.OrdinalIgnoreCase);
                }

                // Check employee count (ALE threshold is typically 50+)
                if (employerType.EmployeeCount >= 50)
                    return true;
            }

            // Default: Assume non-ALE to be safe
            return false;
        }

        // Helper method for safe filenames
        private string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return "Unknown";

            // Remove invalid characters
            var invalidChars = Path.GetInvalidFileNameChars();
            var result = new string(fileName.Where(ch => !invalidChars.Contains(ch)).ToArray());

            // Trim length if too long
            if (result.Length > 50)
                result = result.Substring(0, 50);

            return result;
        }


        public async Task ProcessEmployeeBatchAsync(
    int employerId,
    List<int> employeeIds,
    int year,
    string userId,
    string formType)
        {
            try
            {
                bool isCForm = formType.Contains("1095-C");

                using var memoryStream = new MemoryStream();

                using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
                {
                    foreach (var empId in employeeIds)
                    {
                        try
                        {
                            byte[] pdfBytes;
                            string fileName;

                            if (isCForm)
                            {
                                pdfBytes = await Generate1095CForEmployeeAsync(empId, year);
                                fileName = $"1095C_{empId}.pdf";
                            }
                            else
                            {
                                pdfBytes = await Generate1095BForEmployeeAsync(empId, year);
                                fileName = $"1095B_{empId}.pdf";
                            }

                            var entry = archive.CreateEntry(fileName);
                            using var stream = entry.Open();
                            await stream.WriteAsync(pdfBytes, 0, pdfBytes.Length);
                        }
                        catch (Exception ex)
                        {
                            await _notificationService.AddNotificationAsync(
                                userId,
                                "Employee PDF Failed",
                                $"Failed generating PDF for Employee ID: {empId}",
                                "warning"
                            );

                            continue;
                        }
                    }
                }

                // =====================================================
                // ? AZURE UPLOAD LOGIC
                // =====================================================
                string zipName = $"Filing_{formType}_{year}_{employerId}.zip";

                await _azureBlobService.UploadFileAsync(memoryStream, _generatedReportsFolder, zipName);

                // ? Notify user
                await _notificationService.AddNotificationAsync(
                    userId,
                    "Batch Complete",
                    $"{formType} batch generated successfully.",
                    "success",
                    "/Downloads"
                );
            }
            catch (Exception ex)
            {
                await _notificationService.AddNotificationAsync(
                    userId,
                    "Batch Failed",
                    ex.Message,
                    "danger"
                );
            }
        }



        public async Task GenerateMultiEmployerBatchAsync(
           List<int> employerIds,
           List<int> employeeIds,
           int year,
           string userId,
           string formType, bool suppressSSN = false)
        {
            string zipName = $"MultiBatch_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
            string blobPath = string.IsNullOrEmpty(_generatedReportsFolder) ? zipName : $"{_generatedReportsFolder}/{zipName}";

            // 1. Direct Azure Blob Client Setup
            string storageConnectionString = _configuration["AzureBlob:ConnectionString"];
            string containerName = _configuration["AzureBlob:ContainerName"];

            var blobServiceClient = new BlobServiceClient(storageConnectionString);
            var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(blobPath);

            try
            {
                var employerNamesList = new List<string>();
                using var db = new SqlConnection(_connectionString);
                var failedEmployeeIds = new System.Collections.Concurrent.ConcurrentBag<int>();

                // STRATEGY PATTERN 
                var employerFormStrategies = new Dictionary<string, Func<int, int, Task<byte[]>>>
           {
               { "1094-C", Generate1094CForEmployerAsync },
               { "1094-B", Generate1094BForEmployerAsync }
           };

                var employeeFormStrategies = new Dictionary<string, Func<int, int, bool, Task<byte[]>>>
           {
               { "1095-C", Generate1095CForEmployeeAsync },
               { "1095-B", Generate1095BForEmployeeAsync }
           };

                // 2. Open DIRECT Stream to Azure 

                using (var azureStream = await blobClient.OpenWriteAsync(true))
                {
                    using (var archive = new ZipArchive(azureStream, ZipArchiveMode.Create, true))
                    {
                        foreach (var empId in employerIds)
                        {
                            // 1. Fetch Employer Name safely
                            var employer = await db.QueryFirstOrDefaultAsync<dynamic>(
                                "SELECT [name] FROM [Employer] WHERE [id] = @Id", new { Id = empId });

                            string rawName = employer?.name ?? empId.ToString();
                            string folderName = SanitizeFileName(rawName);

                            if (!employerNamesList.Contains(rawName))
                            {
                                employerNamesList.Add(rawName);
                            }

                            // =====================================================
                            // 1. AUTOMATIC EMPLOYER FORM (1094) GENERATION
                            // =====================================================
                            
                            bool include1094 = formType.Contains("1094");
                            bool include1095 = formType.Contains("1095");
                            string transmittalForm = formType.Contains("-C") ? "1094-C" : "1094-B";

                            if (include1094 && employerFormStrategies.TryGetValue(transmittalForm, out var generateEmployerPdf))
                            {
                                byte[] employerPdf = await generateEmployerPdf(empId, year);
                                if (employerPdf != null)
                                {
                                    string fileName = $"{folderName}/{transmittalForm}_Employer_{empId}.pdf";
                                    var entry = archive.CreateEntry(fileName);
                                    using var stream = entry.Open();
                                    await stream.WriteAsync(employerPdf, 0, employerPdf.Length);
                                }
                            }

                            // User strictly asked ONLY for 1094 forms, so skip employee loop
                            if (!include1095)
                            {
                                continue;
                            }

                            // =====================================================
                            // 2. Employee Forms (1095 only)
                            // =====================================================
                            string sql;

                            if (employeeIds != null && employeeIds.Any())
                            {
                                sql = @"SELECT [id], [firstName], [lastName] FROM [Employee] 
               WHERE [EmployerId] = @EmployerId 
               AND [id] IN @Ids 
               AND [IsDeleted] = 0";
                            }
                            else
                            {
                                sql = @"SELECT [id], [firstName], [lastName] FROM [Employee] 
               WHERE [EmployerId] = @EmployerId 
               AND [IsDeleted] = 0";
                            }

                            var employees = await db.QueryAsync<dynamic>(sql, new { EmployerId = empId, Ids = employeeIds });

                            // PARALLEL LOGIC 
                            object zipLock = new object();
                            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 };

                            await Parallel.ForEachAsync(employees, parallelOptions, async (emp, cancellationToken) =>
                            {
                                try
                                {
                                    string currentForm = formType.Contains("-C") ? "1095-C" : "1095-B";

                                    if (employeeFormStrategies.TryGetValue(currentForm, out var generateEmployeePdf))
                                    {
                                        byte[] pdfBytes = await generateEmployeePdf((int)emp.id, year, suppressSSN);

                                        if (pdfBytes != null)
                                        {
                                            string fileName = $"{folderName}/{currentForm}_{emp.lastName}_{emp.firstName}_{emp.id}.pdf";

                                            // ZIP multiple threads 
                                            lock (zipLock)
                                            {
                                                var entry = archive.CreateEntry(fileName);
                                                using var stream = entry.Open();
                                                stream.Write(pdfBytes, 0, pdfBytes.Length);
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    failedEmployeeIds.Add((int)emp.id);
                                }
                            });
                        }
                    }
                } // azureStream is disposed here, file is completely saved to Azure!

                // =====================================================
                // 3. Save History to DB
                // =====================================================
                string joinedEmployerNames = string.Join(", ", employerNamesList);

                // Get final size directly from Azure blob properties
                var blobProperties = await blobClient.GetPropertiesAsync();
                long generatedSizeInBytes = blobProperties.Value.ContentLength;

                string insertHistorySql = @"
INSERT INTO [GenerateMultiEmployerBatchHistory]
([FileName], [EmployerNames], [FormType], [TaxYear], [FileSizeBytes], [GeneratedBy], [GeneratedDate])
VALUES
(@FileName, @EmployerNames, @FormType, @TaxYear, @FileSizeBytes, @GeneratedBy, GETDATE());
SELECT CAST(SCOPE_IDENTITY() AS INT);";

                int batchHistoryId = await db.ExecuteScalarAsync<int>(insertHistorySql, new
                {
                    FileName = zipName,
                    EmployerNames = joinedEmployerNames,
                    FormType = formType,
                    TaxYear = year,
                    FileSizeBytes = generatedSizeInBytes,
                    GeneratedBy = userId
                });

                // Record which employers this archive actually contains.
                //
                // GenerateMultiEmployerBatchHistoryEmployers was created with an index
                // and a foreign key but nothing ever wrote to it, so it held no rows.
                // Three features read it and therefore never worked:
                //   - ACAController.DownloadForms always answered "no batch found"
                //   - FilingService left HasBatch false on every row
                //   - the downloads hub had no way to tell whose archive a file was,
                //     which is why it listed every file to every user
                // EmployerNames above is a display string; this is the queryable link.
                if (batchHistoryId > 0 && employerIds is { Count: > 0 })
                {
                    const string insertLinkSql = @"
INSERT INTO [GenerateMultiEmployerBatchHistoryEmployers] ([BatchHistoryId], [EmployerId], [TaxYear])
VALUES (@BatchHistoryId, @EmployerId, @TaxYear);";

                    await db.ExecuteAsync(insertLinkSql,
                        employerIds.Distinct()
                                   .Where(id => id > 0)
                                   .Select(id => new
                                   {
                                       BatchHistoryId = batchHistoryId,
                                       EmployerId = id,
                                       TaxYear = year
                                   }));
                }

                if (employerIds is { Count: > 0 })
                {
                    const string upsertStatusSql = @"
MERGE [EmployerFilingStatus] AS target
USING (SELECT @EmployerId AS EmpId, @TaxYear AS Yr) AS source
ON (target.EmployerId = source.EmpId AND target.TaxYear = source.Yr)
WHEN MATCHED AND target.IsLocked = 0 THEN
    UPDATE SET FilingStatusId = 1, LastGeneratedDate = GETUTCDATE()
WHEN NOT MATCHED THEN
    INSERT (EmployerId, TaxYear, FilingStatusId, IsLocked, LastGeneratedDate)
    VALUES (source.EmpId, source.Yr, 1, 0, GETUTCDATE());";

                    await db.ExecuteAsync(upsertStatusSql,
                        employerIds.Distinct()
                                   .Where(id => id > 0)
                                   .Select(id => new
                                   {
                                       EmployerId = id,
                                       TaxYear = year,
                                       Description = $"{formType} forms generated ({zipName}).",
                                       UserId = userId
                                   }));
                }

                // =====================================================
                // 4. Dynamic Notification (Success or Partial Success)
                // =====================================================
                string title = failedEmployeeIds.IsEmpty ? "Batch Complete" : "Batch Completed with Errors";
                string message = failedEmployeeIds.IsEmpty
                    ? "All forms generated and uploaded directly to Azure successfully."
                    : $"Zip created in Azure. But {failedEmployeeIds.Count} forms failed to generate.";
                string statusType = failedEmployeeIds.IsEmpty ? "success" : "warning";

                await _notificationService.AddNotificationAsync(
                    userId,
                    title,
                    message,
                    statusType,
                    "/Downloads");
            }
            catch (Exception ex)
            {

                await blobClient.DeleteIfExistsAsync();

                await _notificationService.AddNotificationAsync(
                    userId,
                    "Batch Failed",
                    ex.Message,
                    "danger");
            }
        }





        public async Task<byte[]> GenerateEmployeesPreviewAsync(
            List<int> employeeIds,
            List<int> employerIds, // ? Changed to List<int>
            int year,
            string formType, bool suppressSSN = false)
        {
            var pdfList = new List<byte[]>();
            bool include1094 = formType.Contains("1094");
            bool include1095 = formType.Contains("1095");

            // =====================================================
            // HANDLE 1094 PREVIEW (Loops through Employers)
            // =====================================================
            if (include1094)
            {
                foreach (var empId in employerIds)
                {
                    try
                    {
                        byte[] pdfBytes = null;

                        if (formType.Contains("-C"))
                            pdfBytes = await Generate1094CForEmployerAsync(empId, year);
                        else if (formType.Contains("-B"))
                            pdfBytes = await Generate1094BForEmployerAsync(empId, year);

                        if (pdfBytes != null)
                            pdfList.Add(pdfBytes);
                    }
                    catch
                    {
                        continue;
                    }
                }
            }

            // =====================================================
            // 1095 PREVIEW (Loops through Employees)
            // =====================================================
            if (include1095)
            {
                foreach (var empId in employeeIds)
                {
                    try
                    {
                        byte[] pdfBytes = null;

                        // Updated from Switch case to Contains to support combined strings like "1094-C_1095-C"
                        if (formType.Contains("-C"))
                        {
                            pdfBytes = await Generate1095CForEmployeeAsync(empId, year, suppressSSN);
                        }
                        else if (formType.Contains("-B"))
                        {
                            pdfBytes = await Generate1095BForEmployeeAsync(empId, year, suppressSSN);
                        }

                        if (pdfBytes != null)
                            pdfList.Add(pdfBytes);
                    }
                    catch
                    {
                        continue;
                    }
                }
            }

            if (!pdfList.Any())
                throw new Exception("No preview files generated. Please check data.");

            return _mergeService.MergePdfs(pdfList);
        }


        private string MaskSSN(string ssn, bool suppress)
        {
            if (string.IsNullOrWhiteSpace(ssn)) return "";
            if (!suppress) return ssn; // Unchecked? Return original.

            var clean = new string(ssn.Where(char.IsDigit).ToArray());
            if (clean.Length == 9)
            {
                return $"XXX-XX-{clean.Substring(5)}"; // Checked? Return Masked.
            }
            return ssn;
        }
    }
}