using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Models
{
    public class FilingYearModel
    {
        public int? Id { get; set; }         // Was long? — changed to int?
        public int? FilingYear { get; set; }

        public int Year { get; set; }
        public decimal FpgPremium { get; set; }

        public decimal FpgPercent { get; set; }

        public int RopHours { get; set; }

        public decimal OfferPercent { get; set; }

        public decimal PenaltyAAnnual { get; set; }

        public decimal PenaltyBAnnual { get; set; }

        public bool IsActive { get; set; } = true;
    }
    public class FilingYearRules
    {
        public int FilingYear { get; set; }
        public decimal FpgPremium { get; set; }
        public decimal FpgPercent { get; set; }
        public int RopHours { get; set; }
        public decimal OfferPercent { get; set; }
        public decimal PenaltyAAnnual { get; set; }

        public decimal PenaltyBAnnual { get; set; }
    }
}
