using NSubstitute;
using ShelfGuard.Application.Features.MobileConfig;
using ShelfGuard.Application.Features.MobileConfig.Dtos;
using Xunit;

namespace ShelfGuard.Tests.MobileConfig;

/// <summary>
/// TASK-543 — ConsumerFeatureFlagService: the production-safety default-enabled behavior for a
/// tenant with no published MobileConfigurationVersion, explicit-false-only disable semantics,
/// opt-out-not-opt-in for an unmentioned flag key, and all 8 spec flags resolvable. Mocks
/// <see cref="IMobileConfigPublishedReadService"/> — its own
/// tenant-lookup/RLS/draft-never-leaks behavior is already covered by
/// <c>MobileConfigPublishedReadServiceTests</c> and its RLS integration sibling, so this suite
/// does not re-test that.
/// </summary>
public sealed class ConsumerFeatureFlagServiceTests
{
    private readonly IMobileConfigPublishedReadService _publishedRead = Substitute.For<IMobileConfigPublishedReadService>();
    private readonly ConsumerFeatureFlagService _sut;

    private static readonly Guid TenantId = Guid.NewGuid();

    public ConsumerFeatureFlagServiceTests()
    {
        _sut = new ConsumerFeatureFlagService(_publishedRead);
    }

    /// <summary>
    /// THE production-safety proof required by TASK-543: a tenant with zero MobileConfiguration
    /// activity — i.e. every already-onboarded production tenant today, since the Publish flow
    /// (TASK-544) does not exist yet — must still pass every flag check. If this regressed to
    /// "no published config = disabled", wiring this service onto the already-live
    /// ConsumerContentController/ConsumerLoyaltyController would 403 every real consumer request
    /// in production.
    /// </summary>
    [Theory]
    [InlineData("loyalty")]
    [InlineData("promotions")]
    [InlineData("catalog")]
    [InlineData("coupons")]
    [InlineData("news")]
    [InlineData("receipts")]
    [InlineData("delivery")]
    [InlineData("personalOffers")]
    public async Task PRODUCTION_SAFETY_unconfigured_tenant_defaults_every_flag_to_enabled(string flagKey)
    {
        _publishedRead.GetPublishedConfigAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns((null, (string?)null, new MobileConfigReadError(MobileConfigReadErrorType.NoPublishedConfiguration, "none")));

        var enabled = await _sut.IsEnabledAsync(TenantId, flagKey);

        Assert.True(enabled);
    }

    [Fact]
    public async Task PRODUCTION_SAFETY_unknown_tenant_also_defaults_to_enabled()
    {
        _publishedRead.GetPublishedConfigAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns((null, (string?)null, new MobileConfigReadError(MobileConfigReadErrorType.TenantNotFound, "none")));

        var enabled = await _sut.IsEnabledAsync(TenantId, "loyalty");

        Assert.True(enabled);
    }

    [Fact]
    public async Task Explicit_false_in_published_features_disables_the_flag()
    {
        const string documentJson = """{"features":{"loyalty":false,"news":true}}""";
        _publishedRead.GetPublishedConfigAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns((documentJson, "\"etag\"", (MobileConfigReadError?)null));

        Assert.False(await _sut.IsEnabledAsync(TenantId, "loyalty"));
        Assert.True(await _sut.IsEnabledAsync(TenantId, "news"));
    }

    [Fact]
    public async Task Flag_absent_from_a_published_features_object_still_defaults_to_enabled()
    {
        // Published, but this tenant's document never mentions "delivery" at all — opt-out
        // semantics, not opt-in: only an explicit false turns a flag off.
        const string documentJson = """{"features":{"loyalty":true}}""";
        _publishedRead.GetPublishedConfigAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns((documentJson, "\"etag\"", (MobileConfigReadError?)null));

        Assert.True(await _sut.IsEnabledAsync(TenantId, "delivery"));
    }

    [Fact]
    public async Task Missing_features_object_entirely_defaults_to_enabled()
    {
        const string documentJson = """{"schemaVersion":1}""";
        _publishedRead.GetPublishedConfigAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns((documentJson, "\"etag\"", (MobileConfigReadError?)null));

        Assert.True(await _sut.IsEnabledAsync(TenantId, "catalog"));
    }

    [Fact]
    public async Task Unknown_flag_key_throws_ArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.IsEnabledAsync(TenantId, "not_a_real_flag"));
    }
}
