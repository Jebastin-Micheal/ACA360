using System;
using System.Collections.Generic;

namespace ACA360.Core.Models.Dashboard
{
    public class EmployerDashboardDto
    {
        // Snapshot Tiles Summary Counts
        public int TotalWaived { get; set; }
        public int TotalCobra { get; set; }
        public int TotalRetiree { get; set; }
        public int TotalUnion { get; set; }

        // Employee Lifecycle Summary Dates
        public DateTime? MostRecentHire { get; set; }
        public DateTime? MostRecentTermination { get; set; }

        // System Communications Count
        public int MessagesRead { get; set; }
        public int MessagesUnread { get; set; }

        // Tracker Progress Properties
        public string CurrentProcessStep { get; set; }
        public DateTime? ProcessDueDate { get; set; }
        public int ProcessCompletePercent { get; set; } // Percentage range: 0..100

        // Explicitly Computed Deadlines (Client Clocks)
        public DateTime MailingDeadline { get; set; }     // Evaluates to Year+1-03-01
        public DateTime EfilingDeadline { get; set; }     // Evaluates to Year+1-03-31

        // 12-Month Array Sequence Container for Chart Targets
        public List<MonthlyDashboardRow> Monthly { get; set; } = new List<MonthlyDashboardRow>();
        // Snapshot Tiles Summary Counts
        public int FtReceivingForms { get; set; }
        public int PtReceivingForms { get; set; }

        // ADDED — widgets 5, 8 and 10. Supplied by sp_GetEmployerDashboardCounts
        // as amended in Pack 3 Part 1 section 4.
        public int TotalEmployees { get; set; }
        public int TotalFullTime { get; set; }
        public int TotalPartTime { get; set; }

        public int TotalEnrolled { get; set; }
    }

    public class MonthlyDashboardRow
    {
        public int MonthNum { get; set; } // Sequence: 1 = Jan .. 12 = Dec
        public int FullTime { get; set; }
        public int TotalEmployed { get; set; }
        public int Enrolled { get; set; }
        public int Waived { get; set; }
        public int Cobra { get; set; }
        public int Retiree { get; set; }
        public int Union { get; set; }
    }
}