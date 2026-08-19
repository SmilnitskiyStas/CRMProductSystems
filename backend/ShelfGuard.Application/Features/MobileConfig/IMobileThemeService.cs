using ShelfGuard.Application.Features.MobileConfig.Dtos;
using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Application.Features.MobileConfig;

/// <summary>
/// Tenant-admin read/write of a tenant's <c>MobileTheme</c> row (TASK-536). Callers must resolve
/// <c>tenantId</c> from <c>ITenantContext</c> — this is a staff/admin operation, never a consumer
/// one (contrast with <c>IMobileConfigPublishedReadService</c>, which resolves tenant from an
/// explicit anonymous-caller-supplied id via <c>ITenantSessionOverride</c>).
///
/// LIVE-EFFECT CAVEAT (load-bearing, see this interface's implementation remarks for the full
/// writeup): <see cref="MobileTheme"/> is one row per tenant, not versioned/duplicated between
/// draft and published states the way <c>MobileConfigurationVersion</c> is (TASK-531). Because
/// <c>GET /api/v1/mobile/config</c> (TASK-534) reads theme LIVE off this same row on every request
/// rather than from a published snapshot, <b>every successful <see cref="UpdateThemeAsync"/> call
/// takes effect in production immediately</b> — there is no draft/publish protection for theme
/// today. This is a known, explicitly accepted gap pending TASK-544's generalized Draft→Preview→
/// Publish reconciliation, not an oversight.
/// </summary>
public interface IMobileThemeService
{
    /// <summary>
    /// Returns the tenant's current theme, or <c>MobileTheme.CreateDefault</c>'s built-in defaults
    /// (with <c>UpdatedAt = null</c>) when the tenant has never saved one yet.
    /// </summary>
    Task<MobileThemeDto> GetThemeAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Validates <paramref name="request"/> against <see cref="MobileThemeWhitelists"/> and, on
    /// success, persists it as the tenant's theme — creating the <c>MobileConfiguration</c> root
    /// and/or <c>MobileTheme</c> row on first write for a tenant that has never touched either.
    /// On validation failure, nothing is persisted and the error list is non-empty.
    /// <paramref name="actingUserId"/> is recorded on the resulting <c>ActivityLog</c> entry
    /// (TASK-550) — same "config changed" audit family as
    /// <see cref="IMobileConfigDraftService.SaveDraftAsync"/>/<see cref="IMobileConfigPublishService.PublishAsync"/>,
    /// since a theme edit is conceptually part of "mobile config changed" even though it lives on a
    /// separate entity/service (see this interface's LIVE-EFFECT CAVEAT above).
    /// </summary>
    Task<(MobileThemeDto? Theme, IReadOnlyList<MobileConfigValidationError> Errors)> UpdateThemeAsync(
        Guid tenantId, Guid? actingUserId, UpdateMobileThemeRequest request, CancellationToken ct = default);
}
