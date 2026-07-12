namespace ShelfGuard.Domain.Entities;

public sealed class NotificationQueue
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? TenantId { get; init; }
    public Guid? UserId { get; init; }

    /// <summary>
    /// Store/location scope of the event (TASK-338, ADR-018 §3). Nullable — not every
    /// notification is store-scoped (e.g. weekly_report is tenant-wide). C# property is
    /// named StoreId but maps to DB column "LocationId" — same rename-era convention as
    /// StockStatusSnapshot.StoreId, DailySale.StoreId, StockMovement.FromStoreId/ToStoreId.
    /// </summary>
    public Guid? StoreId { get; init; }

    /// <summary>
    /// Short human-readable summary (e.g. "Надійшла поставка №1234 — Хрещатик"), populated by
    /// whichever service enqueues the row. Backs keyword search via pg_trgm so callers don't
    /// have to parse the Payload JSONB on every query (ADR-018 §3).
    /// </summary>
    public string? Title { get; init; }

    public string Channel { get; init; } = string.Empty;
    public string? EventType { get; init; }
    public string? Payload { get; init; }
    public string Status { get; set; } = "pending";
    public int RetryCount { get; set; }
    public DateTime? SentAt { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public bool IsRead { get; set; } = false;
    public DateTime? ReadAt { get; set; }

    public void MarkRead()
    {
        IsRead = true;
        ReadAt = DateTime.UtcNow;
    }

    public void MarkUnread()
    {
        IsRead = false;
        ReadAt = null;
    }
}
