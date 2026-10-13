using System.Globalization;
using ACA360.Core.Models;

namespace ACA360.BusinessLogic.Services
{
    /// <summary>
    /// "Why this code?": puts into words how sp_Generate1095Codes arrived at a month's Line 14,
    /// 15 and 16, from the values it kept (dbo.EmployeeCodeMonthDetail, script 058). The rules
    /// here follow the procedure's order; they describe its result, they do not recalculate it.
    /// </summary>
    public static class CodeExplainer
    {
        public static readonly string[] MonthKeys = { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };

        private static string Money(decimal? value) => value == null ? "—" : "$" + value.Value.ToString("#,##0.00", CultureInfo.InvariantCulture);
        private static string Pct(decimal? value) => value == null ? "—" : (value.Value * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";
        private static string Day(DateTime? value) => value?.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture) ?? "—";
        private static string? Blank(string? code) => string.IsNullOrWhiteSpace(code) ? null : code.Trim();

        /// <summary>The rate-of-pay limit: hourly rate × full-time hours × the year's percentage.</summary>
        public static decimal? RateOfPayLimit(CodeMonthDetail d) =>
            d.HourlyRate > 0 && d.FullTimeHours != null && d.AffordabilityPct != null
                ? Math.Round(d.HourlyRate.Value * d.FullTimeHours.Value * d.AffordabilityPct.Value, 2) : null;

        /// <summary>The W-2 limit: W-2 pay ÷ months employed × the year's percentage.</summary>
        public static decimal? W2Limit(CodeMonthDetail d) =>
            d.AnnualW2 > 0 && d.HiredMonths > 0 && d.AffordabilityPct != null
                ? Math.Round(d.AnnualW2.Value / d.HiredMonths.Value * d.AffordabilityPct.Value, 2) : null;

        public static CodeExplanation Build(int employeeId, IDictionary<string, object?> header, IReadOnlyList<CodeMonthDetail> details)
        {
            object? H(string key) => header.TryGetValue(key, out var v) ? v : null;
            var first = details.FirstOrDefault();
            var result = new CodeExplanation
            {
                EmployeeId = employeeId,
                EmployeeCodeId = Convert.ToInt32(H("EmployeeCodeId") ?? 0),
                EmployerId = Convert.ToInt32(H("EmployerId") ?? 0),
                FilingYear = Convert.ToInt32(H("FilingYear") ?? 0),
                EmployeeName = H("EmployeeName") as string,
                EmployerName = H("EmployerName") as string,
                IsLocked = H("IsLocked") is true,
                DisableCoding = H("DisableCoding") is true,
                HasDetail = details.Count > 0,
                CalculatedAt = first?.CalculatedAt,
                MonthlyFpl = first?.MonthlyFpl,
                AffordabilityPct = first?.AffordabilityPct,
                FullTimeHours = first?.FullTimeHours,
                HourlyRate = first?.HourlyRate,
                AnnualW2 = first?.AnnualW2,
            };

            for (int m = 1; m <= 12; m++)
            {
                var key = MonthKeys[m - 1];
                var month = new CodeExplanationMonth
                {
                    Month = m,
                    MonthName = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(m),
                    Line14 = Blank(H(key + "_COC") as string),
                    Line15 = H(key + "_LCMP") is { } l15 ? Convert.ToDecimal(l15, CultureInfo.InvariantCulture) : null,
                    Line16 = Blank(H(key + "_SHC") as string),
                };
                var d = details.FirstOrDefault(x => x.MonthNum == m);
                if (d != null)
                {
                    Explain(d, month);
                    month.ChangedByHand = !string.Equals(month.Line14, Blank(d.Line14), StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(month.Line16, Blank(d.Line16), StringComparison.OrdinalIgnoreCase)
                        || month.Line15 != d.Line15;
                }
                result.Months.Add(month);
            }
            return result;
        }

        /// <summary>Fills a month's calculated codes and the reasons for them.</summary>
        public static void Explain(CodeMonthDetail d, CodeExplanationMonth month)
        {
            month.Detail = d;
            month.Calculated14 = Blank(d.Line14);
            month.Calculated15 = d.Line15;
            month.Calculated16 = Blank(d.Line16);
            var l14 = month.Calculated14 ?? "";
            var l16 = month.Calculated16;
            var plan = string.IsNullOrWhiteSpace(d.PlanName) ? "the plan" : $"plan \"{d.PlanName!.Trim()}\"";
            string Others() =>
                (d.OfferedSpouse == 1 ? (d.OfferedDependents == 1 ? ", spouse and dependents" : " and spouse")
                 : d.ConditionalSpouse == 1 ? (d.OfferedDependents == 1 ? ", spouse (conditionally) and dependents" : " and spouse (conditionally)")
                 : d.OfferedDependents == 1 ? " and dependents" : "");

            // ---- Line 14: the offer
            if (d.Employed == false)
            {
                month.Why14.Add($"Not employed in {month.MonthName}: hired {Day(d.HireDate)}"
                    + (d.TerminationDate != null ? $", left {Day(d.TerminationDate)}." : "."));
            }
            else if (d.IsIchra == true)
            {
                if (l14 == "1S") month.Why14.Add("Not full-time; an individual coverage HRA (ICHRA) is offered: 1S.");
                else if (d.IchraNetCost == null) month.Why14.Add("An ICHRA is offered, but no lowest-cost silver plan premium is on file for the month, so no offer can be reported: 1H.");
                else if (l14 == "1R") month.Why14.Add($"An ICHRA is offered but is not affordable: the net cost {Money(d.IchraNetCost)} (silver plan {Money(d.IchraLcsp)} less allowance {Money(d.IchraAllowance)}) is above every limit: 1R.");
                else month.Why14.Add($"An affordable ICHRA is offered to the employee{Others()}: net cost {Money(d.IchraNetCost)} (silver plan {Money(d.IchraLcsp)} less allowance {Money(d.IchraAllowance)}): {l14}.");
            }
            else if (d.PlanId == null)
            {
                month.Why14.Add("No coverage offered: no enrolment or offer is on file for the month, and the employee's \"lowest cost plan offered\" matches none of the employer's plans: 1H.");
            }
            else if (l14 == "1G")
            {
                month.Why14.Add($"Not full-time, but enrolled in the self-insured {plan}: 1G.");
            }
            else if (d.MinimumValue == false)
            {
                month.Why14.Add($"{Cap(plan)} is offered but does not provide minimum value: 1F.");
            }
            else if (l14 == "1A")
            {
                month.Why14.Add($"Qualifying offer: {plan} is marked as qualifying, is offered to the spouse and dependents, and the employee's monthly share {Money(d.Premium)} is within the FPL limit of {Money(d.MonthlyFpl)}: 1A.");
            }
            else
            {
                month.Why14.Add($"{Cap(plan)} (minimum value) is offered to the employee{Others()}: {l14}.");
            }

            // ---- Line 15: the employee's share
            if (month.Calculated15 != null)
                month.Why15 = d.IsIchra == true
                    ? $"The ICHRA's net cost to the employee: {Money(month.Calculated15)} a month."
                    : $"The employee's monthly share of the lowest-cost self-only coverage: {Money(month.Calculated15)}.";
            else if (l14 == "1A")
                month.Why15 = "Left blank with 1A.";

            // ---- Line 16: why no penalty applies (or why one may)
            var share = d.IsIchra == true ? d.IchraNetCost : d.Premium;
            var rop = RateOfPayLimit(d);
            var w2 = W2Limit(d);
            switch (l16)
            {
                case "2A" when d.Employed == false:
                    month.Why16.Add("2A: not employed in the month.");
                    break;
                case "2A":
                    month.Why16.Add("2A: on COBRA in the month.");
                    break;
                case "2E":
                    month.Why16.Add("2E: covered through a union (multiemployer) arrangement.");
                    break;
                case "2C":
                    month.Why16.Add("2C: enrolled in the coverage offered.");
                    break;
                case "2D":
                    month.Why16.Add($"2D: in the waiting period (hired {Day(d.HireDate)}"
                        + (d.EligibleDate != null ? $", eligible from {Day(d.EligibleDate)})." : ")."));
                    break;
                case "2B":
                    month.Why16.Add("2B: not full-time in the month.");
                    break;
                case "2G":
                    month.Why16.Add($"2G, FPL safe harbor: the share {Money(share)} is within the FPL limit of {Money(d.MonthlyFpl)}.");
                    break;
                case "2H":
                    month.Why16.Add($"2H, rate-of-pay safe harbor: the share {Money(share)} is within {Money(d.HourlyRate)} an hour × {d.FullTimeHours} hours × {Pct(d.AffordabilityPct)} = {Money(rop)}.");
                    break;
                case "2F":
                    month.Why16.Add($"2F, W-2 safe harbor: the share {Money(share)} is within {Money(d.AnnualW2)} ÷ {d.HiredMonths} months employed × {Pct(d.AffordabilityPct)} = {Money(w2)}.");
                    break;
                case null when l14 is "1A" or "1G":
                    month.Why16.Add($"No Line 16 code is needed with {l14}.");
                    break;
                case null when l14 == "1H":
                    month.Why16.Add("No code: full-time, employed and not offered coverage. This month is a penalty risk (4980H(a)).");
                    month.PenaltyRisk = true;
                    break;
                case null:
                    var limits = new List<string> { $"the FPL limit {Money(d.MonthlyFpl)}" };
                    if (rop != null) limits.Add($"the rate-of-pay limit {Money(rop)}");
                    if (w2 != null) limits.Add($"the W-2 limit {Money(w2)}");
                    month.Why16.Add($"No safe harbor applies: the share {Money(share)} is above {string.Join(", ", limits)}."
                        + (rop == null && w2 == null ? " No pay is on file to test rate of pay or W-2." : "")
                        + " If the employee receives a premium tax credit this is a penalty risk (4980H(b)).");
                    break;
                default:
                    month.Why16.Add(l16 + ".");
                    break;
            }
        }

        private static string Cap(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
    }
}
