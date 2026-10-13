using System;
using System.Linq;

namespace ACA360.Core.Extensions
{
    public static class DateTimeExtensions
    {
        public static DateTime AddBusinessDays(this DateTime dt, int numberOfDays)
        {
            if (numberOfDays <= 0) return dt;

            DateTime futureDate = dt;
            DayOfWeek[] weekend = { DayOfWeek.Saturday, DayOfWeek.Sunday };
            int businessDayCountDown = numberOfDays;

            while (businessDayCountDown != 0)
            {
                futureDate = futureDate.AddDays(1);
                if (!weekend.Contains(futureDate.DayOfWeek))
                {
                    businessDayCountDown--;
                }
            }

            return futureDate;
        }
    }
}