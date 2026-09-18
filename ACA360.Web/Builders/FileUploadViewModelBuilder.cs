using System;
using System.Collections.Generic;
using System.Linq;
using ACA360.Core.Models;
using ACA360.Web.Models;

namespace ACA360.Web.Builders
{
    /// <summary>
    /// Encapsulates the logic for assembling ViewModels for the FileUploadController.
    /// Manages date parsing, LINQ grouping of logs by Employer/Tax ID, and pagination state mapping.
    /// </summary>
    public class FileUploadViewModelBuilder
    {
        /// <summary>
        /// Assembles the initial Dashboard ViewModel including statistical counts and active templates.
        /// </summary>
        public GroupedDashboardViewModel BuildGroupedDashboardViewModel(
            IEnumerable<UploadedFileLog> logs,
            fileDashboardStatsDto stats,
            IEnumerable<ImportTemplate> activeTemplates,
            int totalCount,
            string searchTerm,
            string filter,
            string dateRange,
            bool showMyFiles,
            int pageNumber,
            int pageSize)
        {
            return new GroupedDashboardViewModel
            {
                FileGroups = GroupLogs(logs),
                TotalCount = totalCount,
                PageSize = pageSize,
                PageNumber = pageNumber,
                SearchTerm = searchTerm ?? "",
                SelectedStatus = filter ?? "All",
                DateRange = dateRange,
                ShowMyFilesOnly = showMyFiles,

                TotalUploadsOfMonth = stats.TotalUploads,
                ActionRequiredCount = stats.ActionRequired,
                ProcessingCount = stats.Processing,
                CompletedCount = stats.Completed,

                UploadModel = new UploadedFileModel
                {
                    AvailableTemplates = (activeTemplates ?? new List<ImportTemplate>())
                        .Select(t => new TemplateViewModel
                        {
                            TemplateId = t.TemplateId,
                            TemplateName = t.TemplateName
                        }).ToList()
                }
            };
        }

        /// <summary>
        /// Assembles the partial Dashboard ViewModel used asynchronously when paginating or filtering the grid.
        /// </summary>
        public GroupedDashboardViewModel BuildPartialDashboardViewModel(
            IEnumerable<UploadedFileLog> logs,
            int totalCount,
            string filter,
            string viewMode,
            int pageNumber,
            int pageSize)
        {
            return new GroupedDashboardViewModel
            {
                FileGroups = GroupLogs(logs),
                TotalCount = totalCount,
                PageSize = pageSize,
                PageNumber = pageNumber,
                SelectedStatus = filter ?? "All",
                ViewMode = viewMode ?? "Active"
            };
        }

        /// <summary>
        /// Groups raw File Logs by Employer Name and Tax ID for accordion display.
        /// </summary>
        public List<FileGroupViewModel> GroupLogs(IEnumerable<UploadedFileLog> logs)
        {
            if (logs == null) return new List<FileGroupViewModel>();

            return logs
                .GroupBy(f => new { f.EmployerName, f.TaxID })
                .Select(g => new FileGroupViewModel
                {
                    EmployerName = string.IsNullOrEmpty(g.Key.EmployerName) ? "Pending Identification" : g.Key.EmployerName,
                    TaxId = g.Key.TaxID,
                    Files = g.OrderByDescending(f => f.UploadedAt).ToList()
                })
                .OrderByDescending(g => g.LatestUpload)
                .ToList();
        }

        /// <summary>
        /// Safely translates a UI data range string (e.g. "01/01/2026 to 01/31/2026") into nullable DateTimes.
        /// </summary>
        public (DateTime? from, DateTime? to) ParseDateRange(string dateRange)
        {
            DateTime? from = null, to = null;
            if (string.IsNullOrEmpty(dateRange)) return (from, to);

            var parts = dateRange.Split(" to ", StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1 && DateTime.TryParse(parts[0], out var f)) from = f;
            if (parts.Length >= 2 && DateTime.TryParse(parts[1], out var t)) to = t;

            return (from, to);
        }
    }
}
