using System;
using System.Data;
using System.Linq;

namespace ACA360.BusinessLogic.Parsing
{
    public class FileDateAnalyzer
    {
        public (DateTime? Start, DateTime? End) DetectPeriod(DataSet data)
        {
            DateTime? minDate = null;
            DateTime? maxDate = null;

            // 1. Check "Premiums" Table (Best Source)
            if (data.Tables.Contains("Premiums"))
            {
                var dt = data.Tables["Premiums"];
                foreach (DataRow row in dt.Rows)
                {
                    // Look for "StartDate" column (as defined in your Fixed Schema)
                    if (dt.Columns.Contains("StartDate") && DateTime.TryParse(row["StartDate"]?.ToString(), out DateTime start))
                    {
                        if (minDate == null || start < minDate) minDate = start;
                    }

                    // Look for "EndDate" column
                    if (dt.Columns.Contains("EndDate") && DateTime.TryParse(row["EndDate"]?.ToString(), out DateTime end))
                    {
                        if (maxDate == null || end > maxDate) maxDate = end;
                    }
                }
            }

            // 2. Check "Employees" Table (Backup Source)
            // Only if Premiums didn't give us a full picture
            if (data.Tables.Contains("Employees") && (minDate == null || maxDate == null))
            {
                var dt = data.Tables["Employees"];
                foreach (DataRow row in dt.Rows)
                {
                    // Coverage Start Date
                    if (dt.Columns.Contains("CoverageStartDate") && DateTime.TryParse(row["CoverageStartDate"]?.ToString(), out DateTime start))
                    {
                        if (minDate == null || start < minDate) minDate = start;
                    }

                    // Coverage End Date 
                    if (dt.Columns.Contains("CoverageEndDate") && DateTime.TryParse(row["CoverageEndDate"]?.ToString(), out DateTime end))
                    {
                        if (maxDate == null || end > maxDate) maxDate = end;
                    }
                }
            }

            // 3. Sanity Check (Avoid "1900" or "2099" typos ruining the DB)
            // Example rule: Date must be within +/- 2 years of today
            if (minDate.HasValue && (minDate.Value.Year < DateTime.Now.Year - 2 || minDate.Value.Year > DateTime.Now.Year + 1))
                minDate = null;

            if (maxDate.HasValue && (maxDate.Value.Year < DateTime.Now.Year - 2 || maxDate.Value.Year > DateTime.Now.Year + 1))
                maxDate = null;

            return (minDate, maxDate);
        }
    }
}