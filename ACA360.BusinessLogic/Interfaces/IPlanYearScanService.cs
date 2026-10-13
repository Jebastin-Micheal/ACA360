using System.IO;
using ACA360.Core.Models;

namespace ACA360.BusinessLogic.Interfaces
{
    public interface IPlanYearScanService
    {
        /// <summary>
        /// Reads the period columns out of an uploaded workbook and reports whether
        /// they line up with the filing year the user selected.
        ///
        /// Never throws: a file it cannot parse comes back with Scanned = false and
        /// no warnings, because the structure validator is what should reject an
        /// unreadable file, not this.
        /// </summary>
        PlanYearScanResult Scan(Stream stream, int planYear);
    }
}
