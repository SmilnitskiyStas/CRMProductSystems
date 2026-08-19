using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>
/// Data access for the <see cref="MobileConfiguration"/> root/pointer row and its
/// <see cref="MobileConfigurationVersion"/> children (TASK-531 domain, TASK-532 Draft CRUD). Runs
/// entirely under the calling staff request's own tenant RLS context — same as
/// <see cref="IBannerRepository"/>, no <c>ITenantSessionOverride</c> needed here.
/// </summary>
public interface IMobileConfigurationRepository
{
    /// <summary>
    /// Loads the tenant's <see cref="MobileConfiguration"/> row with <c>DraftVersion</c> and
    /// <c>Theme</c> eagerly loaded (callers need them to mutate an existing draft/theme in place
    /// without a second round-trip — TASK-536 added the <c>Theme</c> include for
    /// <c>MobileThemeService</c>, harmless to the pre-existing draft-only callers since it's a
    /// one-to-one join). Null if the tenant has no <see cref="MobileConfiguration"/> row yet.
    /// </summary>
    Task<MobileConfiguration?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Loads the tenant's <see cref="MobileConfiguration"/> row with <c>PublishedVersion</c>
    /// eagerly loaded (TASK-534's consumer-facing read — never needs <c>DraftVersion</c>, so a
    /// draft is structurally unreachable through this method). Read-only (untracked). Null if the
    /// tenant has no <see cref="MobileConfiguration"/> row yet.
    ///
    /// Deliberately does NOT include <c>Theme</c> (TASK-544 reconciliation): the published read
    /// path now sources <c>theme</c> from <c>PublishedVersion.ConfigurationJson.theme</c> — the
    /// snapshot <see cref="MobileConfigurationVersion.ConfigurationJson"/>
    /// <c>MobileConfigPublishService</c> composes at publish time — not from a live join against
    /// this tenant's current, possibly-since-edited <see cref="MobileTheme"/> row. See
    /// <c>MobileConfigPublishedReadService</c>'s remarks for the full reconciliation.
    /// </summary>
    Task<MobileConfiguration?> GetPublishedByTenantIdAsync(Guid tenantId, CancellationToken ct = default);

    Task AddAsync(MobileConfiguration configuration, CancellationToken ct = default);

    void Update(MobileConfiguration configuration);

    Task AddVersionAsync(MobileConfigurationVersion version, CancellationToken ct = default);

    void UpdateVersion(MobileConfigurationVersion version);

    /// <summary>
    /// Persists a brand-new <see cref="MobileTheme"/> row (TASK-536, first theme write for a
    /// tenant). Unlike <see cref="AddVersionAsync"/>/<see cref="MobileConfiguration.SetDraftVersion"/>,
    /// <see cref="MobileConfiguration"/> holds no FK pointer back at <see cref="MobileTheme"/> (the
    /// relationship is one-directional — only <c>MobileTheme.MobileConfigurationId</c> points at
    /// the parent), so this never needs the two-<c>SaveChangesAsync</c>-calls split
    /// <c>MobileConfigDraftService.SaveDraftAsync</c> requires for its
    /// <c>DraftVersionId</c>/<c>MobileConfigurationId</c> mutual-pointer case (TASK-534b).
    /// </summary>
    Task AddThemeAsync(MobileTheme theme, CancellationToken ct = default);

    void UpdateTheme(MobileTheme theme);

    /// <summary>
    /// Highest existing <see cref="MobileConfigurationVersion.Version"/> across this tenant's
    /// version rows (any status), or 0 if none exist yet — callers add 1 for the next number.
    /// </summary>
    Task<int> GetMaxVersionNumberAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Loads a single <see cref="MobileConfigurationVersion"/> row by id, scoped to
    /// <paramref name="tenantId"/> (defense in depth alongside RLS — never trust a bare
    /// <c>versionId</c> from a route parameter without also checking tenant ownership). Tracked, not
    /// <c>AsNoTracking</c> — <c>MobileConfigPublishService</c> (TASK-545) uses this both to
    /// load-and-mutate the tenant's previously-published version (<see cref="MobileConfigurationVersion.Archive"/>)
    /// and to load a historical rollback target for reading. Null if no version with that id exists
    /// for this tenant (wrong id, or belongs to a different tenant).
    /// </summary>
    Task<MobileConfigurationVersion?> GetVersionByIdAsync(Guid tenantId, Guid versionId, CancellationToken ct = default);

    /// <summary>
    /// Every version row for the tenant — draft, published, and archived — newest-first by
    /// <see cref="MobileConfigurationVersion.Version"/> descending (TASK-545 version history).
    /// Read-only (untracked).
    /// </summary>
    Task<IReadOnlyList<MobileConfigurationVersion>> GetVersionsForTenantAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Throws <see cref="ShelfGuard.Domain.Exceptions.ConcurrencyConflictException"/> (TASK-544) if
    /// either of two concurrent-write races on the same tenant is detected: (1) an xmin
    /// optimistic-concurrency check on <see cref="MobileConfiguration"/> or
    /// <see cref="MobileConfigurationVersion"/> finds a row was modified by another writer since it
    /// was loaded, or (2) a unique-constraint violation on
    /// <c>(MobileConfigurationId, Version)</c> — two concurrent publishes both computed the same
    /// next version number before either committed. Both cases mean the same thing to a caller:
    /// e.g. two concurrent <c>MobileConfigPublishService.PublishAsync</c> calls for the same
    /// tenant; safe to retry.
    /// </summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}
