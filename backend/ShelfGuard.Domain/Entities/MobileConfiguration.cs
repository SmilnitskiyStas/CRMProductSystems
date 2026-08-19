namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Root/pointer record for one tenant's mobile app configuration — the multi-tenant consumer
/// app-builder domain (CLAUDE CODE SPEC ЕТАП 3, TASK-531). Exactly one row per tenant. Points at
/// the tenant's current draft and published <see cref="MobileConfigurationVersion"/> snapshots;
/// the versions themselves hold the actual serialized document
/// (<see cref="MobileConfigurationVersion.ConfigurationJson"/>).
///
/// The two version pointers (<see cref="PublishedVersionId"/>/<see cref="DraftVersionId"/>)
/// deliberately use <c>Restrict</c> delete behavior, not <c>Cascade</c> — even though
/// <see cref="MobileConfigurationVersion.MobileConfigurationId"/> cascades the other way. This is
/// the intentional "root + versions with a pointer" shape: deleting a
/// <see cref="MobileConfiguration"/> cascades away every version that belongs to it, but a
/// version can never be deleted out from under an active pointer — the pointer has to be cleared
/// first. A single-cascade-path model like this compiles and migrates cleanly under EF Core /
/// PostgreSQL (verified — see TASK-531 task log); it would only be a problem under SQL Server's
/// stricter multiple-cascade-paths restriction, which does not apply here.
/// </summary>
public sealed class MobileConfiguration
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }

    /// <summary>Null until the tenant's first publish.</summary>
    public Guid? PublishedVersionId { get; private set; }

    /// <summary>Null when there is no in-progress draft.</summary>
    public Guid? DraftVersionId { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    // Navigation (optional, for eager-loading) — mirrors Banner.Tenant/Creator style.
    public Tenant? Tenant { get; private set; }
    public MobileConfigurationVersion? PublishedVersion { get; private set; }
    public MobileConfigurationVersion? DraftVersion { get; private set; }
    public MobileTheme? Theme { get; private set; }

    private MobileConfiguration() { }

    /// <summary>Creates the root record for a tenant with no draft/published version yet.</summary>
    public static MobileConfiguration Create(Guid tenantId)
    {
        var now = DateTime.UtcNow;
        return new MobileConfiguration
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PublishedVersionId = null,
            DraftVersionId = null,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Repoints the draft version (e.g. after a new draft is created, or discarded → null).</summary>
    public void SetDraftVersion(Guid? versionId)
    {
        DraftVersionId = versionId;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Repoints the published version (backs the future publish/rollback flow, TASK-532+).</summary>
    public void SetPublishedVersion(Guid? versionId)
    {
        PublishedVersionId = versionId;
        UpdatedAt = DateTime.UtcNow;
    }
}
