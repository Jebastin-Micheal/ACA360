using ACA360.Core.Models;

namespace ACA360.BusinessLogic.Interfaces
{
    /// <summary>The Dummy Data page (SuperAdmin): made-up import workbooks for testing uploads.</summary>
    public interface IDummyDataService
    {
        /// <summary>Main employers (no parent) to choose from, one per EIN, newest filing year's name.</summary>
        Task<List<DummyEmployer>> GetMainEmployersAsync();

        /// <summary>Filing years that have employers, newest first.</summary>
        Task<List<int>> GetFilingYearsAsync();

        /// <summary>
        /// One workbook per chosen employer, filled in from the import template at
        /// <paramref name="templatePath"/>; several come back as one zip. Nothing is written to the database.
        /// </summary>
        Task<(byte[] Content, string FileName, string ContentType, List<DummyFileSummary> Files)> GenerateAsync(
            DummyDataRequest request, string templatePath);
    }
}
