using ACA360.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Repositories.Interfaces
{
    public interface IEmailService
    {
        Task<IEnumerable<EmailItem>> GetInboxAsync(bool isDeleted, string userId);
        Task<IEnumerable<EmailItem>> GetTrashAsync(bool isDeleted, string userId);
        Task ComposeEmailAsync(EmailItem email);
        Task MoveToTrashAsync(string id, bool isDeleted, string userId);
        Task ToggleStarAsync(string id, bool isStarred, string userId);
        Task ToggleReadAsync(string id, bool isRead, string userId);
        Task AssignLabelAsync(string id, string labelId, string userId);
        Task<IEnumerable<Label>> GetLabelsAsync(string userId);
        Task AddLabelAsync(Label label);
        Task UpdateLabelAsync(Label label);
        Task DeleteLabelAsync(string id, string userId);
    }
}
