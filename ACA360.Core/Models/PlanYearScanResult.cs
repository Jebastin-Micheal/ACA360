using System;
using System.Collections.Generic;
using System.Linq;

namespace ACA360.Core.Models
{
    /// <summary>
    /// What a quick scan of an uploaded workbook says about the period the data
    /// actually describes, compared with the filing year the user picked on the
    /// upload form.
    ///
    /// This exists because those two can disagree silently. A file whose premium
    /// banding windows sit in 2024, filed as 2026, imports without a murmur and
    /// then produces a 1095-C with no Line 15 on any employee and no affordability
    /// safe harbour anywhere, because sp_Generate1095Codes builds its premium
    /// lookup from the filing year's months and finds nothing to join to.
    /// </summary>
    public class PlanYearScanResult
    {
        /// <summary>False when the file could not be read at all. Never block on this.</summary>
        public bool Scanned { get; set; }

        public int PlanYear { get; set; }

        // -- Premium banding windows. The decisive signal: if none of these cover
        //    the filing year, Line 15 will be blank on every form produced.
        public int PremiumRowsFound { get; set; }
        public int PremiumRowsCoveringYear { get; set; }
        public DateTime? PremiumEarliest { get; set; }
        public DateTime? PremiumLatest { get; set; }

        // -- Employee coverage periods. Supporting signal.
        public int CoverageRowsFound { get; set; }
        public int CoverageRowsCoveringYear { get; set; }

        /// <summary>Years the file's periods actually touch, lowest first.</summary>
        public List<int> YearsInFile { get; set; } = new List<int>();

        /// <summary>Human-readable reasons the chosen year looks wrong. Empty means no objection.</summary>
        public List<string> Warnings { get; set; } = new List<string>();

        public bool HasWarnings => Warnings.Count > 0;

        /// <summary>The year the data most looks like, when there is an obvious candidate.</summary>
        public int? SuggestedYear { get; set; }

        public string YearsSummary =>
            YearsInFile.Count == 0 ? "none found"
            : YearsInFile.Count == 1 ? YearsInFile[0].ToString()
            : $"{YearsInFile.First()}–{YearsInFile.Last()}";
    }
}
