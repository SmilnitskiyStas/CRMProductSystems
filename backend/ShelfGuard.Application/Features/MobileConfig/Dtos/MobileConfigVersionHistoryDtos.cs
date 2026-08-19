namespace ShelfGuard.Application.Features.MobileConfig.Dtos;

/// <summary>
/// One row in a tenant's mobile-config version history (TASK-545, <c>GET /api/v1/mobile/config/versions</c>).
/// Deliberately excludes <c>ConfigurationJson</c> — the history list is a picker for choosing a
/// rollback target, not a document viewer; a version's full content is only fetched on demand
/// (e.g. by the rollback call itself, or a future "view this version" detail endpoint).
/// </summary>
public sealed record MobileConfigVersionSummaryDto(
    Guid Id,
    int Version,
    string Status,
    DateTime CreatedAt,
    DateTime? PublishedAt,
    Guid? CreatedBy);

/// <summary><see cref="MobileConfigVersionSummaryDto"/>'s wire shape for <c>GET /api/v1/mobile/config/versions</c>.</summary>
public sealed record MobileConfigVersionSummaryResponse(
    Guid Id,
    int Version,
    string Status,
    DateTime CreatedAt,
    DateTime? PublishedAt,
    Guid? CreatedBy)
{
    public static MobileConfigVersionSummaryResponse From(MobileConfigVersionSummaryDto dto) => new(
        dto.Id, dto.Version, dto.Status, dto.CreatedAt, dto.PublishedAt, dto.CreatedBy);
}
