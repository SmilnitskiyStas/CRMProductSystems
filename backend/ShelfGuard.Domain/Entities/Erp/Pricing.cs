namespace ShelfGuard.Domain.Entities.Erp;

/// <summary>Append-only complete revision of a planned retail price list. Never updates legacy POS prices.</summary>
public sealed class PriceList
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid ListKey { get; init; }
    public int Version { get; init; }
    public Guid DocumentId { get; init; }
    public string Name { get; init; } = "";
    public string Currency { get; init; } = "UAH";
    public Guid? LocationId { get; init; }
    public int Priority { get; init; }
    public bool IsActive { get; init; }
    public DateTime ValidFrom { get; init; }
    public DateTime? ValidUntil { get; init; }
    public string Fingerprint { get; init; } = "";
    public Guid CreatedBy { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed class PriceListItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid PriceListId { get; init; }
    public Guid ItemId { get; init; }
    public decimal Price { get; init; }
    public decimal VatRate { get; init; }
    public string Unit { get; init; } = "";
    public string ItemName { get; init; } = "";
    public string ItemFingerprint { get; init; } = "";
}

/// <summary>Immutable simulated base offer. It is neither a historical checkout nor a printable retail offer.</summary>
public sealed class PriceResolutionSnapshot
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid CreatedBy { get; init; }
    public Guid LocationId { get; init; }
    public Guid ItemId { get; init; }
    public DateTime EvaluatedAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public string RequestHash { get; init; } = "";
    public string ContentHash { get; init; } = "";
    public string Payload { get; init; } = "{}";
}
