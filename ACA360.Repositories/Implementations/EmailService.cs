using ACA360.Core.Models;
using ACA360.Repositories.Interfaces;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Repositories.Implementations
{
    public class EmailService : IEmailService
    {
        private readonly IEmailRepository _repository;

        public EmailService(IEmailRepository repository)
        {
            _repository = repository;
        }

        public async Task<IEnumerable<EmailItem>> GetInboxAsync(bool isDeleted, string userId) => await _repository.GetEmailsAsync(isDeleted, userId);

        public async Task<IEnumerable<EmailItem>> GetTrashAsync(bool isDeleted, string userId) => await _repository.GetEmailsAsync(isDeleted, userId);

        public async Task MoveToTrashAsync(string id, bool isDeleted, string userId) => await _repository.MoveToTrashAsync(id, isDeleted, userId);

        public async Task ToggleStarAsync(string id, bool isStarred, string userId) => await _repository.ToggleStarAsync(id, isStarred, userId);

        public async Task ToggleReadAsync(string id, bool isRead, string userId) => await _repository.ToggleReadAsync(id, isRead, userId);

        public async Task AssignLabelAsync(string id, string labelId, string userId) => await _repository.AssignLabelAsync(id, labelId, userId);

        public async Task<IEnumerable<Label>> GetLabelsAsync(string userId) => await _repository.GetLabelsAsync(userId);

        public async Task ComposeEmailAsync(EmailItem email)
        {
            email.Id = Guid.NewGuid().ToString(); // Generate unique ID for the email
            // Leave ReceivedDate empty/null as requested to avoid default DateTime database issues
            await _repository.CreateEmailAsync(email);
        }

        public async Task AddLabelAsync(Label label)
        {
            label.Id = Guid.NewGuid().ToString(); // Generate unique ID for the new label
            await _repository.CreateLabelAsync(label);
        }

        public async Task UpdateLabelAsync(Label label) => await _repository.UpdateLabelAsync(label);

        public async Task DeleteLabelAsync(string id, string userId) => await _repository.DeleteLabelAsync(id, userId);
    }
}
