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
    // completed / pending_fiscalization / fiscalized / cancelled
    public string Status { get; set; } = "pending_fiscalization";
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public ICollection<PosTransactionItem> Items { get; init; } = new List<PosTransactionItem>();

    // Navigation properties
    public Store? Store { get; init; }
    public PosShift? Shift { get; init; }
}
