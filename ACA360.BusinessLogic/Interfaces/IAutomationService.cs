using ACA360.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Interfaces
{
    /// <summary>
    /// Defines the contract for services that run automated, scheduled tasks.
    /// </summary>
    public interface IAutomationService
    {
        //Task RunNightlyProcessing();
        Task<IEnumerable<AutomationJobDto>> GetJobsAsync();
        Task UpdateJobAsync(string jobKey, string cron, bool enabled);
        Task RunJobNowAsync(string jobKey);
    }
}
