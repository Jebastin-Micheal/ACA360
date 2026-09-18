using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Models
{
    public class Employee
    {
        public long RowNum { get; set; }
        public int? Id { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Suffix { get; set; }
        public string? SSN { get; set; }
        public string? EmployerID { get; set; }
        public int FilingYear { get; set; }

        public static implicit operator Employee(EmployeeBasicDetails v)
        {
            throw new NotImplementedException();
        }
        public long? EmployerId { get; set; } // Changed to long? to match some DB contexts, or int?
        public string? FullName { get; set; }
        public string? MiddleName { get; set; }
        public DateTime? Birthday { get; set; }
        public string? Email { get; set; }

        // Address Info
        public string? Address { get; set; }
        public string? Address2 { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public int? StateId { get; set; }
        public string? Zip { get; set; }
        public int? CountryId { get; set; }
        public short? IsForeign { get; set; } // Using short? to match existing DB types often used for bit/smallint

        // ACA Flags
        public short? IsExPatriot { get; set; }
        public short? IsCorrected { get; set; }

        // Primary compliance flag (synced from EmployeeFlags by sp_SyncEmployeePrimaryFlag)
        public int? FlagId { get; set; }
        public string? FlagDescription { get; set; }
        public string? FlagSeverity { get; set; } // Critical / Warning / Info — severity of the primary flag
        public bool IsMissingData { get; set; }   // no hire span / status / enrollment / payroll rows

        // Metadata
        public long? FileLogId { get; set; }

        // === NEW COLUMNS FOR LOGIC ENGINE & DASHBOARD ===
        // These MUST be present to fix your CS1061 errors

        public int Status { get; set; } // 1=FullTime, 0=PartTime
        public bool IsDeleted { get; set; }

        public DateTime? HireDate { get; set; }
        public DateTime? TerminationDate { get; set; }
        public DateTime? DateEligibleForCoverage { get; set; }

        public bool CoverageElected { get; set; }
        public DateTime? CoverageStartDate { get; set; }
        public DateTime? CoverageEndDate { get; set; }

        public string? LowestCostPlanOffered { get; set; }
    }

}
