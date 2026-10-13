using ACA360.Core.Models;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Xml.Linq;
using ACA360.BusinessLogic.Interfaces;

namespace ACA360.BusinessLogic.Services
{
    public class IrsXmlService : IIrsXmlService
    {
        private readonly string _connectionString;
        private readonly IWebHostEnvironment _env;
        private readonly IEmployerService _employerService;
        private readonly IACALogicService _acaLogic;

        private const string BForms = "1094B_1095B";
        private const string CForms = "1094C_1095C";

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
        /// Generates the IRS submission files for an employer and tax year: the 1094-C/1095-C
        /// or 1094-B/1095-B form data file (from the employer's FormType) and its manifest, in
        /// the IRS format of that tax year (<see cref="IrsAirXml"/>), each checked against the IRS
        /// schema before it is saved, and records them in IrsSubmissionLog with the record list
        /// (one row per employer and year). If recording fails the new files are deleted.
        /// What can be regenerated follows IRS Publication 5165, section 7:
        /// <list type="bullet">
        /// <item>not sent yet (status 0): regenerated freely (a replacement stays a replacement);</item>
        /// <item>sent and waiting for the acknowledgement (1), or accepted (2, 3): refused - an
        /// accepted filing is changed only by corrections;</item>
        /// <item>rejected (4): regenerated as a replacement (TransmissionTypeCd R) naming the
        /// rejected Receipt ID (OriginalReceiptId), as 7.3.1 requires.</item>
        /// </list>
        /// Data the IRS would reject is reported as an <see cref="IrsDataException"/> listing what to fix.
        /// </summary>
        /// <param name="employerId">The numeric employer ID to generate files for.</param>
        /// <param name="year">The ACA tax year being filed.</param>
        /// <param name="userId">The ID of the user triggering the generation, stored in the submission log.</param>
        /// <returns>The <see cref="IrsSubmissionLog"/> record, with its SubmissionId.</returns>
        public async Task<IrsSubmissionLog> GenerateSubmissionFilesAsync(int employerId, int year, string userId)
        {
            try
            {
                var config = await GetTransmitterConfigAsync()
                    ?? throw new InvalidOperationException("Transmitter Configuration missing.");
                var employer = await _employerService.GetEmployerDetailsByIdAsync(employerId.ToString())
                    ?? throw new InvalidOperationException($"Employer {employerId} was not found.");
                string formType = employer.FormType == "1094-B" ? BForms : CForms;

                // What the existing filing allows, before anything is built or written.
                var existing = await GetSubmissionByEmployerYearAsync(employerId, year);
                string? replaces = existing?.ReplacesReceiptId;
                switch (existing?.StatusId)
                {
                    case 1:
                        throw new IrsDataException($"The {year} IRS files of this employer were not regenerated", new[]
                        {
                            $"Submission #{existing.SubmissionId} is marked as transmitted and waiting for the IRS acknowledgement. Upload the acknowledgement first: if the IRS rejects it, a replacement can be generated; if it accepts it, changes are made by corrections."
                        });
                    case 2:
                    case 3:
                        throw new IrsDataException($"The {year} IRS files of this employer were not regenerated", new[]
                        {
                            $"The IRS accepted submission #{existing.SubmissionId} (receipt {existing.ReceiptId}). An accepted filing is changed only by corrections (Fix & Correct), not by sending it again."
                        });
                    case 4:
                        // A rejected original is re-sent as a replacement of the rejected transmission.
                        replaces = existing.ReceiptId ?? existing.ReplacesReceiptId;
                        break;
                }

                Guid transmissionId = Guid.NewGuid();
                var built = formType == BForms
                    ? await BuildBFormsAsync(employerId, year, corrections: null)
                    : await BuildCFormsAsync(employerId, year, corrections: null);
                int recordCount = built.Records.Count;

                var files = WriteSubmissionFiles(employerId, year, formType, built.Doc, recordCount, transmissionId, config,
                    isCorrection: false, originalReceiptId: replaces);
                try
                {
                    if (existing != null)
                    {
                        // One row per employer and year: point it at the new files.
                        await UpdateSubmissionFilesAsync(existing.SubmissionId, formType, transmissionId,
                            files.ManifestPath, files.FormPath, recordCount, userId, built.Records, replaces, built.PayerName, built.PayerEin);

                        existing.FormType = formType;
                        existing.TransmissionId = transmissionId;
                        existing.ManifestFilePath = files.ManifestPath;
                        existing.FormFilePath = files.FormPath;
                        existing.RecordCount = recordCount;
                        existing.StatusId = 0;
                        existing.ReceiptId = null;     // a new transmission (script 049)
                        existing.AckFilePath = null;
                        existing.ReplacesReceiptId = replaces;
                        existing.FiledPayerName = built.PayerName;
                        existing.FiledPayerEin = built.PayerEin;
                        return existing;
                    }

                    var log = new IrsSubmissionLog
                    {
                        EmployerId = employerId,
                        TaxYear = year,
                        FormType = formType,
                        TransmissionId = transmissionId,
                        ManifestFilePath = files.ManifestPath,
                        FormFilePath = files.FormPath,
                        RecordCount = recordCount,
                        StatusId = 0,
                        GeneratedBy = userId,
                        SubmissionType = "O",
                        FiledPayerName = built.PayerName,
                        FiledPayerEin = built.PayerEin,
                    };
                    await SaveSubmissionLogAsync(log, built.Records);
                    return log;
                }
                catch
                {
                    files.Delete();
                    throw;
                }
            }
            catch (IrsDataException)
            {
                throw;   // already says what to fix
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
        /// Reads an IRS acknowledgement uploaded for a submission - the status response or the
        /// Error Data File from the AIR UI (<see cref="IrsAcknowledgement.Parse"/>) - and records
        /// it: the status (Accepted 2, Accepted with Errors / Partially Accepted 3, Rejected 4,
        /// Processing 1), the Receipt ID, the file, and every error the IRS reported. Errors on a
        /// 1095 are traced to the employee through the submission's record list (Record IDs run
        /// 1, 2, 3...; dbo.IrsSubmissionRecord), so the correction screen and the correction
        /// files can use them. Status and errors are saved together.
        /// </summary>
        /// <param name="submissionId">The ID of the submission record being acknowledged.</param>
        /// <param name="file">The IRS acknowledgement or Error Data File uploaded by the user.</param>
        /// <returns><c>true</c> on success.</returns>
        public async Task<bool> ProcessAckFileAsync(int submissionId, IFormFile file)
        {
            if (file == null || file.Length == 0) throw new Exception("Invalid file.");
            var submission = await GetSubmissionByIdAsync(submissionId) ?? throw new Exception("Submission not found.");

            IrsAcknowledgement ack;
            await using (var stream = file.OpenReadStream())
            {
                try { ack = IrsAcknowledgement.Parse(stream); }
                catch (FormatException ex) { throw new IrsDataException("The acknowledgement was not recorded", new[] { ex.Message }); }
            }

            if (ack.NotFound)
                throw new IrsDataException("The acknowledgement was not recorded", new[] { "The IRS reports this transmission as Not Found: check the Receipt ID and that you are on the right system (AATS for test files, production otherwise)." });
            if (!string.IsNullOrWhiteSpace(submission.ReceiptId) && !string.IsNullOrWhiteSpace(ack.ReceiptId)
                && !string.Equals(submission.ReceiptId.Trim(), ack.ReceiptId.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new IrsDataException("The acknowledgement was not recorded", new[] { $"This acknowledgement is for receipt {ack.ReceiptId}, but submission #{submissionId} has receipt {submission.ReceiptId}." });

            // Which employee each Record ID is.
            var records = (await GetSubmissionRecordsAsync(submissionId)).ToDictionary(r => r.RecordId, r => r.EmployeeId);
            var unknown = ack.Errors.Where(e => e.RecordId is { } rid && !records.ContainsKey(rid)).Select(e => e.UniqueRecordId).Distinct().ToList();
            if (unknown.Count > 0)
                throw new IrsDataException("The acknowledgement was not recorded", new[]
                {
                    $"It reports errors on records this submission does not have ({string.Join(", ", unknown.Take(5))}). Is it for a different transmission, or were the files regenerated after it was sent?"
                });

            // Keep the file with the submission's other files.
            string folder = Path.GetDirectoryName(submission.ManifestFilePath)
                ?? throw new InvalidOperationException("The submission has no manifest file path.");
            string fullPath = Path.Combine(folder, $"ACK_{DateTime.Now:yyyyMMddHHmmss}_{Path.GetFileName(file.FileName)}");
            await using (var output = new FileStream(fullPath, FileMode.Create))
                await file.CopyToAsync(output);

            int recordErrors = ack.Errors.Count(e => e.RecordId != null);
            string statusMsg = ack.StatusId switch
            {
                4 => "Rejected: " + (ack.Errors.FirstOrDefault()?.Text ?? ack.Errors.FirstOrDefault()?.Code ?? "see the errors"),
                3 => $"{ack.Status}: {recordErrors} error(s) on forms, {ack.Errors.Count - recordErrors} on the submission",
                2 => "Accepted",
                _ => ack.Status ?? "Acknowledgement recorded",
            };
            if (ack.ErrorFileMissing && ack.StatusId is 3 or 4)
                statusMsg += " - the error details are in the attachment, which this file does not include: upload the Error Data File from the AIR UI.";
            if (statusMsg.Length > 500) statusMsg = statusMsg[..500];

            try
            {
                using var db = Connection;
                db.Open();
                using var transaction = db.BeginTransaction();
                try
                {
                    await db.ExecuteAsync("sp_UpdateSubmissionAckStatus",
                        new { Status = ack.StatusId, Msg = statusMsg, Receipt = ack.ReceiptId ?? submission.ReceiptId, Path = fullPath, Id = submissionId },
                        transaction: transaction, commandType: CommandType.StoredProcedure);

                    await db.ExecuteAsync("sp_ClearSubmissionErrors", new { SubmissionId = submissionId },
                        transaction: transaction, commandType: CommandType.StoredProcedure);
                    foreach (var e in ack.Errors)
                        await db.ExecuteAsync("sp_AddSubmissionError", new
                        {
                            SubmissionId = submissionId,
                            EmployeeId = e.RecordId is { } rid ? records[rid] : (int?)null,
                            UniqueRecordId = e.UniqueRecordId ?? e.UniqueSubmissionId,
                            ErrorCode = e.Code,
                            ErrorMessage = string.Join(" ", new[] { e.Text, e.Xpath == null ? null : $"[{e.Xpath}]" }.Where(t => !string.IsNullOrWhiteSpace(t))),
                        }, transaction: transaction, commandType: CommandType.StoredProcedure);

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
                return true;
            }
            catch (SqlException ex)
            {
                try { File.Delete(fullPath); } catch (IOException) { }
                throw new Exception($"Database error processing ACK file for SubmissionId {submissionId}.", ex);
            }
        }

        /// <summary>One 1095 as filed: its Record ID, the employee, and the name and TIN written on it.</summary>
        private sealed record FiledRecord(int RecordId, int EmployeeId, string? FiledFirstName, string? FiledLastName, string? FiledTin);

        /// <summary>The submission's record list: which employee each Record ID (1, 2, 3...) is, as filed.</summary>
        private async Task<List<FiledRecord>> GetSubmissionRecordsAsync(int submissionId)
        {
            using var db = Connection;
            return (await db.QueryAsync<FiledRecord>("sp_GetSubmissionRecords",
                new { SubmissionId = submissionId }, commandType: CommandType.StoredProcedure)).ToList();
        }

        /// <summary>Replaces a submission's record list, inside the caller's transaction.</summary>
        private static Task SaveSubmissionRecordsAsync(IDbConnection db, IDbTransaction transaction, int submissionId, IReadOnlyList<FiledRecord> records)
        {
            var table = new DataTable();
            table.Columns.Add("RecordId", typeof(int));
            table.Columns.Add("EmployeeId", typeof(int));
            table.Columns.Add("FiledFirstName", typeof(string));
            table.Columns.Add("FiledLastName", typeof(string));
            table.Columns.Add("FiledTin", typeof(string));
            foreach (var r in records)
                table.Rows.Add(r.RecordId, r.EmployeeId, (object?)r.FiledFirstName ?? DBNull.Value, (object?)r.FiledLastName ?? DBNull.Value, (object?)r.FiledTin ?? DBNull.Value);
            return db.ExecuteAsync("sp_SaveSubmissionRecords",
                new { SubmissionId = submissionId, Records = table.AsTableValuedParameter("dbo.IrsSubmissionRecordList") },
                transaction: transaction, commandType: CommandType.StoredProcedure);
        }

        /// <summary>The corrections filed for a submission (SubmissionType C, ParentSubmissionId = it), oldest first.</summary>
        private async Task<List<IrsSubmissionLog>> GetChildSubmissionsAsync(int submissionId)
        {
            using var db = Connection;
            return (await db.QueryAsync<IrsSubmissionLog>("sp_GetChildSubmissions",
                new { ParentSubmissionId = submissionId }, commandType: CommandType.StoredProcedure)).ToList();
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
        /// Generates the corrections for a submission the IRS accepted, from the errors in its
        /// acknowledgement, as IRS Publication 5165 section 7.1 sets them out:
        /// <list type="bullet">
        /// <item>errors on the 1094-C itself (ReceiptId|SubmissionId): a corrected 1094-C authoritative
        /// transmittal, complete and sent alone, naming the 1094-C it corrects with the employer name
        /// and EIN as filed (7.1.2);</item>
        /// <item>errors on 1095s: a transmission of corrected 1095s for only those employees, each
        /// naming the record it corrects with the name and TIN as filed (7.1.3).</item>
        /// </list>
        /// When both are needed they are two transmissions (7.1.4), so up to two correction rows
        /// (SubmissionType C, ParentSubmissionId the original) are made. A record or 1094-C already
        /// corrected by an accepted correction is corrected again through that correction (7.1.1).
        /// Corrections are refused for a submission that was not accepted: a rejected one is replaced
        /// (regenerate it). A 1094-B is a transmittal only and is never corrected.
        /// </summary>
        /// <param name="originalSubmissionId">The submission the IRS reported errors for.</param>
        /// <param name="userId">The ID of the user triggering the correction, stored in the new submission log.</param>
        /// <returns>The correction submissions made (one or two).</returns>
        public async Task<List<IrsSubmissionLog>> GenerateCorrectionFilesAsync(int originalSubmissionId, string userId)
        {
            try
            {
                var original = await GetSubmissionByIdAsync(originalSubmissionId)
                    ?? throw new Exception("Submission not found.");
                if (original.StatusId is not (2 or 3))
                    throw new IrsDataException("No correction was generated", new[]
                    {
                        original.StatusId == 4
                            ? $"The IRS rejected submission #{originalSubmissionId}: a rejected filing is not corrected but replaced. Regenerate it; it is sent as a replacement."
                            : $"Submission #{originalSubmissionId} has not been accepted by the IRS yet. Corrections can only be filed for an accepted filing: upload its acknowledgement first."
                    });
                if (string.IsNullOrWhiteSpace(original.ReceiptId))
                    throw new IrsDataException("No correction was generated", new[] { $"Submission #{originalSubmissionId} has no IRS Receipt ID: upload its acknowledgement first." });
                var config = await GetTransmitterConfigAsync()
                    ?? throw new Exception("Transmitter Configuration missing.");

                string formType = original.FormType == BForms ? BForms : CForms;
                string receipt = original.ReceiptId.Trim();
                var errors = await GetSubmissionErrorsAsync(originalSubmissionId);
                // Accepted corrections of this submission: a record they corrected is corrected through them.
                var acceptedCorrections = (await GetChildSubmissionsAsync(originalSubmissionId))
                    .Where(c => c.StatusId is 2 or 3 && !string.IsNullOrWhiteSpace(c.ReceiptId)).ToList();

                var made = new List<IrsSubmissionLog>();

                // 1. The 1094-C authoritative transmittal (errors identified by ReceiptId|SubmissionId).
                bool fix1094 = errors.Any(e => string.IsNullOrEmpty(e.RecordId) && e.UniqueRecordId?.Split('|').Length == 2);
                if (fix1094 && formType == CForms)
                {
                    var latest1094 = acceptedCorrections.LastOrDefault(c => c.RecordCount == 0 && c.FiledPayerEin != null);
                    var corrects = latest1094 != null
                        ? new IrsCorrectedSubmission($"{latest1094.ReceiptId!.Trim()}|1", latest1094.FiledPayerName, latest1094.FiledPayerEin)
                        : new IrsCorrectedSubmission($"{receipt}|1", original.FiledPayerName, original.FiledPayerEin);
                    var built = await BuildCFormsAsync(original.EmployerId, original.TaxYear, corrections: null, correctsSubmission: corrects);
                    made.Add(await SaveCorrectionAsync(original, formType, built, config, userId,
                        $"Correction of the Form 1094-C of Submission #{originalSubmissionId}"));
                }

                // 2. The 1095s the IRS reported errors on.
                var corrections = new Dictionary<int, IrsCorrectedRecord>();
                var filed = (await GetSubmissionRecordsAsync(originalSubmissionId)).ToDictionary(r => r.EmployeeId);
                foreach (var e in errors)
                {
                    if (!int.TryParse(e.RecordId, out var employeeId) || corrections.ContainsKey(employeeId)) continue;
                    filed.TryGetValue(employeeId, out var record);
                    var uniqueRecordId = e.UniqueRecordId is { } u && u.Split('|').Length == 3 ? u
                        : record != null ? $"{receipt}|1|{record.RecordId}" : null;
                    if (uniqueRecordId != null)
                        corrections[employeeId] = new IrsCorrectedRecord(uniqueRecordId, record?.FiledFirstName, record?.FiledLastName, record?.FiledTin);
                }
                foreach (var c in acceptedCorrections.Where(c => c.RecordCount > 0))
                    foreach (var r in await GetSubmissionRecordsAsync(c.SubmissionId))
                        if (corrections.ContainsKey(r.EmployeeId))
                            corrections[r.EmployeeId] = new IrsCorrectedRecord($"{c.ReceiptId!.Trim()}|1|{r.RecordId}", r.FiledFirstName, r.FiledLastName, r.FiledTin);

                if (corrections.Count > 0)
                {
                    var built = formType == BForms
                        ? await BuildBFormsAsync(original.EmployerId, original.TaxYear, corrections)
                        : await BuildCFormsAsync(original.EmployerId, original.TaxYear, corrections);
                    made.Add(await SaveCorrectionAsync(original, formType, built, config, userId,
                        $"Correction of {corrections.Count} form(s) of Submission #{originalSubmissionId}"));
                }

                if (made.Count == 0)
                    throw new IrsDataException("No correction was generated", new[]
                    {
                        fix1094 && formType == BForms
                            ? "The errors are on the Form 1094-B, which is a transmittal only and is not corrected; no Form 1095-B has an error to correct."
                            : "The acknowledgement lists no error on a Form 1094-C or on a form of this submission. (Errors on the transmission's manifest are for information only and cannot be corrected.)"
                    });
                return made;
            }
            catch (IrsDataException)
            {
                throw;
            }
            catch (SqlException ex)
            {
                throw new Exception($"Database error generating correction files for SubmissionId {originalSubmissionId}.", ex);
            }
            catch (Exception ex) when (ex.Message != "Submission not found.")
            {
                throw new Exception($"Unexpected error generating correction files for SubmissionId {originalSubmissionId}.", ex);
            }
        }

        /// <summary>Writes one correction transmission and records it as a new row linked to the original.</summary>
        private async Task<IrsSubmissionLog> SaveCorrectionAsync(IrsSubmissionLog original, string formType, BuiltForms built,
            TransmitterConfig config, string userId, string message)
        {
            Guid transmissionId = Guid.NewGuid();
            var files = WriteSubmissionFiles(original.EmployerId, original.TaxYear, formType, built.Doc, built.Records.Count, transmissionId, config,
                isCorrection: true, originalReceiptId: null);
            try
            {
                var log = new IrsSubmissionLog
                {
                    EmployerId = original.EmployerId,
                    TaxYear = original.TaxYear,
                    FormType = formType,
                    TransmissionId = transmissionId,
                    ManifestFilePath = files.ManifestPath,
                    FormFilePath = files.FormPath,
                    RecordCount = built.Records.Count,
                    StatusId = 0,
                    GeneratedBy = userId,
                    SubmissionType = "C",
                    ParentSubmissionId = original.SubmissionId,
                    StatusMessage = message,
                    FiledPayerName = built.PayerName,
                    FiledPayerEin = built.PayerEin,
                };
                await SaveSubmissionLogAsync(log, built.Records);
                return log;
            }
            catch
            {
                files.Delete();
                throw;
            }
        }

        /// <summary>The two files of one transmission, deleted together if it cannot be recorded.</summary>
        private sealed record SubmissionFiles(string FormPath, string ManifestPath)
        {
            public void Delete()
            {
                foreach (var path in new[] { FormPath, ManifestPath })
                    try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        /// <summary>
        /// Checks the form data file against the IRS schema and saves it, then builds, checks and
        /// saves its manifest (SHA-256 checksum and byte size of the saved form file). File names
        /// follow the IRS pattern: 1094C_Request_TCC_yyyyMMddTHHmmssfffZ.xml.
        /// </summary>
        private SubmissionFiles WriteSubmissionFiles(int employerId, int year, string formType, XDocument formDoc, int recordCount,
            Guid transmissionId, TransmitterConfig config, bool isCorrection, string? originalReceiptId)
        {
            string tcc = (config.TCC ?? "").Trim();
            var transmitterAddress = new IrsAddress(config.AddressLine1, null, config.City, config.State, config.ZipCode);
            // Before anything is written: the TCC is also part of the file names.
            IrsAirXml.CheckTransmitter(tcc, config.TransmitterEIN, config.CompanyName, transmitterAddress,
                config.ContactName, config.ContactPhone, config.SoftwareId);

            string folder = Path.Combine(_env.ContentRootPath, "IrsSubmissions", employerId.ToString());
            Directory.CreateDirectory(folder);
            var now = DateTime.UtcNow;
            string formFile = $"{(formType == BForms ? "1094B" : "1094C")}_Request_{tcc}_{now:yyyyMMdd}T{now:HHmmssfff}Z.xml";
            string formPath = Path.Combine(folder, formFile);
            string manifestPath = Path.Combine(folder, $"Manifest_{tcc}_{now:yyyyMMdd}T{now:HHmmssfff}Z.xml");

            IrsAirXml.ValidateAndSave(formDoc, formPath, formType == BForms ? "1094-B/1095-B" : "1094-C/1095-C");
            try
            {
                var manifest = IrsAirXml.BuildManifest(new IrsManifest(
                    TaxYear: year,
                    // The tax year reported, not the year of transmission; earlier years are prior-year data.
                    PriorYearData: year < now.Year - 1,
                    IsCorrection: isCorrection,
                    TestFile: IsTestEnvironment(config.Environment),
                    BForms: formType == BForms,
                    TransmitterEin: config.TransmitterEIN,
                    TransmitterName: config.CompanyName,
                    TransmitterAddress: transmitterAddress,
                    ContactName: config.ContactName,
                    ContactPhone: config.ContactPhone,
                    SoftwareId: config.SoftwareId,
                    Tcc: tcc,
                    TransmissionId: transmissionId,
                    PayeeCount: recordCount,
                    Checksum: ComputeSha256Checksum(formPath),
                    ByteSize: new FileInfo(formPath).Length,
                    FileName: formFile,
                    TimestampUtc: now,
                    OriginalReceiptId: originalReceiptId));
                IrsAirXml.ValidateAndSave(manifest, manifestPath, "manifest");
            }
            catch
            {
                new SubmissionFiles(formPath, manifestPath).Delete();
                throw;
            }
            return new SubmissionFiles(formPath, manifestPath);
        }

        /// <summary>TransmitterConfig.Environment holds "T" or "TEST" for the IRS test system (AATS).</summary>
        private static bool IsTestEnvironment(string? environment) =>
            environment?.Trim().ToUpperInvariant() is "T" or "TEST";

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
        /// Returns a single <see cref="IrsSubmissionLog"/> record by its primary
        /// key via <c>sp_GetSubmissionById</c>. Returns <c>null</c> when no
        /// matching record is found, which callers (e.g. ProcessAckFileAsync)
        /// treat as a "Submission not found" error condition.
        /// </summary>
        /// <param name="submissionId">The primary-key ID of the submission to retrieve.</param>
        /// <returns>The <see cref="IrsSubmissionLog"/> record, or <c>null</c> if not found.</returns>
        public async Task<IrsSubmissionLog?> GetSubmissionByIdAsync(int submissionId)
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
        private async Task<TransmitterConfig?> GetTransmitterConfigAsync()
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
        /// Inserts a new <see cref="IrsSubmissionLog"/> row via <c>sp_SaveSubmissionLog</c>, sets
        /// <see cref="IrsSubmissionLog.SubmissionId"/> to the new row's id (the procedure returns it)
        /// and saves the record list with it, in one transaction.
        /// </summary>
        /// <param name="log">The fully populated <see cref="IrsSubmissionLog"/> to insert.</param>
        private async Task SaveSubmissionLogAsync(IrsSubmissionLog log, IReadOnlyList<FiledRecord> records)
        {
            try
            {
                using var db = Connection;
                db.Open();
                using var transaction = db.BeginTransaction();
                try
                {
                    log.SubmissionId = await db.ExecuteScalarAsync<int>(
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
                            log.GeneratedBy,
                            SubmissionType = log.SubmissionType ?? "O",
                            ParentSubmissionId = log.ParentSubmissionId > 0 ? log.ParentSubmissionId : (int?)null,
                            log.StatusMessage,
                            log.FiledPayerName,
                            log.FiledPayerEin
                        },
                        transaction: transaction,
                        commandType: CommandType.StoredProcedure);

                    if (log.SubmissionId <= 0)
                        throw new InvalidOperationException("sp_SaveSubmissionLog did not return the new SubmissionId.");
                    await SaveSubmissionRecordsAsync(db, transaction, log.SubmissionId, records);
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

        // ---- Form data: from the database to IrsAirXml ----

        /// <summary>A built form data file, its record list (as filed) and the employer name and EIN as filed.</summary>
        private sealed record BuiltForms(XDocument Doc, List<FiledRecord> Records, string? PayerName, string? PayerEin);

        private static IrsAddress EmployeeAddress(EmployeeCodeReviewDto e) => new(e.Address1, e.Address2, e.City, e.State, e.Zip);

        /// <summary>The record as it is written in the file (the IRS text rules applied), to correct it later.</summary>
        private static FiledRecord Filed(int recordId, EmployeeCodeReviewDto e) =>
            new(recordId, e.EmployeeId, IrsText.PersonName(e.FirstName), IrsText.PersonName(e.LastName), IrsText.NineDigits(e.SSN));

        private async Task<List<IrsCoveredPerson>> CoveredPeopleAsync(int employeeId, int year)
        {
            var covered = await _acaLogic.GetCoveredIndividualsAsync(employeeId, year) ?? new List<CoveredIndividualModel>();
            return covered.Select(c => new IrsCoveredPerson(
                c.FirstName, c.MiddleName, c.LastName, c.Suffix, c.SSN, c.Birthday, c.AllM,
                new[] { c.Jan, c.Feb, c.Mar, c.Apr, c.May, c.Jun, c.Jul, c.Aug, c.Sep, c.Oct, c.Nov, c.Dec })).ToList();
        }

        /// <summary>
        /// The 1094-C/1095-C form data for an employer: the 1094-C (certifications and monthly
        /// counts from the employer's 1094 details, other members of its aggregated group) and a
        /// 1095-C for every employee who gets a form. For a correction of 1095-Cs, only the
        /// employees in <paramref name="corrections"/>, under a non-authoritative 1094-C; for a
        /// correction of the 1094-C (<paramref name="correctsSubmission"/>), the complete
        /// authoritative 1094-C alone (IRS Publication 5165, 7.1.2 and 7.1.3).
        /// Record IDs run 1, 2, 3... (3.2); the record list says which employee each is.
        /// </summary>
        private async Task<BuiltForms> BuildCFormsAsync(int employerId, int year, IReadOnlyDictionary<int, IrsCorrectedRecord>? corrections,
            IrsCorrectedSubmission? correctsSubmission = null)
        {
            bool isCorrection = corrections != null;
            var employer1094 = await _employerService.GetEmployer1094DetailsAsync(employerId.ToString())
                ?? throw new InvalidOperationException($"Employer details not found for ID {employerId}. Please verify the employer exists.");
            var aggregatedMembers = await _employerService.GetAggregatedMembersAsync(employerId);

            var employees = correctsSubmission != null ? new List<EmployeeCodeReviewDto>() : await FormEmployeesAsync(employerId, year, corrections?.Keys);

            var forms = new List<Irs1095C>();
            var records = new List<FiledRecord>();
            foreach (var e in employees)
            {
                int recordId = forms.Count + 1;
                records.Add(Filed(recordId, e));
                forms.Add(new Irs1095C(
                    RecordId: recordId,
                    EmployeeId: e.EmployeeId,
                    BirthDate: e.BirthDate,
                    Corrects: corrections?.GetValueOrDefault(e.EmployeeId),
                    FirstName: e.FirstName, LastName: e.LastName, Ssn: e.SSN, Address: EmployeeAddress(e),
                    PlanStartMonth: e.PlanStartMonth,
                    // sp_Generate1095Codes writes the ALLM_ columns only when every month agrees.
                    AnnualOffer: e.ALLM_COC,
                    MonthlyOffer: new[] { e.JAN_COC, e.FEB_COC, e.MAR_COC, e.APR_COC, e.MAY_COC, e.JUN_COC, e.JUL_COC, e.AUG_COC, e.SEP_COC, e.OCT_COC, e.NOV_COC, e.DEC_COC },
                    AnnualContribution: e.ALLM_LCMP,
                    MonthlyContribution: new[] { e.JAN_LCMP, e.FEB_LCMP, e.MAR_LCMP, e.APR_LCMP, e.MAY_LCMP, e.JUN_LCMP, e.JUL_LCMP, e.AUG_LCMP, e.SEP_LCMP, e.OCT_LCMP, e.NOV_LCMP, e.DEC_LCMP },
                    AnnualSafeHarbor: e.ALLM_SHC,
                    MonthlySafeHarbor: new[] { e.JAN_SHC, e.FEB_SHC, e.MAR_SHC, e.APR_SHC, e.MAY_SHC, e.JUN_SHC, e.JUL_SHC, e.AUG_SHC, e.SEP_SHC, e.OCT_SHC, e.NOV_SHC, e.DEC_SHC },
                    AnnualIchraZip: e.ALLM_ZIP,
                    MonthlyIchraZip: new[] { e.JAN_ZIP, e.FEB_ZIP, e.MAR_ZIP, e.APR_ZIP, e.MAY_ZIP, e.JUN_ZIP, e.JUL_ZIP, e.AUG_ZIP, e.SEP_ZIP, e.OCT_ZIP, e.NOV_ZIP, e.DEC_ZIP },
                    // Part III: self-insured employers; none for fully insured ones.
                    Covered: await CoveredPeopleAsync(e.EmployeeId, year)));
            }

            static T[] Months<T>(T[] byMonth, Func<T, T> map) => Enumerable.Range(1, 12).Select(m => map(byMonth.Length > m ? byMonth[m] : default!)).ToArray();

            string? ein = employer1094.EIN ?? employer1094.TaxId;
            var doc = IrsAirXml.Build1094C(new Irs1094C(
                TaxYear: year,
                Employer: new IrsEmployer(employer1094.Name, ein,
                    new IrsAddress(employer1094.Address1, employer1094.Address2, employer1094.City, employer1094.State, employer1094.ZipCode),
                    employer1094.ContactName, employer1094.PhoneNumber),
                // A 1095-C correction re-sends only some 1095-Cs, so its 1094-C is not the authoritative one.
                Authoritative: !isCorrection,
                IsCorrection: isCorrection,
                AggregatedGroup: aggregatedMembers.Any(),
                QualifyingOfferMethod: employer1094.CertA,
                NinetyEightPercentOfferMethod: employer1094.CertB,
                // The 1094 arrays are indexed by month, 1 to 12.
                MinimumEssentialCoverageOffered: Enumerable.Range(1, 12).Select(m => employer1094.MinimumCoverage.Length > m && employer1094.MinimumCoverage[m] == 1).ToArray(),
                FullTimeCount: Months(employer1094.FullTime, x => x),
                TotalCount: Months(employer1094.Total, x => x),
                AggregatedGroupMonths: Enumerable.Range(1, 12).Select(m => employer1094.AggregateGroup.Length > m && employer1094.AggregateGroup[m] == 1).ToArray(),
                OtherMembers: aggregatedMembers.Select(a => (a.MemberName, a.MemberEIN)).ToList(),
                Forms: forms,
                Corrects: correctsSubmission));
            return new BuiltForms(doc, records, IrsText.BusinessName(employer1094.Name), IrsText.NineDigits(ein));
        }

        /// <summary>The employees who get a form, in a fixed order; for a correction, only those corrected.</summary>
        private async Task<List<EmployeeCodeReviewDto>> FormEmployeesAsync(int employerId, int year, IEnumerable<int>? onlyEmployees)
        {
            var employees = (await _acaLogic.GetEmployeeCodesListAsync(employerId, year))
                .Where(e => e.GetForm == 1)
                .GroupBy(e => e.EmployeeId).Select(g => g.First())   // one form per employee
                .OrderBy(e => e.LastName).ThenBy(e => e.FirstName).ThenBy(e => e.EmployeeId)
                .ToList();
            if (onlyEmployees == null) return employees;
            var only = onlyEmployees.ToHashSet();
            return employees.Where(e => only.Contains(e.EmployeeId)).ToList();
        }

        /// <summary>The 1094-B/1095-B form data: a 1095-B for every employee who gets a form (or only those listed, for a correction).</summary>
        private async Task<BuiltForms> BuildBFormsAsync(int employerId, int year, IReadOnlyDictionary<int, IrsCorrectedRecord>? corrections)
        {
            bool isCorrection = corrections != null;
            var employer = await _employerService.GetEmployerDetailsByIdAsync(employerId.ToString())
                ?? throw new InvalidOperationException($"Employer {employerId} was not found.");

            var employees = await FormEmployeesAsync(employerId, year, corrections?.Keys);

            var forms = new List<Irs1095B>();
            var records = new List<FiledRecord>();
            foreach (var e in employees)
            {
                int recordId = forms.Count + 1;
                records.Add(Filed(recordId, e));
                forms.Add(new Irs1095B(recordId, e.FirstName, e.LastName, e.SSN, EmployeeAddress(e), await CoveredPeopleAsync(e.EmployeeId, year),
                    EmployeeId: e.EmployeeId, Corrects: corrections?.GetValueOrDefault(e.EmployeeId)));
            }

            var doc = IrsAirXml.Build1094B(new Irs1094B(
                TaxYear: year,
                Employer: new IrsEmployer(employer.Name, employer.TaxId,
                    new IrsAddress(employer.Address1, employer.Address2, employer.City, employer.State, employer.ZipCode),
                    employer.ContactName, employer.PhoneNumber),
                IsCorrection: isCorrection,
                Forms: forms));
            return new BuiltForms(doc, records, IrsText.BusinessName(employer.Name), IrsText.NineDigits(employer.TaxId));
        }

        //Helper method

        private async Task<IrsSubmissionLog?> GetSubmissionByEmployerYearAsync(int employerId, int year)
        {
            using var db = Connection;
            return await db.QuerySingleOrDefaultAsync<IrsSubmissionLog>(
                "sp_GetSubmissionByEmployerYear",
                new { EmployerId = employerId, TaxYear = year },
                commandType: CommandType.StoredProcedure);
        }

        private async Task UpdateSubmissionFilesAsync(int id, string formType, Guid transmissionId,
            string manifestPath, string formPath, int recordCount, string generatedBy, IReadOnlyList<FiledRecord> records,
            string? replacesReceiptId, string? filedPayerName, string? filedPayerEin)
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
                        GeneratedBy = generatedBy,
                        ReplacesReceiptId = replacesReceiptId,
                        FiledPayerName = filedPayerName,
                        FiledPayerEin = filedPayerEin
                    },
                    transaction: transaction,
                    commandType: CommandType.StoredProcedure);
                await SaveSubmissionRecordsAsync(db, transaction, id, records);

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