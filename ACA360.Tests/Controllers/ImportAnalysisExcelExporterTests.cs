using ACA360.Core.Models;
using ACA360.Repositories.Interfaces;
using ACA360.Web.Helpers;
using Moq;
using OfficeOpenXml;

namespace ACA360.Tests;

public class ImportAnalysisExcelExporterTests
{
    [Fact]
    public async Task WorkbookContainsAllRowsSixSheetsChartAndLiteralIdentifiers()
    {
        ExcelPackage.LicenseContext = LicenseContext.Commercial; // same configuration as ACA360
        var repository = new Mock<IDataProcessingRepository>(MockBehavior.Strict);
        foreach (var entity in new[] { "Employer", "Plan", "Employee", "Premium", "Dependent" })
        {
            repository.Setup(x => x.ReadImportAnalysisExportRowsAsync(42, entity, "Merge", It.IsAny<CancellationToken>()))
                .Returns(Rows(entity));
        }
        repository.Setup(x => x.GetImportAnalysisSummaryAsync(42, "Merge", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportAnalysisSummary());
        repository.Setup(x => x.GetImportChangesAsync(42, null, null, 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportChanges
            {
                FileLogId = 42,
                Kinds = new[] { new ImportChangeKind { Kind = "TERMINATED", Label = "Leavers and termination dates", Changes = 1 } },
                Items = new[] { new ImportChange { Kind = "TERMINATED", Entity = "Employee", RowNumber = 7, Name = "JANE DOE", Field = "Termination date",
                                                   NewValue = "06/30/2026", Note = "Leaving", TotalCount = 1 } },
            });
        await using var stream = await ImportAnalysisExcelExporter.CreateAsync(repository.Object, 42,
            "Example.xlsx", 2026, "Merge");
        using var workbook = new ExcelPackage(stream);
        Assert.Equal(new[] { "Employer", "Plan", "Employee", "Premium", "Dependents", "Charts", "What changed" },
            workbook.Workbook.Worksheets.Select(x => x.Name));
        var changed = workbook.Workbook.Worksheets["What changed"];
        Assert.Equal("Leavers and termination dates", changed.Cells[5, 1].GetValue<string>());
        Assert.Equal(1, changed.Cells[5, 2].GetValue<int>());
        Assert.Equal("JANE DOE", changed.Cells[8, 3].GetValue<string>());
        Assert.Equal("06/30/2026", changed.Cells[8, 6].GetValue<string>());
        var employees = workbook.Workbook.Worksheets["Employee"];
        Assert.Equal(20, employees.Dimension.End.Row); // 5 header rows plus ALL 15 records, not 10
        Assert.Equal("001-02-0003", employees.Cells[6, 10].GetValue<string>());
        Assert.Equal("=1+1", employees.Cells[6, 4].GetValue<string>());
        Assert.True(string.IsNullOrEmpty(employees.Cells[6, 4].Formula));
        Assert.Contains("SSN needs review", employees.Cells[6, 45].GetValue<string>());
        Assert.NotEqual(employees.Cells[6, 10].Style.Fill.BackgroundColor.Rgb,
            employees.Cells[7, 10].Style.Fill.BackgroundColor.Rgb);
        var charts = workbook.Workbook.Worksheets["Charts"];
        Assert.Equal(15, charts.Cells[12, 2].GetValue<int>());
        Assert.Equal(13, charts.Cells[8, 2].GetValue<int>());
        Assert.Equal(1, charts.Cells[9, 2].GetValue<int>());
        Assert.Equal(1, charts.Cells[10, 2].GetValue<int>());
        Assert.Equal(15, charts.Cells[28, 7].GetValue<int>()); // Employee impact total
        Assert.Single(charts.Drawings);
        repository.VerifyAll();
    }

    private static async IAsyncEnumerable<IDictionary<string, object?>> Rows(string entity)
    {
        await Task.CompletedTask;
        if (entity != "Employee") yield break;
        for (int i = 0; i < 15; i++)
            yield return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["rowNumber"] = i + 1, ["rowSource"] = "Upload",
                ["actionType"] = i == 14 ? "EXCLUDED" : "INSERT",
                ["validationStatus"] = i == 0 ? "Warning" : i == 14 ? "Error" : "Clean",
                ["PrimaryEIN"] = "=1+1", ["EmployeeSSN"] = "001-02-0003",
                ["issuesJson"] = i == 0 ? "[{\"column\":\"EmployeeSSN\",\"severity\":\"Warning\",\"message\":\"SSN needs review\"}]" : "[]"
            };
    }
}
