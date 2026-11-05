namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Link between a ShelfGuard tenant and its Beauty CRM business (ADR-018 control plane).
/// Provider-level table (no RLS), same pattern as landing_leads. Holds NO secrets and NO invite tokens.
/// </summary>
public sealed class BeautyTenantLink
{
    public Guid Id { get; private set; }
    /// <summary>ShelfGuard tenant id; sent to Beauty as externalRef.</summary>
    public Guid TenantId { get; private set; }
    public Guid? BeautyTenantId { get; private set; }
    public string BeautySlug { get; private set; } = string.Empty;
    /// <summary>active | suspended (last known state in Beauty).</summary>
    public string BeautyStatus { get; private set; } = "active";
    public DateTime LastSyncedAt { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private BeautyTenantLink() { }

    public static BeautyTenantLink Create(Guid tenantId, Guid? beautyTenantId, string slug, string status, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        BeautyTenantId = beautyTenantId,
        BeautySlug = slug,
        BeautyStatus = status,
        LastSyncedAt = now,
        CreatedAt = now,
    };

    public void Sync(Guid? beautyTenantId, string? slug, string status, DateTime now)
    {
        if (beautyTenantId is not null) BeautyTenantId = beautyTenantId;
        if (!string.IsNullOrEmpty(slug)) BeautySlug = slug;
        BeautyStatus = status;
        LastSyncedAt = now;
    }
}
