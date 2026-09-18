using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Services
{
    public class HealthCheckService
    {
        private readonly string _connectionString;

        public HealthCheckService(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<object> RunCheck()
        {
            var result = new Dictionary<string, string>();
            var sw = new Stopwatch();

            // DB Test
            try
            {
                sw.Restart();
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();
                result["Database"] = $"OK ({sw.ElapsedMilliseconds}ms)";
            }
            catch (Exception ex)
            {
                result["Database"] = $"FAILED: {ex.Message}";
            }

            // Add more checks later: SMTP, Redis, API, Disk space etc.
            return result;
        }
    }
}
