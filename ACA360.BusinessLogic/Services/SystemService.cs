using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Services
{
    public class SystemService : ISystemService
    {
        private readonly ISystemSettingsService _settings;
        private readonly HealthCheckService _health;
        private readonly ILogger<SystemService> _logger;

        public SystemService(
            ISystemSettingsService settings,
            HealthCheckService health,
            ILogger<SystemService> logger)
        {
            _settings = settings;
            _health = health;
            _logger = logger;
        }

        public async Task<object> RunHealthCheckAsync()
        {
            return await _health.RunCheck();
        }

        public async Task<(bool success, string message)> TestSmtpConnectionAsync()
        {
            try
            {
                var s = await _settings.GetSmtpSettingsAsync();
                if (string.IsNullOrEmpty(s.Host)) return (false, "SMTP Host is missing.");

                using var client = new SmtpClient(s.Host, s.Port);
                client.EnableSsl = s.EnableSsl;
                if (!string.IsNullOrEmpty(s.Username))
                {
                    // Masking password for test (password is from DB record)
                    client.Credentials = new NetworkCredential(s.Username, s.Password);
                }

                var mail = new MailMessage
                {
                    From = new MailAddress(s.Username ?? "test@aca360.com"),
                    Subject = "ACA360 SMTP Test",
                    Body = "SMTP Configuration is working!",
                    IsBodyHtml = false
                };
                mail.To.Add(s.Username ?? "admin@aca360.com");

                await client.SendMailAsync(mail);
                return (true, "Connection successful! Test email sent.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SMTP test failed.");
                return (false, $"SMTP Connection Failed: {ex.Message}");
            }
        }

        public async Task<Stream> ExportAuditLogsToCsvAsync()
        {
            // Use a memory stream to avoid large string allocations on the LOH
            var ms = new MemoryStream();
            var sw = new StreamWriter(ms, Encoding.UTF8, 4096, leaveOpen: true);

            try
            {
                var logs = await _settings.GetAllAuditLogsAsync();
                await sw.WriteLineAsync("Activity Type,Detail,User,Date/Time");

                foreach (var log in logs)
                {
                    string d = log.ActionDetail?.Replace("\"", "\"\"") ?? "";
                    await sw.WriteLineAsync($"{log.ActivityType},\"{d}\",{log.UserName},{log.ActivityDate}");
                }
                
                await sw.FlushAsync();
                ms.Position = 0;
                return ms;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting audit logs to CSV.");
                sw.Dispose();
                ms.Dispose();
                throw;
            }
        }

        public async Task<(bool success, string message)> ValidateIrsFormatAsync()
        {
            var s = await _settings.GetIrsSettingsAsync();
            if (string.IsNullOrWhiteSpace(s.Tcc) || s.Tcc.Length != 5)
                return (false, "TCC must be exactly 5 characters.");
            if (string.IsNullOrWhiteSpace(s.TransmitterEin) || s.TransmitterEin.Length != 9)
                return (false, "Transmitter EIN must be 9 digits.");
            
            return (true, "Format validation passed.");
        }
    }
}
