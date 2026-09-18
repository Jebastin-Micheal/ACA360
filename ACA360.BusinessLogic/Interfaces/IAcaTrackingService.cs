using ACA360.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Interfaces
{
    public interface IAcaTrackingService
    {
        Task<AcaTrackingViewModel> GetTrackingSummaryAsync(int employerId, int year);
        Task<List<ConsentByLocation>> GetConsentByLocationAsync(int employerId, int year);
        Task<List<ConsentTrendPoint>> GetConsentTrendAsync(int employerId, int months, int year);
        Task<List<RecentConsentActivity>> GetRecentActivityAsync(int employerId, int top, int year);
        Task SendConsentReminderAsync(int employerId, int year);
    }
}
