namespace ShelfGuard.Application.Features.MobileConfig.Dtos;

/// <summary>
/// Why <see cref="IMobileConfigPublishedReadService.GetPublishedConfigAsync"/> couldn't produce a
/// document (TASK-534). Kept as two distinct values (not one generic failure) because
/// <c>ConsumerContentController</c> — the precedent this endpoint follows — already discloses this
/// same distinction ("Tenant not found." vs a banner/promotion/catalog-specific not-found) for the
/// same public tenant space, so matching that existing disclosure level is not a new leak.
/// </summary>
public enum MobileConfigReadErrorType
{
    /// <summary>tenantId does not match any <c>Tenant</c> row.</summary>
    TenantNotFound,

    /// <summary>
    /// The tenant exists but has no currently published <c>MobileConfigurationVersion</c> (no
    /// <c>MobileConfiguration</c> row yet, no <c>PublishedVersionId</c> set yet, or — defensively
    /// — a published pointer whose target isn't actually <c>Published</c>/is unparseable).
    /// </summary>
    NoPublishedConfiguration,
}

/// <summary>See <see cref="MobileConfigReadErrorType"/>.</summary>
public sealed record MobileConfigReadError(MobileConfigReadErrorType Type, string Message);
