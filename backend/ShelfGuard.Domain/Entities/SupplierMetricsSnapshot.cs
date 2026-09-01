namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Append-only daily snapshot of a supplier's aggregate performance metrics (TASK-670).
/// Written once per (supplier, day) by the nightly supplier-metrics worker job via an
/// idempotent upsert on (SupplierId, SnapshotDate), so re-runs on the same day overwrite
/// rather than duplicate. Column set mirrors <see cref="SupplierMetrics"/>; feeds the
/// buyer-facing metric trend-chart detail page.
/// </summary>
public sealed class SupplierMetricsSnapshot
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SupplierId { get; init; }
    public Guid TenantId { get; init; }
    /// <summary>The calendar day this snapshot represents (one row per supplier per day).</summary>
    public DateOnly SnapshotDate { get; init; }

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
    /// <summary>Number of delivered orders behind <see cref="AvgDeliveryDays"/>.</summary>
    public int? DeliverySampleSize { get; set; }
    /// <summary>Number of chat sessions behind <see cref="ResponseTimeHours"/>.</summary>
    public int? ResponseSampleSize { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public Supplier? Supplier { get; init; }
    public Tenant? Tenant { get; init; }
}
