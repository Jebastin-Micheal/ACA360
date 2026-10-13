using ACA360.Core.Models;

namespace ACA360.Tests;

public class ImportAnalysisRequestTests
{
    [Theory]
    [InlineData("Employer", 16)]
    [InlineData("Plan", 13)]
    [InlineData("Premium", 9)]
    [InlineData("Employee", 39)]
    [InlineData("Dependent", 12)]
    public void SuppliedWorkbookColumnsAreAvailableInOrder(string entity, int count)
    {
        var sheet = ImportAnalysisCatalog.Find(entity)!;
        Assert.Equal(count, sheet.Columns.Count);
        Assert.Equal("PrimaryEIN", sheet.Columns[0].Key);
        Assert.Equal(count, sheet.Columns.Select(c => c.Key).Distinct().Count());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(int.MaxValue)]
    public void UnboundedOrInvalidPagesAreRejected(int length)
    {
        var request = new ImportAnalysisRequest { FileLogId = 42, Length = length };
        Assert.NotNull(request.Validate());
    }

    [Theory]
    [InlineData("Employee", "EmployeeSSN", "asc")]
    [InlineData("Premium", "EEMonthlyContribution", "desc")]
    [InlineData("DependENT", "rowNumber", "asc")]
    public void KnownColumnAndPagingRequestsAreAccepted(string entity, string sort, string direction)
    {
        var request = new ImportAnalysisRequest { FileLogId = 42, Entity = entity, SortColumn = sort,
            SortDirection = direction, Draw = 3, Start = 20, Length = 10 };
        Assert.Null(request.Validate());
        Assert.Equal(20, request.Start);
        Assert.Equal(10, request.Length);
    }

    [Fact]
    public void UpperCasedWebRequestIsAcceptedAndNormalised()
    {
        // The web app's JSON converter upper-cases every string before binding.
        var request = new ImportAnalysisRequest { FileLogId = 42, Entity = "EMPLOYEE", ImportMode = "MERGE",
            SortColumn = "EMPLOYEESSN", SortDirection = "DESC", ImpactFilter = "UPDATE", ValidationFilter = "ERROR" };
        Assert.Null(request.Validate());
        Assert.Equal("Employee", request.Entity);
        Assert.Equal("EmployeeSSN", request.SortColumn);
        Assert.Equal("DESC", request.SortDirection);
        Assert.Equal("UPDATE", request.ImpactFilter);
        Assert.Equal("ERROR", request.ValidationFilter);
    }

    [Fact]
    public void ArbitrarySqlIdentifiersAndSortDirectionsAreRejected()
    {
        Assert.NotNull(new ImportAnalysisRequest { FileLogId = 42, Entity = "Users" }.Validate());
        Assert.NotNull(new ImportAnalysisRequest { FileLogId = 42, SortColumn = "EmployerName; DROP TABLE Employee" }.Validate());
        Assert.NotNull(new ImportAnalysisRequest { FileLogId = 42, SortDirection = "desc;--" }.Validate());
        Assert.NotNull(new ImportAnalysisRequest { FileLogId = 42, Entity = "Employer", SortColumn = "EmployeeSSN" }.Validate());
    }

    [Fact]
    public void InvalidModesFiltersAndOffsetsAreRejected()
    {
        Assert.NotNull(new ImportAnalysisRequest { FileLogId = 42, ImportMode = "ReplaceEverything" }.Validate());
        Assert.NotNull(new ImportAnalysisRequest { FileLogId = 42, ImpactFilter = "DeleteAll" }.Validate());
        Assert.NotNull(new ImportAnalysisRequest { FileLogId = 42, ValidationFilter = "Unchecked" }.Validate());
        Assert.NotNull(new ImportAnalysisRequest { FileLogId = 42, Start = -10 }.Validate());
        Assert.NotNull(new ImportAnalysisRequest { FileLogId = 42, Draw = -1 }.Validate());
        Assert.NotNull(new ImportAnalysisRequest { FileLogId = 0 }.Validate());
        Assert.NotNull(new ImportAnalysisRequest { FileLogId = 42, Search = new string('x', 201) }.Validate());
    }
}
