using ACA360.BusinessLogic.Services;
using ACA360.Core.Models;

namespace ACA360.Tests.Unit.Services;

/// <summary>"Why this code?" (script 058): the reasons follow the code generator's own rules.</summary>
public class CodeExplainerTests
{
    private static CodeMonthDetail Month(int m, string? l14, string? l16, Action<CodeMonthDetail>? set = null)
    {
        var d = new CodeMonthDetail
        {
            MonthNum = m, Employed = true, IsFullTime = true, PlanId = 7, PlanName = "GOLD PPO", MinimumValue = true,
            OfferedSpouse = 1, OfferedDependents = 1, ConditionalSpouse = 0, Premium = 95.50m, Enrolled = false,
            MonthlyFpl = 113.20m, AffordabilityPct = 0.0902m, FullTimeHours = 130, HiredMonths = 12,
            HireDate = new DateTime(2019, 6, 3), Line14 = l14, Line15 = l14 is "1A" or "1H" ? null : 95.50m, Line16 = l16,
            CalculatedAt = new DateTime(2026, 10, 6, 9, 0, 0),
        };
        set?.Invoke(d);
        return d;
    }

    private static CodeExplanationMonth Explain(CodeMonthDetail d)
    {
        var month = new CodeExplanationMonth { Month = d.MonthNum, MonthName = "March" };
        CodeExplainer.Explain(d, month);
        return month;
    }

    [Fact]
    public void AnOfferToTheFamilyThatIsTakenUp()
    {
        var m = Explain(Month(3, "1E", "2C", d => d.Enrolled = true));
        Assert.Contains("plan \"GOLD PPO\" (minimum value) is offered to the employee, spouse and dependents: 1E", m.Why14.Single(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$95.50", m.Why15);
        Assert.StartsWith("2C", m.Why16.Single());
        Assert.False(m.PenaltyRisk);
    }

    [Fact]
    public void TheW2SafeHarborShowsItsSum()
    {
        var m = Explain(Month(3, "1E", "2F", d => d.AnnualW2 = 24000m));
        // 24,000 / 12 x 9.02% = 180.40
        Assert.Equal("2F, W-2 safe harbor: the share $95.50 is within $24,000.00 ÷ 12 months employed × 9.02% = $180.40.", m.Why16.Single());
        Assert.Equal(180.40m, CodeExplainer.W2Limit(m.Detail!));
    }

    [Fact]
    public void TheRateOfPaySafeHarborShowsItsSum()
    {
        var m = Explain(Month(3, "1E", "2H", d => d.HourlyRate = 10m));
        // 10 x 130 x 9.02% = 117.26
        Assert.Contains("$10.00 an hour × 130 hours × 9.02% = $117.26", m.Why16.Single());
    }

    [Fact]
    public void NoOfferToAFullTimeEmployeeIsAPenaltyRisk()
    {
        var m = Explain(Month(3, "1H", null, d => d.PlanId = null));
        Assert.Contains("No coverage offered", m.Why14.Single());
        Assert.Contains("penalty risk (4980H(a))", m.Why16.Single());
        Assert.True(m.PenaltyRisk);
    }

    [Fact]
    public void AnUnaffordableOfferNamesEveryLimit()
    {
        var m = Explain(Month(3, "1E", null, d => { d.Premium = 300m; d.HourlyRate = 10m; d.AnnualW2 = 24000m; }));
        Assert.Contains("the FPL limit $113.20, the rate-of-pay limit $117.26, the W-2 limit $180.40", m.Why16.Single());
        Assert.Contains("4980H(b)", m.Why16.Single());
        Assert.False(m.PenaltyRisk);
    }

    [Fact]
    public void NotEmployedWaitingAndPartTimeMonths()
    {
        Assert.StartsWith("Not employed in March", Explain(Month(3, "1H", "2A", d => d.Employed = false)).Why14.Single());
        Assert.Equal("2A: on COBRA in the month.", Explain(Month(3, "1E", "2A", d => d.CobraMonth = true)).Why16.Single());
        Assert.Contains("eligible from 04/01/2025", Explain(Month(3, "1E", "2D", d => d.EligibleDate = new DateTime(2025, 4, 1))).Why16.Single());
        Assert.StartsWith("2B", Explain(Month(3, "1E", "2B", d => d.IsFullTime = false)).Why16.Single());
        Assert.Contains("does not provide minimum value: 1F", Explain(Month(3, "1F", "2C", d => d.MinimumValue = false)).Why14.Single());
        var qualifying = Explain(Month(3, "1A", null, d => d.Code1A = true));
        Assert.Contains("Qualifying offer", qualifying.Why14.Single());
        Assert.Equal("No Line 16 code is needed with 1A.", qualifying.Why16.Single());
    }

    [Fact]
    public void CodesChangedByHandAreFlagged()
    {
        var header = new Dictionary<string, object?>
        {
            ["EmployeeCodeId"] = 5, ["EmployerId"] = 9, ["FilingYear"] = 2025, ["IsLocked"] = true, ["DisableCoding"] = false,
            ["MAR_COC"] = "1B", ["MAR_LCMP"] = 95.50m, ["MAR_SHC"] = "2C", ["APR_COC"] = "1E", ["APR_LCMP"] = 95.50m, ["APR_SHC"] = "2C",
        };
        var result = CodeExplainer.Build(11, header, new[] { Month(3, "1E", "2C"), Month(4, "1E", "2C") });
        Assert.True(result.HasDetail);
        Assert.True(result.IsLocked);
        Assert.Equal(12, result.Months.Count);
        Assert.True(result.Months[2].ChangedByHand);    // filed 1B, calculated 1E
        Assert.False(result.Months[3].ChangedByHand);
        Assert.Null(result.Months[0].Detail);           // January was not calculated
        Assert.Equal(0.0902m, result.AffordabilityPct);
    }
}
