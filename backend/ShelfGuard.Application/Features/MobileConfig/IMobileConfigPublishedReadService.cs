using ShelfGuard.Application.Features.MobileConfig.Dtos;

namespace ShelfGuard.Application.Features.MobileConfig;

/// <summary>
/// Consumer-facing read of a tenant's currently PUBLISHED mobile app configuration document
/// (TASK-534 — <c>GET /api/v1/mobile/config</c>, mobile's #1 blocking contract per
/// <c>docs/mobile/MOBILE_CURRENT_STATE.md</c>). Draft/archived versions are never reachable
/// through this service under any input — see <see cref="MobileConfigPublishedReadService"/> for
/// the full implementation notes, including the theme-composition sequencing decision.
/// </summary>
public interface IMobileConfigPublishedReadService
{
    /// <summary>
    /// Returns the fully composed document (<c>schemaVersion</c>/<c>configVersion</c>/
    /// <c>tenant</c>/<c>theme</c>/<c>features</c>/<c>navigation</c>/<c>pages</c> — matches
    /// <c>/contracts/mobile-config.schema.json</c>) for the tenant's current
    /// <c>MobileConfigurationVersion</c> with <c>Status == Published</c>, serialized exactly as
    /// returned (the caller should serve <c>DocumentJson</c> verbatim, not re-serialize it), plus
    /// a stable <c>ETag</c> derived from that exact string. Both are <c>null</c> on any error —
    /// see <see cref="MobileConfigReadError.Type"/> to distinguish "tenant not found" from "no
    /// published configuration yet".
    /// </summary>
    Task<(string? DocumentJson, string? ETag, MobileConfigReadError? Error)> GetPublishedConfigAsync(
        Guid tenantId, CancellationToken ct = default);
}
