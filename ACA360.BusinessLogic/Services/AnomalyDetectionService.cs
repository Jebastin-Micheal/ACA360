using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using ACA360.BusinessLogic.Interfaces;

namespace ACA360.BusinessLogic.Services
{
    public class AnomalyDetectionService : IAnomalyDetectionService
    {
        private readonly string _connectionString;
        private readonly ILogger<AnomalyDetectionService> _logger;

        /// <summary>
        /// Purpose: Initializes the anomaly detection service with required dependencies.
        /// Input parameters: string connectionString, ILogger logger
        /// Output/return value: None
        /// </summary>
        public AnomalyDetectionService(string connectionString, ILogger<AnomalyDetectionService> logger)
        {
            try
            {
                _connectionString = connectionString;
                _logger = logger;
            }
            catch (Exception ex)
            {
                if (_logger != null)
                {
                    _logger.LogError(ex, "Error initializing AnomalyDetectionService.");
                }
                throw;
            }
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);

        /// <summary>
        /// Purpose: Analyzes employer data for anomalies and generates a health scorecard.
        /// Input parameters: int employerId, int year
        /// Output/return value: Task of DataHealthScorecard
        /// </summary>
        public async Task<DataHealthScorecard> AnalyzeEmployerDataAsync(int employerId, int year)
        {
            var scorecard = new DataHealthScorecard
            {
                EmployerId = employerId,
                Alerts = new List<AnomalyAlert>()
            };

            var alerts = scorecard.Alerts;

            try
            {
                using var db = Connection;

                // 1. HEURISTIC: Suspicious Uniformity (Copy-Paste Detection)
                var uniformity = await db.QueryFirstOrDefaultAsync<dynamic>(
                    "sp_DetectSuspiciousUniformity",
                    new { Id = employerId, Yr = year },
                    commandType: CommandType.StoredProcedure);

                if (uniformity != null)
                {
                    int total = (int)uniformity.Total;
                    int cnt = (int)uniformity.Cnt;

                    if (total > 10 && (double)cnt / total > 0.95)
                    {
                        alerts.Add(new AnomalyAlert
                        {
                            Title = "Suspicious Data Uniformity",
                            Description = $"{cnt} out of {total} employees have the exact same Plan Start Month ({uniformity.PlanStartMonth}).",
                            Severity = "Medium",
                            AffectedRecordCount = cnt,
                            Suggestion = "Verify if this is accurate or a copy-paste error from the data template."
                        });
                    }
                }

                // 2. HEURISTIC: Code vs Dependent Mismatch
                int mismatchCount = await db.ExecuteScalarAsync<int>(
                    "sp_DetectOfferCodeMismatch",
                    new { Id = employerId, Yr = year },
                    commandType: CommandType.StoredProcedure);

                if (mismatchCount > 0)
                {
                    alerts.Add(new AnomalyAlert
                    {
                        Title = "Offer Code Mismatch",
                        Description = $"{mismatchCount} employees have a 'Self-Only' offer (Code 1B) but have covered individuals listed.",
                        Severity = "High",
                        AffectedRecordCount = mismatchCount,
                        Suggestion = "Check if these employees should have a Family Code (1E) or if the dependents are incorrect."
                    });
                }

                // 3. HEURISTIC: Age Outliers
                int ageCount = await db.ExecuteScalarAsync<int>(
                    "sp_DetectAgeOutliers",
                    new { Id = employerId },
                    commandType: CommandType.StoredProcedure);

                if (ageCount > 0)
                {
                    alerts.Add(new AnomalyAlert
                    {
                        Title = "Date of Birth Anomalies",
                        Description = $"{ageCount} employees are flagged as under 14 or over 100 years old.",
                        Severity = "Medium",
                        AffectedRecordCount = ageCount,
                        Suggestion = "Review DOBs for typos (e.g. 2025 instead of 1985)."
                    });
                }

                // 4. HEURISTIC: Full-Time but No Offer (The Penalty Magnet)
                int riskCount = await db.ExecuteScalarAsync<int>(
                    "sp_DetectCriticalPenaltyExposure",
                    new { Id = employerId, Yr = year },
                    commandType: CommandType.StoredProcedure);

                if (riskCount > 0)
                {
                    alerts.Add(new AnomalyAlert
                    {
                        Title = "Critical Penalty Exposure",
                        Description = $"{riskCount} Full-Time employees have 'No Offer' (1H) and no Safe Harbor code.",
                        Severity = "Critical",
                        AffectedRecordCount = riskCount,
                        Suggestion = "These records trigger 4980H(a) penalties. Review immediately."
                    });
                }

                // --- CALCULATE SCORE ---
                int score = 100;
                foreach (var a in alerts)
                {
                    if (a.Severity == "Critical") score -= 20;
                    else if (a.Severity == "High") score -= 10;
                    else if (a.Severity == "Medium") score -= 5;
                    else score -= 2;
                }

                score = Math.Max(0, score);
                scorecard.HealthScore = score;
                scorecard.Alerts = alerts
                    .OrderBy(a => a.Severity == "Critical" ? 0 : 1)
                    .ThenByDescending(a => a.AffectedRecordCount)
                    .ToList();

                if (score >= 90) scorecard.Rating = "Excellent";
                else if (score >= 70) scorecard.Rating = "Good";
                else if (score >= 50) scorecard.Rating = "Risky";
                else scorecard.Rating = "Critical";

                return scorecard;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in AnalyzeEmployerDataAsync for EmployerId: {EmployerId}", employerId);

                scorecard.HealthScore = 0;
                scorecard.Rating = "Error";
                scorecard.Alerts.Add(new AnomalyAlert
                {
                    Title = "Analysis Failed",
                    Description = ex.Message,
                    Severity = "Critical",
                    AffectedRecordCount = 0,
                    Suggestion = "Contact support."
                });

                throw;
            }
        }
    }
}