namespace ShelfGuard.Application.Features.MobileConfig.Dtos;

/// <summary>
/// The tenant's newly published <c>MobileConfigurationVersion</c> (TASK-544).
/// <see cref="ConfigurationJson"/> is the composed document actually stored — it includes the
/// <c>"theme"</c> key snapshotted from <see cref="ShelfGuard.Domain.Entities.MobileTheme"/> at
/// publish time, unlike a draft's <c>ConfigurationJson</c> (see
/// <see cref="MobileConfigDraftDto"/>'s remarks), which never carries one.
/// </summary>
public sealed record MobileConfigPublishedDto(
    Guid MobileConfigurationId,
    Guid VersionId,
    int Version,
    int SchemaVersion,
    string ConfigurationJson,
    Guid? CreatedBy,
    DateTime CreatedAt,
    DateTime PublishedAt);

/// <summary>Why <see cref="IMobileConfigPublishService.PublishAsync"/> did not publish.</summary>
public enum MobileConfigPublishErrorType
{
    /// <summary>The tenant has no draft version at all — nothing to publish.</summary>
    NoDraftToPublish,

    /// <summary>The draft's <c>ConfigurationJson</c> failed <see cref="IMobileConfigValidator"/> — see <see cref="MobileConfigPublishError.ValidationErrors"/>.</summary>
    ValidationFailed,

    /// <summary>
    /// Another publish for this tenant committed first (xmin conflict) — safe to retry;
    /// nothing was corrupted or partially applied.
    /// </summary>
    ConcurrentPublish,

    /// <summary>
    /// TASK-545 rollback: the requested version id does not exist for this tenant (wrong id,
    /// belongs to a different tenant, or the tenant has no <c>MobileConfiguration</c> row at all).
    /// </summary>
    VersionNotFound,

    /// <summary>
    /// TASK-545 rollback: the requested version is already the tenant's current published or draft
    /// version — nothing to roll back to.
    /// </summary>
    CannotRollbackToCurrentVersion,
}

/// <summary>See <see cref="MobileConfigPublishErrorType"/>. <see cref="ValidationErrors"/> is only non-empty for <see cref="MobileConfigPublishErrorType.ValidationFailed"/>.</summary>
public sealed record MobileConfigPublishError(
    MobileConfigPublishErrorType Type,
    string Message,
    IReadOnlyList<MobileConfigValidationError> ValidationErrors);

/// <summary><see cref="MobileConfigPublishController"/>'s POST response shape.</summary>
public sealed record MobileConfigPublishedResponse(
    Guid MobileConfigurationId,
    Guid VersionId,
    int Version,
    int SchemaVersion,
    string ConfigurationJson,
    Guid? CreatedBy,
    DateTime CreatedAt,
    DateTime PublishedAt)
{
    public static MobileConfigPublishedResponse From(MobileConfigPublishedDto dto) => new(
        dto.MobileConfigurationId,
        dto.VersionId,
        dto.Version,
        dto.SchemaVersion,
        dto.ConfigurationJson,
        dto.CreatedBy,
        dto.CreatedAt,
        dto.PublishedAt);
}
