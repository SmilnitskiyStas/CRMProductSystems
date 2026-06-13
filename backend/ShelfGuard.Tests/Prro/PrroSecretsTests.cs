using ShelfGuard.Application.Features.Integrations;
using Xunit;

namespace ShelfGuard.Tests.Prro;

/// <summary>Unit tests for write-only secret masking (ADR-013 p.4).</summary>
public sealed class PrroSecretsTests
{
    // ── Mask ───────────────────────────────────────────────────────────────

    [Fact]
    public void Mask_null_returns_null() =>
        Assert.Null(PrroSecrets.Mask(null));

    [Fact]
    public void Mask_empty_returns_null() =>
        Assert.Null(PrroSecrets.Mask(string.Empty));

    [Fact]
    public void Mask_without_suffix_returns_token() =>
        Assert.Equal(PrroSecrets.MaskToken, PrroSecrets.Mask("secret123"));

    [Fact]
    public void Mask_with_suffix_4_appends_last_4_chars()
    {
        var result = PrroSecrets.Mask("ABCDEF1234", visibleSuffix: 4);
        Assert.Equal(PrroSecrets.MaskToken + "1234", result);
    }

    [Fact]
    public void Mask_short_value_returns_token_only()
    {
        // Value shorter than suffix — just return the mask token
        var result = PrroSecrets.Mask("AB", visibleSuffix: 4);
        Assert.Equal(PrroSecrets.MaskToken, result);
    }

    // ── IsMasked ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("••••")]
    [InlineData("••••1234")]
    public void IsMasked_returns_true_for_masked_values(string value) =>
        Assert.True(PrroSecrets.IsMasked(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("plaintext")]
    [InlineData("ABCD1234")]
    public void IsMasked_returns_false_for_non_masked(string? value) =>
        Assert.False(PrroSecrets.IsMasked(value));

    // ── Merge (PUT secret field semantics) ─────────────────────────────────

    [Fact]
    public void Merge_null_keeps_stored() =>
        Assert.Equal("stored-value", PrroSecrets.Merge(null, "stored-value"));

    [Fact]
    public void Merge_masked_keeps_stored() =>
        Assert.Equal("stored-secret", PrroSecrets.Merge(PrroSecrets.MaskToken, "stored-secret"));

    [Fact]
    public void Merge_masked_with_suffix_keeps_stored() =>
        Assert.Equal("real-license-key", PrroSecrets.Merge(PrroSecrets.MaskToken + "y", "real-license-key"));

    [Fact]
    public void Merge_empty_string_clears_secret() =>
        Assert.Null(PrroSecrets.Merge(string.Empty, "stored-value"));

    [Fact]
    public void Merge_new_value_replaces_stored() =>
        Assert.Equal("new-secret", PrroSecrets.Merge("new-secret", "old-secret"));

    [Fact]
    public void Merge_null_with_no_stored_returns_null() =>
        Assert.Null(PrroSecrets.Merge(null, null));
}
