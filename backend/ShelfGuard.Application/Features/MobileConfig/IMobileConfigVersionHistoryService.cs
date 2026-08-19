using ShelfGuard.Application.Features.MobileConfig.Dtos;

namespace ShelfGuard.Application.Features.MobileConfig;

/// <summary>
/// Read-only query side of TASK-545 (Version History + Rollback) — no mutation lives here, that is
/// <see cref="IMobileConfigPublishService.RollbackAsync"/>'s job, same split
/// <see cref="IMobileConfigDraftService"/>/<see cref="IMobileConfigPublishService"/> already
/// established for draft-CRUD vs. publish.
/// </summary>
public interface IMobileConfigVersionHistoryService
{
    /// <summary>
    /// Every version row for the tenant — draft, published, and archived, none ever deleted —
    /// ordered newest-first by <c>Version</c> descending.
    /// </summary>
    Task<IReadOnlyList<MobileConfigVersionSummaryDto>> GetHistoryAsync(Guid tenantId, CancellationToken ct = default);
}
