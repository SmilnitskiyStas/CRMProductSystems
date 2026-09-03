namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Aggregated performance metrics for a supplier.
/// 1-to-1 with Supplier. Updated by background job.
/// </summary>
public sealed class SupplierMetrics
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SupplierId { get; init; }
    public Guid TenantId { get; init; }
    /// <summary>Average delivery time in days.</summary>
    public decimal? AvgDeliveryDays { get; set; }
    /// <summary>Fraction of orders delivered correctly (0.0000–1.0000).</summary>
    public decimal? OrderAccuracy { get; set; }
    /// <summary>Quality score (0.0000–1.0000).</summary>
    public decimal? QualityScore { get; set; }
    /// <summary>Average review rating (1.00–5.00).</summary>
    public decimal? Rating { get; set; }
    /// <summary>Fraction of orders cancelled (0.0000–1.0000).</summary>
    public decimal? CancellationRate { get; set; }
    /// <summary>Average response time in hours.</summary>
    public decimal? ResponseTimeHours { get; set; }

    /// <summary>
    /// TASK-689 (plan `1-partitioned-book.md` Phase 6d, request #10): fraction of delivered orders
    /// that arrived on or before their promised <c>ExpectedDeliveryDate</c> (0.0000–1.0000).
    /// Worker-computed over the same 365-day delivered sample as <see cref="AvgDeliveryDays"/>,
    /// counting only orders that carried an <c>ExpectedDeliveryDate</c>. Null when no delivered
    /// order in the window had a promised date.
    /// </summary>
    public decimal? OnTimeDeliveryRate { get; set; }
    /// <summary>
    /// TASK-689: equal-weight mean of the available (non-null) components
    /// { <see cref="Rating"/>/5, <see cref="OrderAccuracy"/>, <see cref="OnTimeDeliveryRate"/>,
    /// clamp(1 − <see cref="ResponseTimeHours"/>/48, 0, 1) }, rounded to 3 decimals (0.000–1.000).
    /// Worker-computed; null when every component is null. <c>QualityScore</c> stays permanently dead
    /// (no data source) — this is the composite quality signal the marketplace UI renders instead.
    /// </summary>
    public decimal? CompositeScore { get; set; }

    /// <summary>
    /// TASK-649: JSONB array of measured delivery time per destination region, computed by the
    /// nightly supplier-metrics worker job. Shape:
    /// [{ "regionCode": "UA-32", "avgDeliveryDays": 2.4, "sampleSize": 17 }].
    /// </summary>
    public string? DeliveryByRegion { get; set; }
    /// <summary>TASK-649: number of delivered orders behind <see cref="AvgDeliveryDays"/>.</summary>
    public int? DeliverySampleSize { get; set; }
    /// <summary>TASK-649: number of chat sessions behind <see cref="ResponseTimeHours"/>.</summary>
    public int? ResponseSampleSize { get; set; }
    /// <summary>TASK-649: when the worker job last recomputed the aggregate columns.</summary>
    public DateTimeOffset? AggregatesComputedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Supplier? Supplier { get; init; }
    public Tenant? Tenant { get; init; }
}
