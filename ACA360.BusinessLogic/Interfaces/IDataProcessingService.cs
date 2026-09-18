using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hangfire;
namespace ACA360.BusinessLogic.Interfaces
{
    public interface IDataProcessingService
    {
        /// <summary>
        /// Orchestrates the entire process of extracting data from a validated Excel file,
        /// loading it into staging tables, and running business rule validations.
        /// This method is designed to be called by a background job processor like Hangfire.
        /// </summary>
        /// <param name="fileLogId">The ID of the file log entry to be processed.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task ExtractAndValidateDataAsync(int fileLogId);
        /// <summary>
        /// Merges a validated file into the live tables and runs the ACA calculations.
        /// Runs as a Hangfire job because the work routinely takes minutes: the request
        /// thread used to be held for the whole of it, behind a page loader.
        ///
        /// The file is already at WS.Importing before this is queued -- that write is
        /// what stops a second click enqueuing a second run, because ImportData's
        /// Gate 2 refuses a file that is not in an importable state.
        /// </summary>
        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 3600)]
        Task ImportToLiveAsync(int fileLogId, string importMode, string userId);

        /// <summary>
        /// Releases files stranded at WS.Importing by a job that stopped without
        /// reporting a result -- an application restart, a recycled worker, a killed
        /// process. Without this they show a spinner forever and the poller never stops.
        /// </summary>
        [AutomaticRetry(Attempts = 0)]
        Task RecoverStuckImportsAsync(int staleMinutes = 90);
    }
}
