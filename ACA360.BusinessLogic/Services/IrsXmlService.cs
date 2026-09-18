using ACA360.Core.IrsSchemas;
using ACA360.Core.Models;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using ACA360.BusinessLogic.Interfaces;

namespace ACA360.BusinessLogic.Services
{
    public class IrsXmlService : IIrsXmlService
    {
        private readonly string _connectionString;
        private readonly IWebHostEnvironment _env;
        private readonly IEmployerService _employerService;
        private readonly IACALogicService _acaLogic;

        // IRS Namespaces (These must be EXACT)
        private readonly XNamespace irs = "urn:us:gov:treasury:irs:common";
        private readonly XNamespace air7 = "urn:us:gov:treasury:irs:ext:aca:air:7.0"; // Check current version (likely 7.0 for 2024/2025)

        public IrsXmlService(
            string connectionString,
            IWebHostEnvironment env,
            IEmployerService employerService,
            IACALogicService acaLogic)
        {
            _connectionString = connectionString;
            _env = env;
            _employerService = employerService;
            _acaLogic = acaLogic;
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        /// <summary>
        /// Orchestrates the full IRS submission file generation process for a given
        /// employer and tax year. Loads the active transmitter configuration, detects
        /// whether the employer files on 1094-B/1095-B or 1094-C/1095-C forms,
        /// generates the appropriate form-data XML, generates the companion manifest
        /// XML (always flagged as Correction in this flow), persists a new
        /// <see cref="IrsSubmissionLog"/> record, and returns that log to the caller.
        /// Throws if no transmitter configuration is active or if any downstream
        /// generation step fails.
        /// </summary>
        /// <param name="employerId">The numeric employer ID to generate files for.</param>
        /// <param name="year">The ACA tax year being filed.</param>
        /// <param name="userId">The ID of the user triggering the generation, stored in the submission log.</param>
        /// <returns>The newly created <see cref="IrsSubmissionLog"/> record.</returns>
        public async Task<IrsSubmissionLog> GenerateSubmissionFilesAsync(int employerId, int year, string userId)
        {
            try
            {
                var config = await GetTransmitterConfigAsync();
                if (config == null) throw new Exception("Transmitter Configuration missing.");

                // 1. DETERMINE FORM TYPE
                // (You might pass this as a parameter, or detect it from Employer settings)
                // For now, let's assume we check the Employer's "FormType" property
                var employer = await _employerService.GetEmployerDetailsByIdAsync(employerId.ToString());
                string formType = (employer.FormType == "1094-B") ? "1094B_1095B" : "1094C_1095C";

                Guid transmissionId = Guid.NewGuid();
                string formXmlPath = "";
                int recordCount = 0;

                // 2. GENERATE DATA
                if (formType == "1094B_1095B")
                {
                    (formXmlPath, recordCount) = await GenerateFormDataXmlForBFormsAsync(employerId, year, transmissionId, config);
                }
                else
                {
                    (formXmlPath, recordCount) = await GenerateFormDataXmlAsync(employerId, year, transmissionId, config);
                }

                // 3. GENERATE MANIFEST (Common for both)
                // Pass 'true' for Correction
                // 3. GENERATE MANIFEST (Common for both)
                string manifestXmlPath = await GenerateManifestXmlAsync(
                    formXmlPath, transmissionId, recordCount, config,
                    isCorrection: false,        // original transmission
                    taxYear: year,
                    formType: formType);

                // 4. Check if a submission already exists for this Employer + Year
                var existing = await GetSubmissionByEmployerYearAsync(employerId, year);

                IrsSubmissionLog log;
                if (existing != null)
                {
                    // UPDATE existing row instead of inserting a duplicate
                    await UpdateSubmissionFilesAsync(existing.SubmissionId, formType, transmissionId,
                        manifestXmlPath, formXmlPath, recordCount, userId);

                    existing.FormType = formType;
                    existing.TransmissionId = transmissionId;
                    existing.ManifestFilePath = manifestXmlPath;
                    existing.FormFilePath = formXmlPath;
                    existing.RecordCount = recordCount;
                    existing.StatusId = 0;
                    log = existing;
                }
                else
                {
                    // 4. Log. Always a new row: each transmission has its own
                    // TransmissionId and its own IRS Receipt ID, and corrections mean
                    // an employer/year legitimately has several. See tracking doc
                    // rows 108-110.
                    log = new IrsSubmissionLog
                    {
                        EmployerId = employerId,
                        TaxYear = year,
                        FormType = formType,
                        TransmissionId = transmissionId,
                        ManifestFilePath = manifestXmlPath,
                        FormFilePath = formXmlPath,
                        RecordCount = recordCount,
                        StatusId = 0,
                        GeneratedBy = userId,
                        SubmissionType = "O"
                    };
                    await SaveSubmissionLogAsync(log);
                }

                return log;
            }
            catch (SqlException ex)
            {
                throw new Exception($"Database error generating submission files for EmployerId {employerId}, Year {year}.", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Unexpected error generating submission files for EmployerId {employerId}, Year {year}.", ex);
            }
        }

        /// <summary>
        /// Builds and serialises the 1094-B/1095-B form-data XML file for an employer.
        /// Loads employer details and the full list of active employees, constructs a
        /// <see cref="Form1094BUpstreamDetail"/> root object with nested
        /// <see cref="Form1095BUpstreamDetail"/> entries (including covered-individual
        /// lists sourced from <see cref="IACALogicService.GetCoveredIndividualsAsync"/>),
        /// and writes the result to the employer's IrsSubmissions folder on disk.
        /// Returns the on-disk path and the total employee count.
        /// </summary>
        /// <param name="employerId">The numeric employer ID.</param>
        /// <param name="year">The ACA tax year being filed.</param>
        /// <param name="transmissionId">The GUID used as the submission ID in the XML header.</param>
        /// <param name="config">The active transmitter configuration supplying TCC, company name, etc.</param>
        /// <returns>A tuple of the full file path and the total number of 1095-B records written.</returns>
        private async Task<(string Path, int Count)> GenerateFormDataXmlForBFormsAsync(int employerId, int year, Guid transmissionId, TransmitterConfig config)
        {
            try
            {
                // 1. Fetch Data
                var employer = await _employerService.GetEmployerDetailsByIdAsync(employerId.ToString());

                // For B-Forms, we iterate ALL active employees (or specific list)
                // We can reuse GetEmployeeCodesListAsync just to get the list of people
                var employees = await _acaLogic.GetEmployeeCodesListAsync(employerId, year);
                employees = employees.Where(e => e.GetForm == 1).ToList();
                // 2. Build Root 1094-B Object
                var form1094B = new Form1094BUpstreamDetail
                {
                    SubmissionId = transmissionId.ToString(),
                    TaxYr = year.ToString(),
                    CorrectedInd = "0",
                    BusinessName = new BusinessNameType { BusinessNameLine1 = employer.Name },
                    BusinessEIN = employer.TaxId?.Replace("-", "") ?? "000000000",
                    BusinessAddressGroup = new AddressType
                    {
                        AddressLine1Txt = employer.Address1,
                        CityNm = employer.City,
                        USStateCd = employer.State,
                        USZIPCd = employer.ZipCode?.Replace("-", "") ?? "00000"
                    },
                    ContactName = new PersonNameType
                    {
                        FirstNm = employer.ContactName?.Split(' ')[0] ?? "Admin",
                        LastNm = employer.ContactName?.Split(' ').LastOrDefault() ?? "User"
                    },
                    ContactPhoneNum = employer.PhoneNumber?.Replace("-", "").Replace("(", "").Replace(")", "").Replace(" ", ""),
                    TotalForm1095BSubmittedCnt = employees.Count
                };

                // 3. Build 1095-B List
                foreach (var emp in employees)
                {
                    var form1095B = new Form1095BUpstreamDetail
                    {
                        RecordId = emp.EmployeeId.ToString(),
                        CorrectedInd = "0",

                        // Part I: Responsible Individual (The Employee)
                        ResponsibleIndividual = new ResponsibleIndividualType
                        {
                            PersonName = new PersonNameType { FirstNm = emp.FirstName, LastNm = emp.LastName },
                            TIN = emp.SSN?.Replace("-", ""),
                            //Address = new AddressType
                            //{
                            //    AddressLine1Txt = emp.Address1,
                            //    CityNm = emp.City,
                            //    USStateCd = emp.State,
                            //    USZIPCd = emp.Zip?.Replace("-", "")
                            //},
                            OriginOfHealthCoverageCd = "B" // Code B = Employer-Sponsored
                        },

                        // Part II: Employer Info (Repeated for self-insured)
                        EmployerSponsoredCoverage = new EmployerSponsoredCoverageType
                        {
                            EmployerName = new BusinessNameType { BusinessNameLine1 = employer.Name },
                            EmployerEIN = employer.TaxId?.Replace("-", ""),
                            EmployerAddress = new AddressType
                            {
                                AddressLine1Txt = employer.Address1,
                                CityNm = employer.City,
                                USStateCd = employer.State,
                                USZIPCd = employer.ZipCode?.Replace("-", "")
                            }
                        },

                        CoveredIndividuals = new List<CoveredIndividualType>()
                    };

                    // Part IV: Covered Individuals (Using existing logic!)
                    var covered = await _acaLogic.GetCoveredIndividualsAsync(emp.EmployeeId, year);

                    foreach (var c in covered)
                    {
                        var ciXml = new CoveredIndividualType
                        {
                            PersonName = new PersonNameType { FirstNm = c.FirstName, LastNm = c.LastName },
                            SSN = c.SSN?.Replace("-", ""),
                            BirthDt = c.Birthday?.ToString("yyyy-MM-dd"),
                            CoveredIndicator = c.AllM ? "1" : "0"
                        };

                        // If not all 12, map individual months
                        if (!c.AllM)
                        {
                            ciXml.JanInd = c.Jan ? "1" : "0";
                            ciXml.FebInd = c.Feb ? "1" : "0";
                            ciXml.MarInd = c.Mar ? "1" : "0";
                            ciXml.AprInd = c.Apr ? "1" : "0";
                            ciXml.MayInd = c.May ? "1" : "0";
                            ciXml.JunInd = c.Jun ? "1" : "0";
                            ciXml.JulInd = c.Jul ? "1" : "0";
                            ciXml.AugInd = c.Aug ? "1" : "0";
                            ciXml.SepInd = c.Sep ? "1" : "0";
                            ciXml.OctInd = c.Oct ? "1" : "0";
                            ciXml.NovInd = c.Nov ? "1" : "0";
                            ciXml.DecInd = c.Dec ? "1" : "0";
                        }

                        form1095B.CoveredIndividuals.Add(ciXml);
                    }

                    form1094B.Form1095Bs.Add(form1095B);
                }

                // 4. Serialize
                string folder = Path.Combine(_env.ContentRootPath, "IrsSubmissions", employerId.ToString());
                Directory.CreateDirectory(folder);
                string fileName = $"1094B_Request_{config.TCC}_{DateTime.UtcNow:yyyyMMdd}T{DateTime.UtcNow:HHmmss}000Z.xml";
                string fullPath = Path.Combine(folder, fileName);

                var serializer = new XmlSerializer(typeof(Form1094BUpstreamDetail));
                var ns = new XmlSerializerNamespaces();
                ns.Add("urn", "urn:us:gov:treasury:irs:ext:aca:air:7.0");
                ns.Add("irs", "urn:us:gov:treasury:irs:common");

                using (var writer = System.Xml.XmlWriter.Create(fullPath, new System.Xml.XmlWriterSettings { Indent = true }))
                {
                    serializer.Serialize(writer, form1094B, ns);
                }

                return (fullPath, employees.Count);
            }
            catch (SqlException ex)
            {
                throw new Exception($"Database error generating 1094-B/1095-B XML for EmployerId {employerId}, Year {year}.", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Unexpected error generating 1094-B/1095-B XML for EmployerId {employerId}, Year {year}.", ex);
            }
        }

        /// <summary>
        /// Processes an IRS acknowledgement (ACK) file upload for a previously
        /// generated submission. Validates the file, saves it alongside the original
        /// submission files, deserialises the IRS ACK XML into an
        /// <see cref="IrsAckResponse"/> object, derives the new status ID from
        /// the IRS transmission status code ("Accepted" → 2, "AcceptedWithErrors" → 3,
        /// "Rejected" → 4), and persists the updated status, receipt ID, and ACK
        /// file path to the submission log via <c>sp_UpdateSubmissionAckStatus</c>.
        /// A transaction guards the UPDATE so it can be rolled back on failure.
        /// Throws a descriptive exception if the file is empty, the submission record
        /// cannot be found, or the ACK XML cannot be deserialised.
        /// </summary>
        /// <param name="submissionId">The ID of the submission record being acknowledged.</param>
        /// <param name="file">The IRS ACK XML file uploaded by the user.</param>
        /// <returns><c>true</c> on success.</returns>
        public async Task<bool> ProcessAckFileAsync(int submissionId, IFormFile file)
        {
            try
            {
                // 1. Validate
                if (file == null || file.Length == 0) throw new Exception("Invalid file.");

                // 2. Get Submission Record
                var submission = await GetSubmissionByIdAsync(submissionId);
                if (submission == null) throw new Exception("Submission not found.");

                // 3. Save ACK File to Disk
                string folder = Path.GetDirectoryName(submission.ManifestFilePath); // Keep with original files
                string fileName = $"ACK_{DateTime.Now:yyyyMMddHHmmss}_{file.FileName}";
                string fullPath = Path.Combine(folder, fileName);

                using (var stream = new FileStream(fullPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                // 4. Parse XML
                IrsAckResponse ackData;
                var serializer = new XmlSerializer(typeof(IrsAckResponse));

                using (var stream = File.OpenRead(fullPath))
                {
                    try
                    {
                        ackData = (IrsAckResponse)serializer.Deserialize(stream);
                    }
                    catch
                    {
                        throw new Exception("Invalid IRS ACK Format. Ensure this is the correct XML file from the AIR system.");
                    }
                }

                // 5. Determine Status
                // IRS Codes:
                // Accepted = "Accepted"
                // Accepted with Errors = "AcceptedWithErrors"
                // Rejected = "Rejected"
                // Processing = "Processing" (Rare for final ACK)

                int newStatusId = 1; // Default Transmitted
                string statusMsg = "ACK Processed";

                string irsStatus = ackData.Detail.TransmissionStatusCd;

                if (irsStatus == "Accepted") newStatusId = 2;
                else if (irsStatus == "AcceptedWithErrors") newStatusId = 3;
                else if (irsStatus == "Rejected")
                {
                    newStatusId = 4;
                    statusMsg = "Rejected: " + (ackData.Detail.TransmitErrorDetail?.ErrorMessageTxt ?? "Unknown Error");
                }

                // 6. Update Database — replaced inline UPDATE with sp_UpdateSubmissionAckStatus
                using var db = Connection;
                db.Open();
                using var transaction = db.BeginTransaction();
                try
                {
                    await db.ExecuteAsync(
                        "sp_UpdateSubmissionAckStatus",
                        new
                        {
                            Status = newStatusId,
                            Msg = statusMsg,
                            Receipt = ackData.Detail.ReceiptId,
                            Path = fullPath,
                            Id = submissionId
                        },
                        transaction: transaction,
                        commandType: CommandType.StoredProcedure);

                    transaction.Commit();
                }
                catch (Exception)
                {
                    transaction.Rollback();
                    throw;
                }

                return true;
            }
            catch (SqlException ex)
            {
                throw new Exception($"Database error processing ACK file for SubmissionId {submissionId}.", ex);
            }
            catch (Exception ex) when (
                ex.Message != "Invalid file." &&
                ex.Message != "Submission not found." &&
                ex.Message != "Invalid IRS ACK Format. Ensure this is the correct XML file from the AIR system.")
            {
                throw new Exception($"Unexpected error processing ACK file for SubmissionId {submissionId}.", ex);
            }
        }

        /// <summary>
        /// Processes an IRS error detail file upload for a previously generated
        /// submission. Saves the file to disk, parses the error XML using
        /// <see cref="XDocument"/> to accommodate namespace variations across IRS
        /// schema versions, clears any existing error records for the submission via
        /// <c>sp_DeleteSubmissionErrors</c>, inserts each extracted error record via
        /// <c>sp_InsertSubmissionError</c>, and updates the submission status to
        /// "Accepted with Errors" (3) via <c>sp_UpdateSubmissionStatus</c>. All three
        /// database operations execute within a single transaction so partial writes
        /// cannot leave the error table in an inconsistent state.
        /// </summary>
        /// <param name="submissionId">The ID of the submission whose errors are being loaded.</param>
        /// <param name="file">The IRS error detail XML file uploaded by the user.</param>
        /// <returns><c>true</c> on success.</returns>
        public async Task<bool> ProcessErrorFileAsync(int submissionId, IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0) throw new Exception("Invalid file.");

                // 1. Save File
                var submission = await GetSubmissionByIdAsync(submissionId);
                string folder = Path.GetDirectoryName(submission.ManifestFilePath);
                string fileName = $"ERRORS_{DateTime.Now:yyyyMMddHHmmss}_{file.FileName}";
                string fullPath = Path.Combine(folder, fileName);

                using (var stream = new FileStream(fullPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                // 2. Parse XML (Using XDocument for flexibility with namespaces)
                // IRS Error structure typically: <TransmitterErrorDetail> or <RecordErrorDetail>
                var doc = XDocument.Load(fullPath);
                XNamespace ns = doc.Root.Name.Namespace;

                // Find all Record Errors
                var errors = doc.Descendants(ns + "TransmitterErrorDetailPair");
                // Note: The tag name varies slightly by schema version.
                // Often it is inside <Acknowledgement> -> <ReportLevelStatus> or <RecordLevelStatus>

                using var db = Connection;
                if (db.State != System.Data.ConnectionState.Open) db.Open();
                using var trans = db.BeginTransaction();

                try
                {
                    // Clear old errors for this submission — replaced inline DELETE with sp_DeleteSubmissionErrors
                    await db.ExecuteAsync(
                        "sp_DeleteSubmissionErrors",
                        new { Id = submissionId },
                        transaction: trans,
                        commandType: CommandType.StoredProcedure);

                    // 3. Extract and Save Errors
                    foreach (var err in doc.Descendants().Where(e => e.Name.LocalName.Contains("ErrorDetail")))
                    {
                        // Try to find the RecordID (EmployeeId) associated with this error
                        // IRS structure: <RecordID>105</RecordID> ... <ErrorMessageTxt>...</ErrorMessageTxt>
                        var parent = err.Parent;
                        string recordId = parent?.Descendants().FirstOrDefault(e => e.Name.LocalName == "RecordID")?.Value;
                        string code = err.Descendants().FirstOrDefault(e => e.Name.LocalName == "ErrorMessageCd")?.Value;
                        string msg = err.Descendants().FirstOrDefault(e => e.Name.LocalName == "ErrorMessageTxt")?.Value;

                        if (!string.IsNullOrEmpty(msg))
                        {
                            // Replaced inline INSERT with sp_InsertSubmissionError stored procedure
                            await db.ExecuteAsync(
                                "sp_InsertSubmissionError",
                                new
                                {
                                    SubId = submissionId,
                                    RecId = recordId ?? "0",
                                    Code = code ?? "UNKNOWN",
                                    Msg = msg
                                },
                                transaction: trans,
                                commandType: CommandType.StoredProcedure);
                        }
                    }

                    // Update Status to 'Accepted with Errors' (3) or 'Rejected' (4)
                    // Replaced inline UPDATE with sp_UpdateSubmissionStatus stored procedure
                    await db.ExecuteAsync(
                        "sp_UpdateSubmissionStatus",
                        new { Id = submissionId, StatusId = 3, StatusMessage = "Errors Processed" },
                        transaction: trans,
                        commandType: CommandType.StoredProcedure);

                    trans.Commit();
                }
                catch (Exception)
                {
                    trans.Rollback();
                    throw;
                }

                return true;
            }
            catch (SqlException ex)
            {
                throw new Exception($"Database error processing error file for SubmissionId {submissionId}.", ex);
            }
            catch (Exception ex) when (ex.Message != "Invalid file.")
            {
                throw new Exception($"Unexpected error processing error file for SubmissionId {submissionId}.", ex);
            }
        }

        /// <summary>
        /// Returns all IRS error records stored for a given submission via
        /// <c>sp_GetSubmissionErrors</c>. Used to populate the error grid on the
        /// submission detail page after an error file has been processed.
        /// Returns an empty list on error rather than propagating an exception.
        /// </summary>
        /// <param name="submissionId">The ID of the submission whose errors should be retrieved.</param>
        /// <returns>A <see cref="List{IrsSubmissionErrorDto}"/> of error records, or an empty list on error.</returns>
        public async Task<List<IrsSubmissionErrorDto>> GetSubmissionErrorsAsync(int submissionId)
        {
            try
            {
                // Replaced inline SQL with sp_GetSubmissionErrors stored procedure
                using var db = Connection;
                return (await db.QueryAsync<IrsSubmissionErrorDto>(
                    "sp_GetSubmissionErrors",
                    new { Id = submissionId },
                    commandType: CommandType.StoredProcedure)).ToList();
            }
            catch (SqlException ex)
            {
                throw new Exception($"Database error retrieving errors for SubmissionId {submissionId}.", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Unexpected error retrieving errors for SubmissionId {submissionId}.", ex);
            }
        }

        /// <summary>
        /// Generates a correction submission for a previously filed submission by
        /// identifying the employee records that received errors, re-generating the
        /// form-data XML for only those employees with the correction flag set
        /// (<c>CorrectedInd = "1"</c>), generating a new companion manifest, and
        /// persisting a new <see cref="IrsSubmissionLog"/> record linked by a
        /// descriptive status message to the original submission ID.
        /// Throws if no error records are found or if any downstream step fails.
        /// </summary>
        /// <param name="originalSubmissionId">The ID of the original submission that contained errors.</param>
        /// <param name="userId">The ID of the user triggering the correction, stored in the new submission log.</param>
        /// <returns>The newly created correction <see cref="IrsSubmissionLog"/> record.</returns>
        public async Task<IrsSubmissionLog> GenerateCorrectionFilesAsync(int originalSubmissionId, string userId)
        {
            try
            {
                // 1. Get Original Info
                var original = await GetSubmissionByIdAsync(originalSubmissionId);
                var config = await GetTransmitterConfigAsync();

                // 2. Identify the Bad Records
                var errors = await GetSubmissionErrorsAsync(originalSubmissionId);
                var badEmployeeIds = errors
                    .Where(e => int.TryParse(e.RecordId, out _))
                    .Select(e => int.Parse(e.RecordId))
                    .Distinct()
                    .ToList();

                if (!badEmployeeIds.Any()) throw new Exception("No records found to correct.");

                // 3. Generate XML (Correction Mode)
                Guid transmissionId = Guid.NewGuid();
                string folder = Path.Combine(_env.ContentRootPath, "IrsSubmissions", original.EmployerId.ToString());

                // --- GENERATE DATA XML (CORRECTION LOGIC) ---
                // We reuse the existing generator but need to filter the employees
                // NOTE: You need to modify GenerateFormDataXmlAsync to accept a list of IDs and a "IsCorrection" flag
                // For this example, we assume we updated GenerateFormDataXmlAsync signature:

                var (formPath, count) = await GenerateFormDataXmlAsync(
                    original.EmployerId,
                    original.TaxYear,
                    transmissionId,
                    config,
                    badEmployeeIds, // <--- Only these people
                    isCorrection: true // <--- Trigger "C" flag
                );

                // 4. Generate Manifest (Same as before, just wraps the new file)
                // Pass 'false' for Original
                // 4. Generate Manifest
                string manifestFilePath = await GenerateManifestXmlAsync(
                    formPath, transmissionId, count, config,
                    isCorrection: true,         // correction transmission
                    taxYear: original.TaxYear,
                    formType: original.FormType);

                // 5. Log as New Submission (Linked logic can be added later)
                var log = new IrsSubmissionLog
                {
                    EmployerId = original.EmployerId,
                    TaxYear = original.TaxYear,
                    FormType = original.FormType,
                    TransmissionId = transmissionId,
                    ManifestFilePath = manifestFilePath,
                    FormFilePath = formPath,
                    RecordCount = count,
                    StatusId = 0,
                    GeneratedBy = userId,
                    StatusMessage = $"Correction for Submission #{originalSubmissionId}"
                };

                await SaveSubmissionLogAsync(log);
                return log;
            }
            catch (SqlException ex)
            {
                throw new Exception($"Database error generating correction files for SubmissionId {originalSubmissionId}.", ex);
            }
            catch (Exception ex) when (ex.Message != "No records found to correct.")
            {
                throw new Exception($"Unexpected error generating correction files for SubmissionId {originalSubmissionId}.", ex);
            }
        }

        /// <summary>
        /// Builds and saves the IRS AIR manifest XML file that accompanies the
        /// form-data XML during a submission. Computes the SHA-256 checksum and
        /// byte size of the form-data file, constructs the
        /// <c>ACAUIBusinessHeader / ACATransmitterManifestReqDtl</c> element tree
        /// per IRS Pub 5165 using the <see cref="XDocument"/> API, and writes the
        /// result to the same folder as the form-data file. The
        /// <paramref name="isCorrection"/> flag controls whether the
        /// <c>TransmissionTypeCd</c> element is set to "C" (correction) or "O"
        /// (original).
        /// </summary>
        /// <param name="dataFilePath">Full on-disk path to the form-data XML file.</param>
        /// <param name="transmissionId">The GUID identifying this transmission.</param>
        /// <param name="recordCount">Total number of payee records in the data file.</param>
        /// <param name="config">Active transmitter configuration (TCC, EIN, contact info, software ID).</param>
        /// <param name="isCorrection"><c>true</c> to set TransmissionTypeCd = "C"; <c>false</c> for "O".</param>
        /// <returns>The full on-disk path of the saved manifest XML file.</returns>
        private async Task<string> GenerateManifestXmlAsync(string dataFilePath,Guid transmissionId,int recordCount,TransmitterConfig config,bool isCorrection,int taxYear,string formType)
        {
            try
            {
                // A. Calculate Checksum & Size of the Data File
                var fileInfo = new FileInfo(dataFilePath);
                long fileSize = fileInfo.Length;
                string checksum = ComputeSha256Checksum(dataFilePath);

                // B. Build XML (Using XDocument for clean structure)
                // Note: The specific structure below is based on IRS Pub 5165
                var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null),
                    new XElement(irs + "ACAUIBusinessHeader",
                        new XAttribute(XNamespace.Xmlns + "irs", irs.NamespaceName),
                        new XAttribute(XNamespace.Xmlns + "urn", air7.NamespaceName),

                        new XElement(air7 + "ACATransmitterManifestReqDtl",
                            // The tax year being reported, NOT the year the file is
                            // transmitted. TY2025 forms are filed in 2026.
                            new XElement(air7 + "PaymentYr", taxYear.ToString()),
                            // season currently being processed. Confirm before relying on it.
                            new XElement(air7 + "PriorYearDataInd",
                                taxYear < DateTime.Now.Year - 1 ? "1" : "0"),
                            new XElement(air7 + "EIN", config.TransmitterEIN),
                            new XElement(air7 + "TransmissionTypeCd", isCorrection ? "C" : "O"), // O=Original, C=Correction, R=Replacement
                            new XElement(air7 + "TestFileCd", config.Environment == "T" ? "T" : "P"), // T=Test, P=Production
                            new XElement(air7 + "TransmitterForeignEntityInd", "0"),

                            // Transmitter Info
                            new XElement(air7 + "TransmitterControlCd", config.TCC),
                            new XElement(air7 + "CompanyNm", config.CompanyName),
                            new XElement(air7 + "TransmitterContactGrp",
                                new XElement(air7 + "ContactNm", config.ContactName),
                                new XElement(air7 + "ContactPhoneNum", config.ContactPhone)
                            ),

                            // Vendor Info (Software ID)
                            new XElement(air7 + "VendorGrp",
                                new XElement(air7 + "VendorCd", "I"), // I = In-house or I = Independent? Check Pub 5165. Usually 'I' for Issuer.
                                new XElement(air7 + "ContactNm", config.ContactName),
                                new XElement(air7 + "ContactPhoneNum", config.ContactPhone)
                            ),

                            // File Attachment (The link to the data file)
                            new XElement(air7 + "TotalPayeeRecordCnt", recordCount),
                            new XElement(air7 + "TotalPayerRecordCnt", "1"), // One employer in this file
                            new XElement(air7 + "SoftwareId", config.SoftwareId),
                            // B-forms and C-forms share this method; the code must follow
                            // the payload, not be assumed.
                            new XElement(air7 + "FormTypeCd",
                                formType == "1094B_1095B" ? "1094/1095B" : "1094/1095C"),
                            // then set BuildUniqueTransmissionId accordingly.
                            new XElement(air7 + "UniqueTransmissionId",
                                BuildUniqueTransmissionId(transmissionId, config)),

                            new XElement(air7 + "BinaryFormatCd", "application/xml"),
                            new XElement(air7 + "ChecksumAugmentationNum", checksum),
                            new XElement(air7 + "AttachmentByteSizeNum", fileSize),
                            new XElement(air7 + "DocumentSystemFileNm", Path.GetFileName(dataFilePath))
                        )
                    )
                );

                // C. Save to Disk
                string folder = Path.GetDirectoryName(dataFilePath);
                // Naming convention: Manifest_TCC_Date_Time_0001.xml
                string fileName = $"Manifest_{config.TCC}_{DateTime.Now:yyyyMMdd}_{DateTime.Now:HHmmss}00.xml";
                string fullPath = Path.Combine(folder, fileName);

                doc.Save(fullPath);
                return fullPath;
            }
            catch (Exception ex)
            {
                throw new Exception($"Error generating manifest XML for transmission {transmissionId}.", ex);
            }
        }

        /// <summary>
        /// Returns the IRS submission dashboard view for a given tax year via
        /// <c>sp_GetIrsDashboard</c>. Each row represents a finalized employer
        /// (FilingStatusId = 2) enriched with the most recent submission record
        /// for that employer/year (if any). The <c>FormType</c> column falls back
        /// from the actual generated type → the employer's configured type →
        /// the 1094-C default. Results are ordered by generation date descending,
        /// then employer name ascending. Returns an empty list on error.
        /// </summary>
        /// <param name="year">The ACA tax year to display submissions for.</param>
        /// <returns>A <see cref="List{IrsDashboardItemDto}"/> for the dashboard grid.</returns>
        public async Task<List<IrsDashboardItemDto>> GetIrsDashboardAsync(int year)
        {
            try
            {
                // Replaced inline SQL with sp_GetIrsDashboard stored procedure
                using var db = Connection;
                var result = await db.QueryAsync<IrsDashboardItemDto>(
                    "sp_GetIrsDashboard",
                    new { Year = year },
                    commandType: CommandType.StoredProcedure);
                return result.ToList();
            }
            catch (SqlException ex)
            {
                throw new Exception($"Database error loading IRS dashboard for Year {year}.", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Unexpected error loading IRS dashboard for Year {year}.", ex);
            }
        }
        /// <summary>
        /// Builds the AIR UniqueTransmissionId.
        /// >>> VERIFY the format against IRS Publication 5165 before transmitting. <<<
        /// Kept as a single method so the format lives in exactly one place.
        /// </summary>
        private static string BuildUniqueTransmissionId(Guid transmissionId, TransmitterConfig config)
        {
            return $"{transmissionId}:SYS12:{config.TCC}::T";
        }
        /// <summary>
        /// Returns all submission log records for a given tax year, each joined
        /// to the employer's name and EIN, via <c>sp_GetSubmissionsByYear</c>.
        /// Results are ordered by generation date descending so the most recent
        /// submission appears first in the list view. Returns an empty enumerable
        /// on error.
        /// </summary>
        /// <param name="year">The ACA tax year to retrieve submissions for.</param>
        /// <returns>An <see cref="IEnumerable{IrsSubmissionLogDto}"/> of submission records.</returns>
        public async Task<IEnumerable<IrsSubmissionLogDto>> GetSubmissionsAsync(int year)
        {
            try
            {
                // Replaced inline SQL with sp_GetSubmissionsByYear stored procedure
                using var db = Connection;
                return await db.QueryAsync<IrsSubmissionLogDto>(
                    "sp_GetSubmissionsByYear",
                    new { Year = year },
                    commandType: CommandType.StoredProcedure);
            }
            catch (SqlException ex)
            {
                throw new Exception($"Database error retrieving submissions for Year {year}.", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Unexpected error retrieving submissions for Year {year}.", ex);
            }
        }

        /// <summary>
        /// Returns a single <see cref="IrsSubmissionLog"/> record by its primary
        /// key via <c>sp_GetSubmissionById</c>. Returns <c>null</c> when no
        /// matching record is found, which callers (e.g. ProcessAckFileAsync)
        /// treat as a "Submission not found" error condition.
        /// </summary>
        /// <param name="submissionId">The primary-key ID of the submission to retrieve.</param>
        /// <returns>The <see cref="IrsSubmissionLog"/> record, or <c>null</c> if not found.</returns>
        public async Task<IrsSubmissionLog> GetSubmissionByIdAsync(int submissionId)
        {
            try
            {
                // Replaced inline SQL with sp_GetSubmissionById stored procedure
                using var db = Connection;
                return await db.QuerySingleOrDefaultAsync<IrsSubmissionLog>(
                    "sp_GetSubmissionById",
                    new { Id = submissionId },
                    commandType: CommandType.StoredProcedure);
            }
            catch (SqlException ex)
            {
                throw new Exception($"Database error retrieving submission for SubmissionId {submissionId}.", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Unexpected error retrieving submission for SubmissionId {submissionId}.", ex);
            }
        }

        /// <summary>
        /// Updates the status ID and optional status message for a given submission
        /// log record via <c>sp_UpdateSubmissionStatus</c>. Used by the controller
        /// layer to manually advance a submission's status (e.g. mark as
        /// "Transmitted" after uploading to the AIR system). A transaction is used
        /// so the UPDATE can be rolled back on failure.
        /// </summary>
        /// <param name="submissionId">The ID of the submission to update.</param>
        /// <param name="statusId">The new numeric status ID.</param>
        /// <param name="message">A human-readable status message to store alongside the status ID.</param>
        public async Task UpdateStatusAsync(int submissionId, int statusId, string message)
        {
            try
            {
                // Replaced inline UPDATE with sp_UpdateSubmissionStatus stored procedure
                using var db = Connection;
                db.Open();
                using var transaction = db.BeginTransaction();
                try
                {
                    await db.ExecuteAsync(
                        "sp_UpdateSubmissionStatus",
                        new { Id = submissionId, StatusId = statusId, StatusMessage = message },
                        transaction: transaction,
                        commandType: CommandType.StoredProcedure);

                    transaction.Commit();
                }
                catch (Exception)
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (SqlException ex)
            {
                throw new Exception($"Database error updating status for SubmissionId {submissionId}.", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Unexpected error updating status for SubmissionId {submissionId}.", ex);
            }
        }

        // --- HELPERS ---

        /// <summary>
        /// Computes the SHA-256 checksum of the specified file and returns it as
        /// a lowercase hexadecimal string (no hyphens). This value is embedded in
        /// the manifest XML as <c>ChecksumAugmentationNum</c> per IRS Pub 5165
        /// so the IRS AIR system can verify file integrity on receipt.
        /// </summary>
        /// <param name="filePath">Full path to the file to checksum.</param>
        /// <returns>A 64-character lowercase hex SHA-256 hash string.</returns>
        private string ComputeSha256Checksum(string filePath)
        {
            try
            {
                using (var sha256 = SHA256.Create())
                using (var stream = File.OpenRead(filePath))
                {
                    byte[] hash = sha256.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error computing SHA-256 checksum for file '{filePath}'.", ex);
            }
        }

        /// <summary>
        /// Returns the single active transmitter configuration row from the
        /// <c>TransmitterConfig</c> table via <c>sp_GetActiveTransmitterConfig</c>.
        /// "Active" is defined as <c>IsActive = 1</c>; only the top row is
        /// returned in case multiple rows exist. Returns <c>null</c> when no active
        /// configuration is found, which causes <see cref="GenerateSubmissionFilesAsync"/>
        /// to throw a descriptive exception.
        /// </summary>
        /// <returns>The active <see cref="TransmitterConfig"/>, or <c>null</c> if none is configured.</returns>
        private async Task<TransmitterConfig> GetTransmitterConfigAsync()
        {
            try
            {
                // Replaced inline SQL with sp_GetActiveTransmitterConfig stored procedure
                using var db = Connection;
                return await db.QuerySingleOrDefaultAsync<TransmitterConfig>(
                    "sp_GetActiveTransmitterConfig",
                    commandType: CommandType.StoredProcedure);
            }
            catch (SqlException ex)
            {
                throw new Exception("Database error retrieving transmitter configuration.", ex);
            }
            catch (Exception ex)
            {
                throw new Exception("Unexpected error retrieving transmitter configuration.", ex);
            }
        }

        /// <summary>
        /// Inserts a new <see cref="IrsSubmissionLog"/> record into the database
        /// via <c>sp_SaveSubmissionLog</c>. Called after successful XML generation
        /// to create a permanent audit trail for every submission attempt. A
        /// transaction is used so the INSERT can be rolled back if the SP raises
        /// an error, preventing orphaned log entries with no corresponding files.
        /// </summary>
        /// <param name="log">The fully populated <see cref="IrsSubmissionLog"/> to insert.</param>
        private async Task SaveSubmissionLogAsync(IrsSubmissionLog log)
        {
            try
            {
                // Replaced inline INSERT with sp_SaveSubmissionLog stored procedure
                using var db = Connection;
                db.Open();
                using var transaction = db.BeginTransaction();
                try
                {
                    await db.ExecuteAsync(
                        "sp_SaveSubmissionLog",
                        new
                        {
                            log.EmployerId,
                            log.TaxYear,
                            log.FormType,
                            log.TransmissionId,
                            log.ManifestFilePath,
                            log.FormFilePath,
                            log.RecordCount,
                            log.StatusId,
                            log.GeneratedBy
                        },
                        transaction: transaction,
                        commandType: CommandType.StoredProcedure);

                    transaction.Commit();
                }
                catch (Exception)
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (SqlException ex)
            {
                throw new Exception($"Database error saving submission log for EmployerId {log.EmployerId}.", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Unexpected error saving submission log for EmployerId {log.EmployerId}.", ex);
            }
        }

        /// <summary>
        /// Builds and serialises the 1094-C/1095-C form-data XML file for an
        /// employer. Loads the 1094 detail record (certifications, monthly counts),
        /// the full aggregated member list, and either all employees or a filtered
        /// subset (when <paramref name="filterEmployeeIds"/> is provided for a
        /// correction run). Constructs the <see cref="Form1094CUpstreamDetail"/>
        /// root object, populates the 12-month coverage groups, builds a
        /// <see cref="Form1095CUpstreamDetail"/> entry per employee (with
        /// <c>CorrectedInd = "1"</c> when <paramref name="isCorrection"/> is true),
        /// and serialises the result to disk. Returns the on-disk path and record count.
        /// Throws if the employer record cannot be found.
        /// </summary>
        /// <param name="employerId">The numeric employer ID.</param>
        /// <param name="year">The ACA tax year being filed.</param>
        /// <param name="transmissionId">The GUID used as the submission ID in the XML header.</param>
        /// <param name="config">The active transmitter configuration.</param>
        /// <param name="filterEmployeeIds">Optional list of employee IDs to include (used for correction runs).</param>
        /// <param name="isCorrection">When <c>true</c>, sets <c>CorrectedInd = "1"</c> on each 1095-C record.</param>
        /// <returns>A tuple of the full file path and the total number of 1095-C records written.</returns>
        private async Task<(string Path, int Count)> GenerateFormDataXmlAsync(
            int employerId,
            int year,
            Guid transmissionId,
            TransmitterConfig config,
            List<int> filterEmployeeIds = null, // Optional Filter
            bool isCorrection = false // Optional Flag
        )
        {
            try
            {
                // 1. Fetch Data
                var employer1094 = await _employerService.GetEmployer1094DetailsAsync(employerId.ToString());

                // Safety Check: Ensure we actually got employer data
                if (employer1094 == null)
                    throw new Exception($"Employer details not found for ID {employerId}. Please verify the employer exists.");

                //var employees = await _acaLogic.GetEmployeeCodesListAsync(employerId, year);
                var aggregatedMembers = await _employerService.GetAggregatedMembersAsync(employerId);
                var allEmployees = await _acaLogic.GetEmployeeCodesListAsync(employerId, year);
                allEmployees = allEmployees.Where(e => e.GetForm == 1).ToList();
                // FILTER: Only process the specific employees if list provided
                if (filterEmployeeIds != null && filterEmployeeIds.Any())
                {
                    allEmployees = allEmployees.Where(e => filterEmployeeIds.Contains(e.EmployeeId)).ToList();
                }

                // 2. Build Root 1094-C Object
                var form1094 = new Form1094CUpstreamDetail
                {
                    SubmissionId = transmissionId.ToString(),
                    TaxYr = year.ToString(),
                    CorrectedInd = "0",
                    // FIX: Handle Null EIN safely
                    EmployerEIN = employer1094.EIN?.Replace("-", "") ?? "000000000",
                    EmployerName = new BusinessNameType { BusinessNameLine1 = employer1094.Name ?? "Unknown Name" },
                    EmployerAddress = new AddressType
                    {
                        AddressLine1Txt = employer1094.Address1 ?? "",
                        CityNm = employer1094.City ?? "",
                        USStateCd = employer1094.State ?? "",
                        // FIX: Handle Null ZipCode safely
                        USZIPCd = employer1094.ZipCode?.Replace("-", "") ?? "00000"
                    },
                    ContactName = new PersonNameType
                    {
                        FirstNm = employer1094.ContactName?.Split(' ')[0] ?? "Admin",
                        LastNm = employer1094.ContactName?.Split(' ').LastOrDefault() ?? "User",
                        MiddleNm = ""
                    },
                    // FIX: Handle Null Phone safely
                    ContactPhoneNum = employer1094.PhoneNumber?.Replace("-", "").Replace("(", "").Replace(")", "").Replace(" ", "") ?? "0000000000",

                    AleMemberInformation = new AleMemberInformationType
                    {
                        Total1095CAttachedCnt = allEmployees.Count,
                        AuthoritativeTransmittalInd = isCorrection ? "0" : "1",
                        Total1095CFiledCnt = isCorrection ? 0 : allEmployees.Count,
                        AleMemberGroupInd = aggregatedMembers.Any() ? "1" : "0",
                        QualifyingOfferMethodInd = employer1094.CertA ? "1" : "0",
                        NinetyEightPercentOfferMethodInd = employer1094.CertB ? "1" : "0",
                        Section4980HReliefInd = "0"
                    },
                    AleMemberInformationMonthly = new AleMemberInformationMonthlyType
                    {
                        MonthlyGroups = new List<AleMemberMonthlyGrp>()
                    }
                };

                // 3. Populate 1094 Monthly Counts
                for (int i = 1; i <= 12; i++)
                {
                    form1094.AleMemberInformationMonthly.MonthlyGroups.Add(new AleMemberMonthlyGrp
                    {
                        MonthId = i.ToString("00"),
                        MinEssentialCvrgOffrInd = employer1094.MinimumCoverage[i] == 1 ? "1" : "0",
                        FullTimeEmployeeCnt = employer1094.FullTime[i],
                        TotalEmployeeCnt = employer1094.Total[i],
                        AggregatedGroupInd = employer1094.AggregateGroup[i] == 1 ? "1" : "0"
                    });
                }

                // 4. Build 1095-C List
                foreach (var emp in allEmployees)
                {
                    var form1095 = new Form1095CUpstreamDetail
                    {
                        RecordId = emp.EmployeeId.ToString(),
                        // FIX: Handle Null Employee SSN
                        EmployeeSSN = emp.SSN?.Replace("-", "") ?? "000000000",
                        CorrectedInd = isCorrection ? "1" : "0", // <--- SET THE X
                        EmployeeName = new PersonNameType { FirstNm = emp.FirstName ?? "", LastNm = emp.LastName ?? "", MiddleNm = "" },
                        EmployeeAddress = new AddressType
                        {
                            AddressLine1Txt = emp.Address1 ?? "",
                            CityNm = emp.City ?? "",
                            USStateCd = emp.State ?? "",
                            USZIPCd = emp.Zip?.Replace("-", "") ?? ""
                        },

                        EmployeeOfferAndCoverage = new EmployeeOfferAndCoverageType
                        {
                            MonthlyOfferCoverage = new List<MonthlyOfferCoverageGroup>()
                        }
                    };

                    AddMonthlyCodes(form1095, emp);
                    // Part III — Covered Individuals. Mirrors the B-form path at
                    // GenerateFormDataXmlForBFormsAsync. Required for self-funded
                    // employers; returns no rows for fully-insured, so the element
                    // stays empty rather than being wrongly populated.
                    var covered = await _acaLogic.GetCoveredIndividualsAsync(emp.EmployeeId, year);

                    if (covered != null && covered.Any())
                    {
                        form1095.CoveredIndividuals = new List<CoveredIndividualType>();

                        foreach (var c in covered)
                        {
                            var ciXml = new CoveredIndividualType
                            {
                                PersonName = new PersonNameType { FirstNm = c.FirstName, LastNm = c.LastName },
                                SSN = c.SSN?.Replace("-", ""),
                                BirthDt = c.Birthday?.ToString("yyyy-MM-dd"),
                                CoveredIndicator = c.AllM ? "1" : "0"
                            };

                            if (!c.AllM)
                            {
                                ciXml.JanInd = c.Jan ? "1" : "0";
                                ciXml.FebInd = c.Feb ? "1" : "0";
                                ciXml.MarInd = c.Mar ? "1" : "0";
                                ciXml.AprInd = c.Apr ? "1" : "0";
                                ciXml.MayInd = c.May ? "1" : "0";
                                ciXml.JunInd = c.Jun ? "1" : "0";
                                ciXml.JulInd = c.Jul ? "1" : "0";
                                ciXml.AugInd = c.Aug ? "1" : "0";
                                ciXml.SepInd = c.Sep ? "1" : "0";
                                ciXml.OctInd = c.Oct ? "1" : "0";
                                ciXml.NovInd = c.Nov ? "1" : "0";
                                ciXml.DecInd = c.Dec ? "1" : "0";
                            }

                            form1095.CoveredIndividuals.Add(ciXml);
                        }
                    }
                    form1094.Form1095Cs.Add(form1095);
                }

                // 5. Serialize
                string folder = Path.Combine(_env.ContentRootPath, "IrsSubmissions", employerId.ToString());
                Directory.CreateDirectory(folder);

                string fileName = $"1094C_Request_{config.TCC}_{DateTime.UtcNow:yyyyMMdd}T{DateTime.UtcNow:HHmmss}000Z.xml";
                string fullPath = Path.Combine(folder, fileName);

                var serializer = new XmlSerializer(typeof(Form1094CUpstreamDetail));
                var ns = new XmlSerializerNamespaces();
                ns.Add("urn", "urn:us:gov:treasury:irs:ext:aca:air:7.0");
                ns.Add("irs", "urn:us:gov:treasury:irs:common");

                using (var writer = XmlWriter.Create(fullPath, new XmlWriterSettings { Indent = true }))
                {
                    serializer.Serialize(writer, form1094, ns);
                }

                return (fullPath, allEmployees.Count);
            }
            catch (Exception ex) when (!(ex.Message.StartsWith("Employer details not found")))
            {
                throw new Exception($"Error generating 1094-C/1095-C XML for EmployerId {employerId}, Year {year}.", ex);
            }
        }

        /// <summary>
        /// Maps the monthly ACA offer-of-coverage codes, employee required
        /// contribution amounts, and safe harbour codes from an
        /// <see cref="EmployeeCodeReviewDto"/> onto the
        /// <see cref="Form1095CUpstreamDetail.EmployeeOfferAndCoverage"/> collection.
        /// When <c>ALLM_COC</c> is populated the entire year is represented by a
        /// single group with <c>MonthId = "00"</c> (IRS "all 12 months" shorthand).
        /// Otherwise, individual monthly groups 01–12 are emitted. This method is
        /// synchronous and performs no database access.
        /// </summary>
        /// <param name="form">The 1095-C form object whose coverage collection will be populated.</param>
        /// <param name="emp">The employee code review DTO supplying the month-by-month code values.</param>
        private void AddMonthlyCodes(Form1095CUpstreamDetail form, EmployeeCodeReviewDto emp)
        {
            try
            {
                var coverage = form.EmployeeOfferAndCoverage ??= new EmployeeOfferAndCoverageType();
                var groups = coverage.MonthlyOfferCoverage ??= new List<MonthlyOfferCoverageGroup>();

                // All twelve months carry the same codes. sp_Generate1095Codes only
                // writes ALLM_COC when every month agrees, so this is the IRS
                // "all 12 months" shorthand and no monthly groups are emitted.
                if (!string.IsNullOrWhiteSpace(emp.ALLM_COC))
                {
                    groups.Add(BuildMonthlyGroup(
                        "00", emp.ALLM_COC, emp.ALLM_LCMP, emp.ALLM_SHC, emp.ALLM_ZIP));
                    return;
                }

                // Otherwise each month is reported on its own line. Line 14 is
                // mandatory for all twelve, so a missing code is a data fault the
                // operator has to resolve rather than something to paper over:
                // defaulting to 1H would file "no offer made" against an employee
                // who may well have been offered coverage, and that is exactly the
                // pattern the IRS assesses a 4980H(a) penalty on.
                foreach (var month in MonthlyCodeAccessors)
                {
                    var offerCode = month.Offer(emp);

                    if (string.IsNullOrWhiteSpace(offerCode))
                        throw new InvalidOperationException(
                            $"Employee {emp.EmployeeId} ({emp.FirstName} {emp.LastName}, SSN ending " +
                            $"{LastFour(emp.SSN)}) has no line 14 offer-of-coverage code for month " +
                            $"{month.MonthId}. Regenerate codes for this employer — or unlock the " +
                            $"employee record if it is locked — before generating the submission.");

                    groups.Add(BuildMonthlyGroup(
                        month.MonthId,
                        offerCode,
                        month.Contribution(emp),
                        month.SafeHarbor(emp),
                        month.Zip(emp)));
                }
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                throw new Exception($"Error mapping monthly codes for EmployeeId {emp.EmployeeId}.", ex);
            }
        }

        /// <summary>
        /// Builds one <see cref="MonthlyOfferCoverageGroup"/>. Optional elements are
        /// set to <c>null</c> rather than an empty string so the serialiser omits
        /// them: AIR rejects an empty decimal element, and line 15 is only reported
        /// for the line 14 codes that require it.
        /// </summary>
        private static MonthlyOfferCoverageGroup BuildMonthlyGroup(
            string monthId, string? offerCode, decimal? contribution, string? safeHarbor, string? zip)
        {
            return new MonthlyOfferCoverageGroup
            {
                MonthId = monthId,
                OfferOfCoverageCd = offerCode,
                // Invariant culture: a comma decimal separator would be rejected.
                EmployeeRequiredContriAmt = contribution?.ToString("F2", CultureInfo.InvariantCulture),
                SafeHarborCd = string.IsNullOrWhiteSpace(safeHarbor) ? null : safeHarbor,
                // ICHRA (1L-1U): Line 17 ZIP. Emitted only when present.
                ZipCd = string.IsNullOrWhiteSpace(zip) ? null : zip
            };
        }

        private static string LastFour(string? ssn)
        {
            var digits = new string((ssn ?? string.Empty).Where(char.IsDigit).ToArray());
            return digits.Length >= 4 ? digits[^4..] : "unknown";
        }

        /// <summary>
        /// Month-by-month accessors for the wide EmployeeCode columns, in IRS month
        /// order. Written out rather than resolved by reflection so a renamed column
        /// is a compile error instead of a silently empty element on a tax filing.
        /// </summary>
        private static readonly (
            string MonthId,
            Func<EmployeeCodeReviewDto, string?> Offer,
            Func<EmployeeCodeReviewDto, decimal?> Contribution,
            Func<EmployeeCodeReviewDto, string?> SafeHarbor,
            Func<EmployeeCodeReviewDto, string?> Zip)[] MonthlyCodeAccessors =
        {
            ("01", e => e.JAN_COC, e => e.JAN_LCMP, e => e.JAN_SHC, e => e.JAN_ZIP),
            ("02", e => e.FEB_COC, e => e.FEB_LCMP, e => e.FEB_SHC, e => e.FEB_ZIP),
            ("03", e => e.MAR_COC, e => e.MAR_LCMP, e => e.MAR_SHC, e => e.MAR_ZIP),
            ("04", e => e.APR_COC, e => e.APR_LCMP, e => e.APR_SHC, e => e.APR_ZIP),
            ("05", e => e.MAY_COC, e => e.MAY_LCMP, e => e.MAY_SHC, e => e.MAY_ZIP),
            ("06", e => e.JUN_COC, e => e.JUN_LCMP, e => e.JUN_SHC, e => e.JUN_ZIP),
            ("07", e => e.JUL_COC, e => e.JUL_LCMP, e => e.JUL_SHC, e => e.JUL_ZIP),
            ("08", e => e.AUG_COC, e => e.AUG_LCMP, e => e.AUG_SHC, e => e.AUG_ZIP),
            ("09", e => e.SEP_COC, e => e.SEP_LCMP, e => e.SEP_SHC, e => e.SEP_ZIP),
            ("10", e => e.OCT_COC, e => e.OCT_LCMP, e => e.OCT_SHC, e => e.OCT_ZIP),
            ("11", e => e.NOV_COC, e => e.NOV_LCMP, e => e.NOV_SHC, e => e.NOV_ZIP),
            ("12", e => e.DEC_COC, e => e.DEC_LCMP, e => e.DEC_SHC, e => e.DEC_ZIP)
        };

        /// <summary>
        /// Returns all submission log records for a given tax year that are
        /// visible to a specific account manager, by joining through
        /// <c>tbl_Portfolio_Assignment</c> via <c>sp_GetSubmissionsForAm</c>.
        /// Only active (IsActive = 1) assignments are included. Results are
        /// ordered by generation date descending. Returns an empty enumerable
        /// on error.
        /// </summary>
        /// <param name="amUserId">The account manager's user ID to scope the results to.</param>
        /// <param name="year">The ACA tax year to retrieve submissions for.</param>
        /// <returns>An <see cref="IEnumerable{IrsSubmissionLogDto}"/> scoped to the AM's portfolio.</returns>
        public async Task<IEnumerable<IrsSubmissionLogDto>> GetSubmissionsForAmAsync(string amUserId, int year)
        {
            try
            {
                // Replaced inline SQL with sp_GetSubmissionsForAm stored procedure
                using var db = Connection;
                return await db.QueryAsync<IrsSubmissionLogDto>(
                    "sp_GetSubmissionsForAm",
                    new { UserId = amUserId, Year = year },
                    commandType: CommandType.StoredProcedure);
            }
            catch (SqlException ex)
            {
                throw new Exception($"Database error retrieving submissions for AM UserId {amUserId}, Year {year}.", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Unexpected error retrieving submissions for AM UserId {amUserId}, Year {year}.", ex);
            }
        }

        //Helper method

        private async Task<IrsSubmissionLog> GetSubmissionByEmployerYearAsync(int employerId, int year)
        {
            using var db = Connection;
            return await db.QuerySingleOrDefaultAsync<IrsSubmissionLog>(
                "sp_GetSubmissionByEmployerYear",
                new { EmployerId = employerId, TaxYear = year },
                commandType: CommandType.StoredProcedure);
        }

        private async Task UpdateSubmissionFilesAsync(int id, string formType, Guid transmissionId,
            string manifestPath, string formPath, int recordCount, string generatedBy)
        {
            using var db = Connection;
            db.Open();
            using var transaction = db.BeginTransaction();
            try
            {
                await db.ExecuteAsync(
                    "sp_UpdateSubmissionFiles",
                    new
                    {
                        Id = id,
                        FormType = formType,
                        TransmissionId = transmissionId,
                        ManifestFilePath = manifestPath,
                        FormFilePath = formPath,
                        RecordCount = recordCount,
                        GeneratedBy = generatedBy
                    },
                    transaction: transaction,
                    commandType: CommandType.StoredProcedure);

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }
}