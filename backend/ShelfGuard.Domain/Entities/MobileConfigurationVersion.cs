namespace ShelfGuard.Domain.Entities;

/// <summary>
/// An immutable-once-published snapshot of one tenant's mobile app configuration document
/// (features/navigation/pages — MASTER SPEC §11's response shape minus <c>tenant</c>, which is
/// resolved separately at read time). Versions are append-only per tenant
/// (<see cref="Version"/> increments, never reused) — <see cref="MobileConfiguration"/> is the
/// mutable root that points at whichever version is currently draft/published.
/// </summary>
public sealed class MobileConfigurationVersion
{
    public Guid Id { get; private set; }
    public Guid MobileConfigurationId { get; private set; }

    /// <summary>
    /// Denormalized from the parent <see cref="MobileConfiguration"/> so RLS can scope this table
    /// directly on its own column, without a join — same house pattern as
    /// <c>LoyaltyLedgerEntry.TenantId</c> (see database-schema.md).
    /// </summary>
    public Guid TenantId { get; private set; }

    /// <summary>Incrementing per tenant, starting at 1. Never reused, even after archiving.</summary>
    public int Version { get; private set; }

    /// <summary>
    /// Schema version of <see cref="ConfigurationJson"/>'s document shape — matches the future
    /// <c>/contracts/mobile-config.schema.json</c> (TASK-533).
    /// </summary>
    public int SchemaVersion { get; private set; }

    /// <summary>draft | published | archived — see <see cref="MobileConfigurationVersionStatus"/>.</summary>
    public string Status { get; private set; } = MobileConfigurationVersionStatus.Draft;

    /// <summary>
    /// The serialized configuration document (schemaVersion/theme/features/navigation/pages —
    /// MASTER SPEC §11 minus <c>tenant</c>). Whitelist validation is TASK-532's job, not this
    /// entity's — this layer only stores/retrieves the JSON.
    /// </summary>
    public string ConfigurationJson { get; private set; } = "{}";

    /// <summary>Staff user who created this version. Nullable/SetNull.</summary>
    public Guid? CreatedBy { get; private set; }

    public DateTime CreatedAt { get; private set; }

    /// <summary>Null until this version is published; set once, never cleared (append-only history).</summary>
    public DateTime? PublishedAt { get; private set; }

    // Navigation
    public MobileConfiguration? MobileConfiguration { get; private set; }
    public User? Creator { get; private set; }

    private MobileConfigurationVersion() { }

    /// <summary>Creates a new draft version. Publishing is a separate call (<see cref="Publish"/>).</summary>
    public static MobileConfigurationVersion Create(
        Guid mobileConfigurationId,
        Guid tenantId,
        int version,
        int schemaVersion,
        string configurationJson,
        Guid? createdBy = null)
    {
        return new MobileConfigurationVersion
        {
            Id = Guid.NewGuid(),
            MobileConfigurationId = mobileConfigurationId,
            TenantId = tenantId,
            Version = version,
            SchemaVersion = schemaVersion,
            Status = MobileConfigurationVersionStatus.Draft,
            ConfigurationJson = configurationJson,
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow,
            PublishedAt = null,
        };
    }

    /// <summary>Replaces the draft document. Throws once this version is no longer a draft — published/archived versions are immutable.</summary>
    public void UpdateConfigurationJson(string configurationJson)
    {
        if (Status != MobileConfigurationVersionStatus.Draft)
        {
            throw new InvalidOperationException(
                $"Cannot edit a {Status} MobileConfigurationVersion — only draft versions are mutable.");
        }

        ConfigurationJson = configurationJson;
    }

    /// <summary>Transitions draft → published. Idempotent on <see cref="PublishedAt"/> — never overwrites the original publish timestamp.</summary>
    public void Publish(DateTime utcNow)
    {
        Status = MobileConfigurationVersionStatus.Published;
        PublishedAt ??= utcNow;
    }

    /// <summary>Transitions published → archived (e.g. superseded by a newer publish/rollback).</summary>
    public void Archive()
    {
        Status = MobileConfigurationVersionStatus.Archived;
    }
}

/// <summary>Valid <see cref="MobileConfigurationVersion.Status"/> values.</summary>
public static class MobileConfigurationVersionStatus
{
    public const string Draft = "draft";
    public const string Published = "published";
    public const string Archived = "archived";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Draft, Published, Archived };
}
