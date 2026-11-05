namespace ShelfGuard.Application.Features.MobileConfig;

/// <summary>
/// Resolves consumer-app availability. Loyalty is derived from the licensed module and
/// LoyaltyProgramSettings.IsEnabled (Bonus Program), immediately and independently of any
/// draft/published features.loyalty value. An explicitly published navigation entry enables
/// its marketing section, matching mobile routes. Other flags retain explicit-false-only
/// disable semantics; missing configuration defaults them to enabled.
/// </summary>
public interface IConsumerFeatureFlagService
{
    /// <summary>Throws ArgumentException for an unknown feature key.</summary>
    Task<bool> IsEnabledAsync(Guid tenantId, string flagKey, CancellationToken ct = default);
}
