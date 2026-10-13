using ACA360.BusinessLogic.Validators;
using ACA360.Core.Models;
using ACA360.Web.Helpers;

namespace ACA360.Tests.Unit.Helpers;

public class DataMaskerTests
{
    [Theory]
    [InlineData("1234567890", 4, false, "******7890")]
    [InlineData("1234567890", 4, true, "1234******")]
    [InlineData("abc", 4, false, "***")]
    [InlineData("abcd", 4, false, "****")]
    public void MaskGenericKeepsTheChosenEnd(string input, int visible, bool fromStart, string expected) =>
        Assert.Equal(expected, DataMasker.MaskGeneric(input, visible, '*', fromStart));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MaskGenericLeavesEmptyValuesAlone(string? input) => Assert.Equal(input, DataMasker.MaskGeneric(input!));

    [Theory]
    [InlineData("123-45-6789", "***-**-6789")]
    [InlineData("123456789", "***-**-6789")]
    [InlineData(" 123 45 6789 ", "***-**-6789")]
    public void MaskSsnShowsOnlyTheLastFour(string ssn, string expected) => Assert.Equal(expected, DataMasker.MaskSSN(ssn));

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567890")]
    public void MaskSsnNeverEchoesAnInvalidNumber(string ssn)
    {
        var masked = DataMasker.MaskSSN(ssn);
        Assert.DoesNotContain(ssn, masked);
        Assert.Equal("Invalid SSN", masked);
    }

    [Fact]
    public void MaskSsnOfNothingIsEmpty() => Assert.Equal("", DataMasker.MaskSSN(null!));

    [Theory]
    [InlineData("12-3456789", "**-***6789")]
    [InlineData("123456789", "**-***6789")]
    public void MaskEinShowsOnlyTheLastFour(string ein, string expected) => Assert.Equal(expected, DataMasker.MaskEIN(ein));

    [Fact]
    public void MaskEinFallsBackToGenericMaskingForOddLengths() => Assert.Equal("****5678", DataMasker.MaskEIN("12345678"));

    [Theory]
    [InlineData("***-**-6789", true)]
    [InlineData("**-***6789", true)]
    [InlineData("123-45-6789", false)]
    [InlineData("****6789", false)]
    [InlineData("", false)]
    public void IsMaskedPlaceholderRecognisesOnlyItsOwnFormats(string value, bool expected) =>
        Assert.Equal(expected, DataMasker.IsMaskedPlaceholder(value));

    [Fact]
    public void AMaskedValueIsRecognisedAsMasked()
    {
        Assert.True(DataMasker.IsMaskedPlaceholder(DataMasker.MaskSSN("123-45-6789")));
        Assert.True(DataMasker.IsMaskedPlaceholder(DataMasker.MaskEIN("12-3456789")));
    }
}

public class UserAgentHelperTests
{
    [Theory]
    [InlineData(null, "Unknown")]
    [InlineData("  ", "Unknown")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36",
        "Chrome 125.0.0.0 / Windows 10/11")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36 Edg/125.0.2535.67",
        "Edge 125.0.2535.67 / Windows 10/11")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 OPR/106.0.0.0",
        "Opera 106.0.0.0 / Windows 10/11")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64; rv:126.0) Gecko/20100101 Firefox/126.0", "Firefox 126.0 / Linux")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_4 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Mobile/15E148 Safari/604.1",
        "Safari 17.4 / iOS 17.4")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Safari/605.1.15",
        "Safari 17.4 / macOS 10.15.7")]
    [InlineData("Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Mobile Safari/537.36",
        "Chrome 125.0.0.0 / Android 14")]
    [InlineData("curl/8.4.0", "Unknown browser / Unknown OS")]
    public void FriendlyNamesTheBrowserAndSystem(string? userAgent, string expected) =>
        Assert.Equal(expected, UserAgentHelper.Friendly(userAgent));
}

public class PasswordPolicyValidatorTests
{
    private static readonly SecuritySettingsDto Policy = new() { MinPasswordLength = 10, RequireUppercase = true, RequireDigits = true };

    [Fact]
    public void AStrongPasswordPasses() => Assert.Empty(PasswordPolicyValidator.Validate("Abcdefgh12", Policy));

    [Fact]
    public void ATooShortPasswordIsRefused() =>
        Assert.Contains(PasswordPolicyValidator.Validate("Abc12", Policy), e => e.Contains("at least 10"));

    [Fact]
    public void AMissingUppercaseLetterIsRefused() =>
        Assert.Contains(PasswordPolicyValidator.Validate("abcdefgh12", Policy), e => e.Contains("uppercase"));

    [Fact]
    public void AMissingDigitIsRefused() =>
        Assert.Contains(PasswordPolicyValidator.Validate("Abcdefghij", Policy), e => e.Contains("number"));

    [Fact]
    public void EveryProblemIsReported() => Assert.Equal(3, PasswordPolicyValidator.Validate("abc", Policy).Count);

    [Fact]
    public void NoPasswordIsTreatedAsEmpty() => Assert.NotEmpty(PasswordPolicyValidator.Validate(null, Policy));

    [Fact]
    public void RulesThatAreSwitchedOffAreNotApplied() =>
        Assert.Empty(PasswordPolicyValidator.Validate("abcd", new SecuritySettingsDto { MinPasswordLength = 4, RequireUppercase = false, RequireDigits = false }));
}
