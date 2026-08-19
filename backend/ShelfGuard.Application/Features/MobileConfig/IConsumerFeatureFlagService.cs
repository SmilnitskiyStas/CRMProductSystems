namespace ShelfGuard.Application.Features.MobileConfig;

/// <summary>
/// Resolves the 8 consumer-app feature flags (<see cref="MobileConfigWhitelists.FeatureKeys"/>:
/// loyalty, promotions, catalog, coupons, news, receipts, delivery, personalOffers) for a
/// consumer session scoped to a specific tenant (TASK-543, Stage D ЕТАП 10).
///
/// This is a deliberately SEPARATE mechanism from the staff-side
/// <c>ShelfGuard.Infrastructure.Authorization.RequireModuleAttribute</c> /
/// <c>Tenant.Modules</c>/<c>Tenant.HasModule</c> gate — that gate reads a <c>tenant_id</c> JWT
/// claim a consumer/anonymous session never carries, and it is a different concept anyway: B2B
/// module licensing (a whole program — "inventory", "pos", "loyalty" — being active for
/// staff/POS use). These 8 flags are an independent layer describing whether the tenant's
/// consumer *app* currently shows a given section, sourced from the already-built
/// <c>MobileConfigurationVersion.ConfigurationJson.features</c> document (TASK-532). Nothing in
/// the spec ties one to the other; do not derive one from the other.
///
/// PRODUCTION-SAFETY DEFAULT (read before changing this contract): every flag resolves as
/// ENABLED unless the tenant has actually published a <c>MobileConfigurationVersion</c> whose
/// <c>features</c> object explicitly sets that key to <c>false</c>. <c>ConsumerContentController</c>
/// and <c>ConsumerLoyaltyController</c> are already live in production today, entirely
/// independent of this new domain, and — as of this task — no tenant has ever published a
/// <c>MobileConfigurationVersion</c> (the Publish flow, TASK-544, does not exist yet, so
/// <c>MobileConfiguration.PublishedVersionId</c> is <c>null</c> for every tenant in existence).
/// Defaulting to "disabled" here would 403 every real consumer request in production the
/// instant this service is wired onto those live endpoints — see
/// <c>ConsumerFeatureFlagServiceTests</c>'s <c>PRODUCTION_SAFETY_*</c> tests for the specific
/// proof this default holds.
///
/// SCOPE NOTE: this task builds and unit-tests this service and its companion
/// <c>RequireConsumerFeatureAttribute</c> in isolation. Neither is wired onto
/// <c>ConsumerContentController</c>/<c>ConsumerLoyaltyController</c> yet — that is deliberately
/// deferred follow-up work for whichever task first makes Publish (TASK-544/546) real, not an
/// oversight of this task.
/// </summary>
public interface IConsumerFeatureFlagService
{
    /// <summary>
    /// True unless the tenant has a published <c>MobileConfigurationVersion</c> whose
    /// <c>features[flagKey]</c> is explicitly <c>false</c>. A published document that simply
    /// doesn't mention <paramref name="flagKey"/> also resolves to enabled (opt-out semantics,
    /// not opt-in). Throws <see cref="ArgumentException"/> when <paramref name="flagKey"/> is
    /// not one of <see cref="MobileConfigWhitelists.FeatureKeys"/> — that indicates a
    /// programming error (callers pass a compile-time-known constant), not user input.
    /// </summary>
    Task<bool> IsEnabledAsync(Guid tenantId, string flagKey, CancellationToken ct = default);
}
