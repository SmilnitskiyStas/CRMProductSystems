namespace ShelfGuard.Domain.Entities;

public sealed class PosTransaction
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid StoreId { get; init; }
    public Guid? CashierId { get; init; }
    public Guid? ShiftId { get; init; }
    // Local sequential number, always assigned (offline-first, ADR-011)
    public string ReceiptNumber { get; init; } = string.Empty;
    // ДПС fiscal number; null until fiscalization completes
    public string? FiscalNumber { get; set; }
    public string PaymentType { get; init; } = "cash"; // cash / card / mixed
    public decimal TotalAmount { get; set; }
    public decimal TaxAmount { get; set; }
    // completed / pending_fiscalization / fiscalized / fiscalization_failed / cancelled
    public string Status { get; set; } = "pending_fiscalization";
    /// <summary>
    /// Number of fiscalization attempts made so far (0 = never attempted).
    /// Incremented on each attempt regardless of outcome.
    /// When it reaches 5 the status is set to fiscalization_failed and no further retries are made.
    /// </summary>
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Optional CRM customer linked to this transaction.</summary>
    public Guid? CustomerId { get; set; }

    /// <summary>TASK-613: reserved for future register-hardware integration. No FK (no register entity exists yet) — intentionally unwired.</summary>
    public Guid? CashRegisterId { get; set; }

    public ICollection<PosTransactionItem> Items { get; init; } = new List<PosTransactionItem>();

    // Navigation properties
    public Location? Store { get; init; }
    public PosShift? Shift { get; init; }
    public Customer? Customer { get; init; }
}
