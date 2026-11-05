namespace ShelfGuard.Domain.Entities;

/// <summary>Status values for <see cref="ProductionRequest.Status"/> (stored as text).</summary>
public static class ProductionRequestStatus
{
    public const string Draft = "draft";
    public const string Submitted = "submitted";
    public const string Accepted = "accepted";
    public const string Partial = "partial";
    public const string Rejected = "rejected";
    public const string Fulfilled = "fulfilled";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Draft, Submitted, Accepted, Partial, Rejected, Fulfilled, Cancelled,
    };
}

/// <summary>Origin of a <see cref="ProductionRequest"/>. Auto sources always start as drafts.</summary>
public static class ProductionRequestSource
{
    public const string Manual = "manual";
    public const string AutoBuffer = "auto_buffer";
    public const string AutoPromo = "auto_promo";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Manual, AutoBuffer, AutoPromo };
}

/// <summary>
/// A store's request to the in-house production location for finished goods.
/// Maps to "production_requests". RLS: tenant_isolation + store_scope (store OR production location).
/// </summary>
public sealed class ProductionRequest
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    /// <summary>Requesting store (a <see cref="Location"/>).</summary>
    public Guid StoreLocationId { get; init; }
    /// <summary>Production location (a <see cref="Location"/> with Type = "production") that fulfils it.</summary>
    public Guid ProductionLocationId { get; init; }
    public DateOnly NeededBy { get; set; }
    public string Status { get; set; } = ProductionRequestStatus.Draft;
    /// <summary>manual | auto_buffer | auto_promo. Auto drafts may re-evaluate it on refresh.</summary>
    public string Source { get; set; } = ProductionRequestSource.Manual;
    public string? Notes { get; set; }
    /// <summary>Overall production-side reason (rejection or partial acceptance).</summary>
    public string? ReviewNote { get; set; }
    public Guid? CreatedBy { get; init; }
    public DateTime? SubmittedAt { get; set; }
    public Guid? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    /// <summary>Stock transfer (production → store) created on shipment.</summary>
    public Guid? ShipmentTransferId { get; set; }
    public DateTime? ShippedAt { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ProductionRequestLine> Lines { get; init; } = new List<ProductionRequestLine>();

    public Location? StoreLocation { get; set; }
    public Location? ProductionLocation { get; set; }
    public StockTransfer? ShipmentTransfer { get; set; }
}

/// <summary>One requested item. <see cref="AcceptedQty"/> is null until production reviews the request.</summary>
public sealed class ProductionRequestLine
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid RequestId { get; init; }
    public Guid ItemId { get; init; }
    public decimal RequestedQty { get; set; }
    public decimal? AcceptedQty { get; set; }
    /// <summary>Production-side reason when AcceptedQty differs from RequestedQty.</summary>
    public string? Reason { get; set; }
    /// <summary>Why the auto-generator proposed this line (target level, stock, promo uplift). Null for manual lines.</summary>
    public string? AutoReason { get; set; }

    public ProductionRequest? Request { get; init; }
    public Item? Item { get; set; }
}

/// <summary>Allocation of a request line to a production order (m:n). Maps to "production_order_request_lines".</summary>
public sealed class ProductionOrderRequestLine
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid ProductionOrderId { get; init; }
    public Guid RequestLineId { get; init; }
    public decimal Qty { get; set; }

    public ProductionOrder? ProductionOrder { get; init; }
    public ProductionRequestLine? RequestLine { get; set; }
}
