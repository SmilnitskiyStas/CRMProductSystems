namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Platform-wide product category. Unlike the (removed) per-tenant <c>Category</c>, this is a
/// single global catalogue the SaaS provider curates: every tenant draws its category list from
/// here, filtered to the entries whose <see cref="BusinessTypes"/> match the tenant's
/// <c>Tenant.BusinessType</c>. Hierarchical via <see cref="ParentId"/>.
///
/// Global reference data — no <c>TenantId</c>, no RLS. Reads are open to any authenticated user;
/// writes go only through the provider-only endpoints (<c>api/provider/categories</c>).
/// </summary>
public sealed class PlatformCategory
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>Self-referencing parent for the category tree; <c>null</c> at the root.</summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// Business types this category applies to (values from <c>Tenant.UpdateBusinessType</c>'s
    /// allow-list: <c>retail</c>, <c>auto_service</c>, <c>production</c>, …). An empty list means
    /// "applies to every business type". Stored as a jsonb string array.
    /// </summary>
    public List<string> BusinessTypes { get; set; } = [];

    /// <summary>Display order among siblings (ascending).</summary>
    public int SortOrder { get; set; }

    /// <summary>Soft-delete / hide flag. Inactive categories stay linked to their items but are
    /// hidden from tenant filters and the product form.</summary>
    public bool IsActive { get; set; } = true;

    // ── Default item attributes ──────────────────────────────────────────────
    // Suggested values the product form pre-fills when a merchandiser picks this category
    // (e.g. "Молочні продукти" → VAT 20, class "chilled", MTS). All nullable — null means
    // "no suggestion, keep the form's own default". The user always validates/overrides.

    /// <summary>Suggested VAT rate (%), 0–100.</summary>
    public decimal? DefaultVatRate { get; set; }

    /// <summary>Suggested perishability class: <c>fresh</c> | <c>chilled</c> | <c>standard</c> | <c>durable</c>.</summary>
    public string? DefaultPerishabilityClass { get; set; }

    /// <summary>Suggested management type: <c>MTS</c> | <c>MTO</c> (matches the product form's options).</summary>
    public string? DefaultManagementType { get; set; }

    /// <summary>Suggested item type: <c>product</c> | <c>service</c> | <c>spare_part</c> | … .</summary>
    public string? DefaultItemType { get; set; }

    /// <summary>Suggested shelf life in days (&gt; 0).</summary>
    public int? DefaultShelfLifeDays { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public PlatformCategory? Parent { get; init; }
}
