namespace ShelfGuard.Domain.Entities;

public sealed class Discount
{
    public Guid     Id              { get; private set; }
    public Guid     TenantId        { get; private set; }
    public Guid?    ProductStockId  { get; private set; }
    public Guid     ProductId       { get; private set; }
    public Guid     StoreId         { get; private set; }
    public Guid?    PromotionCampaignId { get; private set; }

    /// <summary>Percentage value — e.g. 20.00 means 20 %.</summary>
    public decimal  DiscountPercent { get; private set; }
    public decimal? PriceOriginal   { get; private set; }
    public decimal? PriceDiscounted { get; private set; }

    /// <summary>Reason code: expiry | overstock | promo</summary>
    public string   Reason          { get; private set; } = DiscountReason.Expiry;

    public DateTime  ValidFrom      { get; private set; }
    public DateTime? ValidUntil     { get; private set; }

    /// <summary>pending | active | expired | cancelled</summary>
    public string    Status         { get; private set; } = DiscountStatus.Pending;

    public bool      AutoApplied    { get; private set; }
    public Guid?     CreatedBy      { get; private set; }
    public Guid?     ApprovedBy     { get; private set; }
    public DateTime  CreatedAt      { get; private set; }
    public DateTime? ApprovedAt     { get; private set; }
    public DateTime? WebhookSentAt  { get; private set; }

    // Navigation (optional, for eager-loading)
    public Tenant? Tenant   { get; private set; }
    public User?   Creator  { get; private set; }
    public User?   Approver { get; private set; }

    private Discount() { }

    public static Discount Create(
        Guid     tenantId,
        Guid     productId,
        Guid     storeId,
        decimal  discountPercent,
        string   reason,
        Guid?    productStockId = null,
        decimal? priceOriginal  = null,
        DateTime? validFrom     = null,
        DateTime? validUntil    = null,
        bool     autoApplied    = false,
        Guid?    createdBy      = null,
        Guid?    promotionCampaignId = null)
    {
        var discounted = priceOriginal.HasValue
            ? Math.Round(priceOriginal.Value * (1m - discountPercent / 100m), 2)
            : (decimal?)null;

        return new Discount
        {
            Id              = Guid.NewGuid(),
            TenantId        = tenantId,
            ProductId       = productId,
            StoreId         = storeId,
            ProductStockId  = productStockId,
            DiscountPercent = discountPercent,
            PriceOriginal   = priceOriginal,
            PriceDiscounted = discounted,
            Reason          = reason,
            ValidFrom       = validFrom ?? DateTime.UtcNow,
            ValidUntil      = validUntil,
            Status          = DiscountStatus.Pending,
            AutoApplied     = autoApplied,
            CreatedBy       = createdBy,
            PromotionCampaignId = promotionCampaignId,
            CreatedAt       = DateTime.UtcNow,
        };
    }

    /// <summary>Approves a pending discount. Throws if not in pending state.</summary>
    public void Approve(Guid approvedBy)
    {
        if (Status != DiscountStatus.Pending)
            throw new InvalidOperationException(
                $"Cannot approve discount in status '{Status}'. Only 'pending' discounts can be approved.");

        Status     = DiscountStatus.Active;
        ApprovedBy = approvedBy;
        ApprovedAt = DateTime.UtcNow;
    }

    /// <summary>Cancels a pending or active discount.</summary>
    public void Cancel()
    {
        if (Status is DiscountStatus.Expired or DiscountStatus.Cancelled)
            throw new InvalidOperationException(
                $"Cannot cancel a discount in status '{Status}'.");

        Status = DiscountStatus.Cancelled;
    }

    /// <summary>Marks webhook as sent to the cash register.</summary>
    public void MarkWebhookSent() => WebhookSentAt = DateTime.UtcNow;

    /// <summary>Transitions active → expired when ValidUntil has passed.</summary>
    public void ExpireIfOverdue(DateTime utcNow)
    {
        if (Status == DiscountStatus.Active
            && ValidUntil.HasValue
            && ValidUntil.Value <= utcNow)
        {
            Status = DiscountStatus.Expired;
        }
    }
}

public static class DiscountStatus
{
    public const string Pending   = "pending";
    public const string Active    = "active";
    public const string Expired   = "expired";
    public const string Cancelled = "cancelled";
}

public static class DiscountReason
{
    public const string Expiry    = "expiry";
    public const string Overstock = "overstock";
    public const string Promo     = "promo";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { Expiry, Overstock, Promo };
}
