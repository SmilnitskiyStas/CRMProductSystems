namespace ShelfGuard.Application.Features.MobileConfig;

/// <summary>
/// TASK-543 placeholder for ЕТАП 18 (subscription-plan-gated consumer features).
/// <c>TARGET_ARCHITECTURE.md</c> describes a future stage where a tenant's billing plan
/// (<c>Tenant.Plan</c> — basic/standard/enterprise/trial) constrains which of
/// <see cref="MobileConfigWhitelists.FeatureKeys"/> it may enable, via a plan→features mapping
/// table that does not exist yet.
///
/// THIS TYPE ENFORCES NOTHING. It only reads and returns <c>Tenant.Plan</c>.
/// <see cref="IConsumerFeatureFlagService"/> does not call it, and no endpoint is denied based
/// on its result — it exists purely as a documented, already-wired seam so a future ЕТАП 18
/// implementer has somewhere to add real plan-gating logic instead of inventing plan lookup
/// from scratch. Until that stage is scheduled, every flag resolves solely from
/// <see cref="MobileConfigWhitelists.FeatureKeys"/> per <see cref="IConsumerFeatureFlagService"/>.
/// </summary>
public interface ISubscriptionPlanFeatureGate
{
    /// <summary>The tenant's current billing plan, or null if the tenant does not exist. No enforcement is applied.</summary>
    Task<string?> GetTenantPlanAsync(Guid tenantId, CancellationToken ct = default);
}
