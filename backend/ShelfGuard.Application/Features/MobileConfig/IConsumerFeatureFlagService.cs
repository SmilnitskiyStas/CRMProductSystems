namespace ShelfGuard.Application.Features.MobileConfig;

/// <summary>
/// Resolves consumer-app availability. Loyalty is derived from the licensed module and
/// LoyaltyProgramSettings.IsEnabled (Bonus Program), immediately and independently of any
/// draft/published features.loyalty value. Other flags retain their published-document,
/// explicit-false-only disable semantics; missing configuration defaults them to enabled.
/// </summary>
public interface IConsumerFeatureFlagService
{
    /// <summary>Throws ArgumentException for an unknown feature key.</summary>
    Task<bool> IsEnabledAsync(Guid tenantId, string flagKey, CancellationToken ct = default);
}
