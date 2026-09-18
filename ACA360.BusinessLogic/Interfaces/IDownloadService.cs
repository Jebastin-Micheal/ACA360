using ACA360.Core.Models;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Interfaces
{
    public interface IDownloadService
    {
        /// <summary>
        /// The download hub, scoped to what the caller may see. Takes the resolved
        /// employer set rather than a user id and role: entitlement is decided once
        /// by IViewAsService and passed in, so this service cannot disagree with the
        /// rest of the application about who can see what.
        /// </summary>
        /// <param name="permittedEmployerIds">Employers the caller is entitled to.</param>
        /// <param name="unrestricted">True for roles that legitimately see every employer.</param>
        Task<DownloadHubViewModel> GetDownloadsDashboardAsync(
            IEnumerable<long> permittedEmployerIds, bool unrestricted);

        /// <summary>
        /// Whether the caller may download a specific generated archive. Uses the
        /// same predicate as the listing, so a file can never be fetchable without
        /// being listable.
        /// </summary>
        Task<bool> CanAccessFileAsync(
            string fileName, IEnumerable<long> permittedEmployerIds, bool unrestricted);

        Task<Stream> GetFileBytesAsync(string fileName);

        Task<byte[]> GenerateCodeReportAsync(int employerId, int year);
        Task<byte[]> GeneratePenaltyReportAsync(int employerId, int year);
        Task<byte[]> GenerateFullEmployerReportAsync(int employerId, int year);
    }
}