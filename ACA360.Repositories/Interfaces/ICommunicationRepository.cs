using ACA360.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Repositories.Interfaces
{
    public interface ICommunicationRepository
    {
        Task CreateLogAsync(CommunicationLog log);
        Task<IEnumerable<CommunicationLog>> GetLogsForEmployerAsync(string employerEIN);
        /// <summary>
        /// Records an email in CommunicationLog.
        /// <paramref name="status"/> is what actually happened — Sent, Failed, Skipped
        /// or Disabled. It used to be left NULL, so a row's mere existence implied a
        /// delivery that never occurred (F-37).
        /// </summary>
        Task LogEmailAsync(int entityId, string entityType, string type, string sentTo, int sentBy,
                           string subject, string body, string? status = null);
    }
}
