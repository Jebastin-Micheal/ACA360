using System.Security.Cryptography;
using ACA360.Security.Encryption;

namespace ACA360.Tests.Unit.Security;

public class AesEncryptionServiceTests
{
    private readonly AesEncryptionService _service = new("unit-test-key");

    [Theory]
    [InlineData("123-45-6789")]
    [InlineData("")]
    [InlineData("Ünïcödé · 名前 · emoji 🙂")]
    public void DecryptReturnsWhatWasEncrypted(string plain) =>
        Assert.Equal(plain, _service.Decrypt(_service.Encrypt(plain)));

    [Fact]
    public void LongTextRoundTrips()
    {
        var plain = new string('x', 100_000);
        Assert.Equal(plain, _service.Decrypt(_service.Encrypt(plain)));
    }

    [Fact]
    public void EachEncryptionUsesAFreshIv()
    {
        var a = _service.Encrypt("same text");
        var b = _service.Encrypt("same text");
        Assert.NotEqual(a, b);
        Assert.Equal(_service.Decrypt(a), _service.Decrypt(b));
    }

    [Fact]
    public void TheCipherTextDoesNotContainThePlainText() =>
        Assert.DoesNotContain("123456789", _service.Encrypt("123456789"));

    [Fact]
    public void AnotherKeyCannotReadIt()
    {
        var cipher = _service.Encrypt("123-45-6789");
        var other = new AesEncryptionService("a different key");
        string? read = null;
        try { read = other.Decrypt(cipher); } catch (CryptographicException) { }
        Assert.NotEqual("123-45-6789", read);
    }

    [Fact]
    public void TheSameKeyStringAlwaysGivesTheSameKey() =>
        Assert.Equal("abc", new AesEncryptionService("unit-test-key").Decrypt(_service.Encrypt("abc")));
}
