using ShelfGuard.Application.Features.MobileConfig.Dtos;

namespace ShelfGuard.Application.Features.MobileConfig;

/// <summary>
/// Application-layer CRUD for a tenant's draft <c>MobileConfigurationVersion</c> (TASK-532). No
/// publish or consumer-facing read here — those are TASK-534 (published read) and TASK-544
/// (generalized publish flow). Callers must resolve <c>tenantId</c> from
/// <c>ITenantContext</c> — never trust a tenant id from a request body.
/// </summary>
public interface IMobileConfigDraftService
{
    /// <summary>
    /// Validates <paramref name="configurationJson"/> and persists it as the tenant's draft. On
    /// first call for a tenant, lazily creates the <c>MobileConfiguration</c> root row and a new
    /// draft version. On later calls, mutates the existing draft version's JSON in place — a
    /// draft is mutable scratch space until published, so saving it again never creates a new
    /// version row (see <see cref="MobileConfigDraftService"/> remarks). On validation failure,
    /// nothing is persisted and the error list is non-empty.
    /// </summary>
    Task<(MobileConfigDraftDto? Draft, IReadOnlyList<MobileConfigValidationError> Errors)> SaveDraftAsync(
        Guid tenantId, Guid? actingUserId, string configurationJson, CancellationToken ct = default);

    /// <summary>Returns the tenant's current draft version, or null if none exists yet.</summary>
    Task<MobileConfigDraftDto?> GetDraftAsync(Guid tenantId, CancellationToken ct = default);
}
