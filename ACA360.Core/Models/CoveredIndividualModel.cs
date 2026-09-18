using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Models
{
    public class CoveredIndividualModel
    {
        public int Id { get; set; }
        public int? EmployeeCodeId { get; set; }

        public string? FirstName { get; set; }
        public string? MiddleName { get; set; }
        public string? LastName { get; set; }
        public string? SSN { get; set; }
        public string? Suffix { get; set; }

        public DateTime? Birthday { get; set; }
        public DateTime? CoverageStartDate { get; set; }
        public DateTime? CoverageEndDate { get; set; }
        [NotMapped]
        public int? FilingYear { get; set; }
        [NotMapped]
        public string? EmployeeId { get; set; }

        // Monthly Coverage Status
        [NotMapped]
        public bool AllM { get; set; }
        [NotMapped]
        public bool Jan { get; set; }
        [NotMapped]
        public bool Feb { get; set; }
        [NotMapped]
        public bool Mar { get; set; }
        [NotMapped]
        public bool Apr { get; set; }
        [NotMapped]
        public bool May { get; set; }
        [NotMapped]
        public bool Jun { get; set; }
        [NotMapped]
        public bool Jul { get; set; }
        [NotMapped]
        public bool Aug { get; set; }
        [NotMapped]
        public bool Sep { get; set; }
        [NotMapped]
        public bool Oct { get; set; }
        [NotMapped]
        public bool Nov { get; set; }
        [NotMapped]
        public bool Dec { get; set; }
        // Tracking field
        [NotMapped]
        public bool IsDependent { get; set; }
        [NotMapped]
        public bool CI_chk_disable_coding { get; set; }
    }
}
