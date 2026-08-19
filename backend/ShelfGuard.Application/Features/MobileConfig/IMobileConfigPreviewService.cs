namespace ShelfGuard.Application.Features.MobileConfig;

/// <summary>
/// Staff-facing, read-only preview of a tenant's current DRAFT mobile app configuration (TASK-547
/// — closes out Stage D). Lets a Retailer Admin see exactly what publishing right now would
/// produce, without actually publishing anything.
///
/// Deliberately mirrors <see cref="IMobileConfigPublishedReadService"/>'s document shape
/// (<c>schemaVersion</c>/<c>configVersion</c>/<c>tenant</c>/<c>theme</c>/<c>features</c>/
/// <c>navigation</c>/<c>pages</c>) so the App Builder UI can render a preview with the exact same
/// renderer it uses for the real consumer document — but sources the body from the tenant's DRAFT
/// <see cref="ShelfGuard.Domain.Entities.MobileConfigurationVersion"/> instead of the published
/// one, and composes <c>theme</c> LIVE off the tenant's current
/// <see cref="ShelfGuard.Domain.Entities.MobileTheme"/> row (same composition
/// <c>MobileConfigPublishService.ComposeTheme</c> would apply at the next real publish — see
/// <see cref="MobileConfigPreviewService"/> for why this can't just reuse a draft's own
/// <c>ConfigurationJson.theme</c>: TASK-532's invariant is that a draft never carries one).
///
/// Purely a read: never calls <c>SaveChangesAsync</c>, never touches
/// <see cref="ShelfGuard.Domain.Entities.MobileConfigurationVersion"/> status, never calls into
/// <see cref="IMobileConfigPublishService"/>.
/// </summary>
public interface IMobileConfigPreviewService
{
    /// <summary>
    /// Returns the fully composed preview document for <paramref name="tenantId"/>'s current
    /// draft, serialized exactly as it should be served (matches
    /// <c>IMobileConfigPublishedReadService.GetPublishedConfigAsync</c>'s <c>DocumentJson</c>
    /// shape, plus a leading <c>hasDraft</c> flag). Never null and never throws for "no draft yet"
    /// — that is a legitimate, expected state (a brand-new tenant) represented by
    /// <c>hasDraft: false</c> and empty/default <c>features</c>/<c>navigation</c>/<c>pages</c>,
    /// same "propose defaults, don't 404" spirit as <c>MobileConfigDraftController.Get</c>.
    /// </summary>
    Task<string> GetPreviewAsync(Guid tenantId, CancellationToken ct = default);
}
