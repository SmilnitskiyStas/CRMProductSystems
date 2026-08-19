using ShelfGuard.Application.Features.MobileConfig;
using ShelfGuard.Application.Features.MobileConfig.Dtos;
using Xunit;

namespace ShelfGuard.Tests.MobileConfig;

/// <summary>
/// TASK-536 — whitelist-based validation of <see cref="UpdateMobileThemeRequest"/> against
/// <see cref="MobileThemeWhitelists"/>. Every rejection case asserts both that validation fails AND
/// that the specific offending field is reported, matching <c>MobileConfigValidatorTests</c>'s
/// "which field, what was wrong" convention (TASK-532).
/// </summary>
public sealed class MobileThemeValidatorTests
{
    private readonly MobileThemeValidator _sut = new();

    private static UpdateMobileThemeRequest ValidRequest(
        string? logoUrl = "https://cdn.example.com/logo.png",
        string primaryColor = "#1E40AF",
        string secondaryColor = "#222222",
        string backgroundColor = "#FFFFFF",
        string surfaceColor = "#F7F7F7",
        string textPrimaryColor = "#111111",
        string textSecondaryColor = "#777777",
        int buttonRadius = 14,
        int cardRadius = 18,
        string spacingPreset = "comfortable") => new(
        logoUrl, primaryColor, secondaryColor, backgroundColor, surfaceColor,
        textPrimaryColor, textSecondaryColor, buttonRadius, cardRadius, spacingPreset);

    // ── Happy paths ──────────────────────────────────────────────────────────

    [Fact]
    public void Validate_accepts_a_well_formed_request()
    {
        var result = _sut.Validate(ValidRequest());

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_accepts_null_logoUrl()
    {
        var result = _sut.Validate(ValidRequest(logoUrl: null));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("compact")]
    [InlineData("comfortable")]
    public void Validate_accepts_every_whitelisted_spacing_preset(string preset)
    {
        var result = _sut.Validate(ValidRequest(spacingPreset: preset));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    public void Validate_accepts_buttonRadius_at_its_bounds(int radius)
    {
        var result = _sut.Validate(ValidRequest(buttonRadius: radius));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(40)]
    public void Validate_accepts_cardRadius_at_its_bounds(int radius)
    {
        var result = _sut.Validate(ValidRequest(cardRadius: radius));

        Assert.True(result.IsValid);
    }

    // ── Hex color rejections ────────────────────────────────────────────────

    [Theory]
    [InlineData("primaryColor")]
    [InlineData("secondaryColor")]
    [InlineData("backgroundColor")]
    [InlineData("surfaceColor")]
    [InlineData("textPrimaryColor")]
    [InlineData("textSecondaryColor")]
    public void Validate_rejects_a_malformed_hex_color_on_every_color_field(string field)
    {
        var request = field switch
        {
            "primaryColor" => ValidRequest(primaryColor: "blue"),
            "secondaryColor" => ValidRequest(secondaryColor: "blue"),
            "backgroundColor" => ValidRequest(backgroundColor: "blue"),
            "surfaceColor" => ValidRequest(surfaceColor: "blue"),
            "textPrimaryColor" => ValidRequest(textPrimaryColor: "blue"),
            "textSecondaryColor" => ValidRequest(textSecondaryColor: "blue"),
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

        var result = _sut.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == field);
    }

    [Fact]
    public void Validate_rejects_a_three_digit_hex_shorthand()
    {
        var result = _sut.Validate(ValidRequest(primaryColor: "#FFF"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "primaryColor");
    }

    [Fact]
    public void Validate_rejects_a_hex_color_missing_the_hash()
    {
        var result = _sut.Validate(ValidRequest(primaryColor: "1E40AF"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "primaryColor");
    }

    [Fact]
    public void Validate_rejects_empty_color_string()
    {
        var result = _sut.Validate(ValidRequest(primaryColor: ""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "primaryColor");
    }

    // ── Radius bound rejections ─────────────────────────────────────────────

    [Theory]
    [InlineData(-1)]
    [InlineData(33)]
    public void Validate_rejects_buttonRadius_outside_bounds(int radius)
    {
        var result = _sut.Validate(ValidRequest(buttonRadius: radius));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "buttonRadius");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(41)]
    public void Validate_rejects_cardRadius_outside_bounds(int radius)
    {
        var result = _sut.Validate(ValidRequest(cardRadius: radius));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "cardRadius");
    }

    // ── Spacing preset rejections ───────────────────────────────────────────

    [Theory]
    [InlineData("spacious")]
    [InlineData("cozy")]
    [InlineData("")]
    public void Validate_rejects_an_unknown_spacing_preset(string preset)
    {
        var result = _sut.Validate(ValidRequest(spacingPreset: preset));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "spacingPreset");
    }

    // ── logoUrl rejections ───────────────────────────────────────────────────

    [Fact]
    public void Validate_rejects_empty_logoUrl()
    {
        var result = _sut.Validate(ValidRequest(logoUrl: ""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "logoUrl");
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com/logo.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/relative/path.png")]
    public void Validate_rejects_a_non_http_or_malformed_logoUrl(string logoUrl)
    {
        var result = _sut.Validate(ValidRequest(logoUrl: logoUrl));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "logoUrl");
    }

    [Fact]
    public void Validate_rejects_a_logoUrl_exceeding_the_max_length()
    {
        var longUrl = "https://cdn.example.com/" + new string('a', MobileThemeWhitelists.MaxLogoUrlLength);

        var result = _sut.Validate(ValidRequest(logoUrl: longUrl));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "logoUrl");
    }

    // ── Multiple simultaneous rejections ────────────────────────────────────

    [Fact]
    public void Validate_reports_every_invalid_field_at_once()
    {
        var request = ValidRequest(
            primaryColor: "blue",
            buttonRadius: 999,
            cardRadius: -5,
            spacingPreset: "huge");

        var result = _sut.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == "primaryColor");
        Assert.Contains(result.Errors, e => e.Field == "buttonRadius");
        Assert.Contains(result.Errors, e => e.Field == "cardRadius");
        Assert.Contains(result.Errors, e => e.Field == "spacingPreset");
    }
}
