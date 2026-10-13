using ACA360.BusinessLogic.Services;
using ACA360.Core.Constants;
using ACA360.Core.Models;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ACA360.Web.Controllers
{
    /// <summary>
    /// Map an employer's column names once (script 073). A file rejected for missing columns opens a
    /// form listing each missing template column next to the file's unused headers; what is chosen is
    /// kept for the employer (by EIN) and the template, and the file is checked again.
    /// </summary>
    public partial class FileUploadController
    {
        public sealed class ColumnNeed
        {
            public string TargetColumnName { get; set; } = "";
            public string? TemplateHeader { get; set; }
            public string? AlternateNames { get; set; }
        }

        public sealed class SheetMapping
        {
            public string Sheet { get; set; } = "";
            public bool SheetMissing { get; set; }
            public List<ColumnNeed> Missing { get; set; } = new();
            public List<string> UnusedHeaders { get; set; } = new();
        }

        public sealed class ColumnMappingViewModel
        {
            public int FileLogId { get; set; }
            public string? FileName { get; set; }
            public string? EmployerName { get; set; }
            public string? Ein { get; set; }
            public bool HeadersRead { get; set; }
            public List<SheetMapping> Sheets { get; set; } = new();
            public List<EmployerColumnMap> Saved { get; set; } = new();
        }

        public sealed class ColumnMappingInput
        {
            public string? Sheet { get; set; }
            public string? Target { get; set; }
            public string? Header { get; set; }
        }

        private static bool Matches(TemplateColumnMap rule, HashSet<string> normalized) =>
            normalized.Contains(ExcelStructureValidatorService.NormalizeHeader(rule.SourceColumnName))
            || (rule.AlternateNames ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                   .Any(a => normalized.Contains(ExcelStructureValidatorService.NormalizeHeader(a)));

        private async Task<(UploadedFileLog? Log, IActionResult? Denied)> MappableFileAsync(int id)
        {
            var log = await _logRepo.GetLogByIdAsync(id);
            if (log == null || log.IsDeleted || !await _workflowService.VerifyFileAccessAsync(id, GetCurrentUserId()))
                return (null, NotFound(new { success = false, message = "File not found." }));
            if (string.IsNullOrWhiteSpace(log.TaxID))
                return (null, BadRequest(new { success = false, message = "This file has no employer EIN, so a mapping cannot be kept for it." }));
            return (log, null);
        }

        [HttpGet]
        [Authorize(Roles = UserRoles.FileHandlers)]
        public async Task<IActionResult> ColumnMapping(int id)
        {
            var (log, denied) = await MappableFileAsync(id);
            if (denied != null) return denied;

            var rules = await _templateService.GetColumnMapsForEmployerAsync(log!.ImportTemplateId, log.TaxID);
            var headers = await _fileProcessingService.ReadHeadersAsync(id);
            var model = new ColumnMappingViewModel
            {
                FileLogId = id, FileName = log.OriginalFileName, EmployerName = log.EmployerName, Ein = log.TaxID,
                HeadersRead = headers.Count > 0,
                Saved = await _templateService.GetEmployerColumnMapsAsync(log.TaxID!, log.ImportTemplateId),
            };

            foreach (var sheet in rules.Where(r => !string.IsNullOrEmpty(r.SourceSheetName))
                                       .GroupBy(r => r.SourceSheetName!, StringComparer.OrdinalIgnoreCase))
            {
                var entry = new SheetMapping { Sheet = sheet.Key };
                if (!headers.TryGetValue(sheet.Key, out var fileHeaders))
                {
                    entry.SheetMissing = model.HeadersRead;
                    model.Sheets.Add(entry);
                    continue;
                }
                var normalized = fileHeaders.Select(ExcelStructureValidatorService.NormalizeHeader).ToHashSet();
                entry.Missing = sheet.Where(r => r.IsRequired && !Matches(r, normalized))
                    .Select(r => new ColumnNeed { TargetColumnName = r.TargetColumnName ?? "", TemplateHeader = r.SourceColumnName, AlternateNames = r.AlternateNames })
                    .ToList();
                entry.UnusedHeaders = fileHeaders
                    .Where(h => !sheet.Any(r => Matches(r, new HashSet<string> { ExcelStructureValidatorService.NormalizeHeader(h) })))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                model.Sheets.Add(entry);
            }
            return PartialView("_ColumnMappingPartial", model);
        }

        /// <summary>Keeps the chosen headers for the employer, then checks the file again.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = UserRoles.FileHandlers)]
        public async Task<IActionResult> SaveColumnMapping(int id, List<ColumnMappingInput> maps)
        {
            var (log, denied) = await MappableFileAsync(id);
            if (denied != null) return denied;

            var chosen = (maps ?? new()).Where(m => !string.IsNullOrWhiteSpace(m.Header) && !string.IsNullOrWhiteSpace(m.Sheet)
                                                    && !string.IsNullOrWhiteSpace(m.Target)).ToList();
            if (chosen.Count == 0) return BadRequest(new { success = false, message = "Choose a header for at least one column." });

            var headers = await _fileProcessingService.ReadHeadersAsync(id);
            foreach (var m in chosen)
            {
                // Only a header the file really has; a comma would split it when it is read back.
                if (!headers.TryGetValue(m.Sheet!, out var inSheet) || !inSheet.Contains(m.Header!.Trim(), StringComparer.OrdinalIgnoreCase))
                    return BadRequest(new { success = false, message = $"'{m.Header}' is not a header on the {m.Sheet} sheet." });
                if (m.Header!.Contains(','))
                    return BadRequest(new { success = false, message = $"'{m.Header}' has a comma in it. Rename that header in the file and upload it again." });
            }

            var saved = 0;
            foreach (var m in chosen)
                if (await _templateService.SaveEmployerColumnMapAsync(log!.TaxID!, log.ImportTemplateId, m.Sheet!, m.Target!, m.Header!.Trim(), User.Identity?.Name))
                    saved++;

            // The same check as after upload, now with the employer's headers.
            BackgroundJob.Enqueue<ACA360.BusinessLogic.Interfaces.IFileProcessingService>(w => w.ProcessFileAsync(id, log!.ImportTemplateId));
            return Json(new { success = true, saved, message = $"Saved {saved} mapping{(saved == 1 ? "" : "s")} for this employer. The file is being checked again." });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = UserRoles.FileHandlers)]
        public async Task<IActionResult> DeleteColumnMapping(int id, int mapId)
        {
            var (log, denied) = await MappableFileAsync(id);
            if (denied != null) return denied;
            return await _templateService.DeleteEmployerColumnMapAsync(mapId, log!.TaxID!)
                ? Json(new { success = true })
                : NotFound(new { success = false, message = "That mapping no longer exists." });
        }
    }
}
