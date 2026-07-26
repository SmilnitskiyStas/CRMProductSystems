using ShelfGuard.Application.Common;
using Xunit;

namespace ShelfGuard.Tests.Auth;

public sealed class PhoneNormalizerTests
{
    [Theory]
    [InlineData("+380501234567", "+380501234567")]
    [InlineData("380501234567", "+380501234567")]
    [InlineData("0501234567", "+380501234567")]
    [InlineData("501234567", "+380501234567")]
    [InlineData("+38 (050) 123-45-67", "+380501234567")]
    [InlineData(" 050 123 45 67 ", "+380501234567")]
    public void Normalize_AcceptedShapes_ReturnsCanonicalForm(string raw, string expected)
    {
        Assert.Equal(expected, PhoneNormalizer.Normalize(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]              // too short
    [InlineData("+1234567890123")]      // wrong country code, 13 digits
    [InlineData("+380501234567890")]    // way too long
    [InlineData("abc")]
    public void Normalize_RejectedShapes_ReturnsNull(string? raw)
    {
        Assert.Null(PhoneNormalizer.Normalize(raw));
    }
}
