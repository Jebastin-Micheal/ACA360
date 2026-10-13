using ACA360.Core.Models;
using ACA360.Web.Helpers;
using Microsoft.AspNetCore.Mvc;

namespace ACA360.Web.Controllers
{
    public partial class FileUploadController
    {
        [HttpPost]
        [ValidateAntiForgeryToken]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> ExportAnalysisExcel(int id, string importMode = "Merge",
            CancellationToken cancellationToken = default)
        {
            if (id <= 0 || !ImportAnalysisCatalog.IsMode(importMode))
                return BadRequest(new { error = "Invalid export request." });
            try
            {
                var log = await _logRepo.GetLogByIdAsync(id);
                if (log == null || log.IsDeleted ||
                    !await _workflowService.VerifyFileAccessAsync(log.FileLogId, GetCurrentUserId()))
                    return NotFound(new { error = "File is unavailable." });

                // Invalid/warning rows are downloadable for review. Import readiness is a separate check.
                var stream = await ImportAnalysisExcelExporter.CreateAsync(_dataRepo, id,
                    log.OriginalFileName, log.PlanYear, importMode, cancellationToken);
                // MVC disposes the returned stream after sending it; DeleteOnClose removes its temp file.
                return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"Import-Analysis-{id}-{importMode}.xlsx");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new EmptyResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Import analysis Excel export failed for file {FileLogId}", id);
                return StatusCode(500, new { error = "Could not create the Excel export. Please retry or check the server log." });
            }
        }

        /// <summary>
        /// "What changed": the file compared with what the system holds for its employer and year,
        /// by kind (new employees, leavers, status, coverage...), each change before and after,
        /// and the employees missing from the file (script 057).
        /// </summary>
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> AnalysisChanges(int id, string? kind = null, string? search = null, int pageIndex = 1,
            CancellationToken cancellationToken = default)
        {
            if (id <= 0) return BadRequest();
            try
            {
                var log = await _logRepo.GetLogByIdAsync(id);
                if (log == null || log.IsDeleted ||
                    !await _workflowService.VerifyFileAccessAsync(log.FileLogId, GetCurrentUserId()))
                    return NotFound();

                var changes = await _dataRepo.GetImportChangesAsync(id, string.IsNullOrWhiteSpace(kind) ? null : kind,
                    search, Math.Max(1, pageIndex), 25, cancellationToken);
                return PartialView("_ImportChangesPartial", changes);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new EmptyResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "What changed failed for file {FileLogId}", id);
                return StatusCode(500, "Could not work out what changed. Please retry.");
            }
        }
    }
}
