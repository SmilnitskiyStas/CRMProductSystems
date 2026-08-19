using ShelfGuard.Application.Features.MobileConfig.Dtos;

namespace ShelfGuard.Application.Features.MobileConfig;

/// <summary>
/// Generalizes Draft→Validate→Publish from single-entity scale (<c>Banner.PublishedAt</c>/
/// <c>Banner.Publish()</c>) to a whole <c>MobileConfigurationVersion</c> document (TASK-544,
/// Stage D's central task). See <see cref="MobileConfigPublishService"/> for the full reconciliation
/// notes this closes out from TASK-532/534/536/543.
/// </summary>
public interface IMobileConfigPublishService
{
    /// <summary>
    /// Publishes the tenant's current draft:
    /// <list type="number">
    /// <item>Loads the draft. No draft at all → <see cref="MobileConfigPublishErrorType.NoDraftToPublish"/>.</item>
    /// <item>Validates the draft's <c>ConfigurationJson</c> (never containing a <c>theme</c> key)
    /// via <see cref="IMobileConfigValidator"/>. Invalid → <see cref="MobileConfigPublishErrorType.ValidationFailed"/>,
    /// nothing persisted.</item>
    /// <item>Composes the tenant's current <c>MobileTheme</c> into a copy of the validated JSON
    /// (adds <c>"theme"</c>) — this composed document becomes the published version's content.</item>
    /// <item>Atomically: marks that version Published, sets <c>MobileConfiguration.PublishedVersionId</c>,
    /// and repoints <c>DraftVersionId</c> at a brand-new cloned Draft row seeded from the
    /// ORIGINAL (theme-less) draft content — so a subsequent <c>SaveDraftAsync</c> call can never
    /// mutate the now-immutable published row, and the draft itself still never carries a
    /// <c>theme</c> key.</item>
    /// </list>
    /// Two concurrent calls for the same tenant never corrupt state or produce two published
    /// versions — the loser gets <see cref="MobileConfigPublishErrorType.ConcurrentPublish"/>,
    /// safe to retry. On any error, the previous published version (if any) is left completely
    /// untouched and still serving.
    /// </summary>
    Task<(MobileConfigPublishedDto? Published, MobileConfigPublishError? Error)> PublishAsync(
        Guid tenantId, Guid? actingUserId, CancellationToken ct = default);

    /// <summary>
    /// TASK-545. Publishes a NEW version cloned from a historical (non-current) version identified
    /// by <paramref name="targetVersionId"/> — never a destructive revert; the target version itself
    /// is untouched and remains in history. <paramref name="targetVersionId"/> must be neither the
    /// tenant's current <c>PublishedVersionId</c> nor its current <c>DraftVersionId</c> —
    /// <see cref="MobileConfigPublishErrorType.CannotRollbackToCurrentVersion"/> otherwise; must
    /// belong to this tenant — <see cref="MobileConfigPublishErrorType.VersionNotFound"/> otherwise.
    /// <list type="number">
    /// <item>Splits the target's composed <c>ConfigurationJson</c> back into <c>theme</c> and the
    /// theme-less body (the inverse of what a publish composes).</item>
    /// <item>Overwrites the tenant's LIVE <c>MobileTheme</c> row with the historical theme — required
    /// so a subsequent normal <see cref="PublishAsync"/> (which always composes from the CURRENT
    /// <c>MobileTheme</c>) does not silently regress the rollback.</item>
    /// <item>Runs the theme-less body through the same archive-previous / mark-published /
    /// clone-new-draft / repoint-pointers / atomic-save sequence <see cref="PublishAsync"/> uses,
    /// reusing the tenant's current draft row as the row that becomes the new published version —
    /// same mechanics as a normal publish, so any of that draft's own unpublished edits are
    /// superseded by the restored historical content, exactly as a normal publish supersedes
    /// whatever was previously published.</item>
    /// </list>
    /// Same concurrency guarantee as <see cref="PublishAsync"/> — a losing concurrent writer gets
    /// <see cref="MobileConfigPublishErrorType.ConcurrentPublish"/>, safe to retry.
    /// </summary>
    Task<(MobileConfigPublishedDto? Published, MobileConfigPublishError? Error)> RollbackAsync(
        Guid tenantId, Guid targetVersionId, Guid? actingUserId, CancellationToken ct = default);
}
