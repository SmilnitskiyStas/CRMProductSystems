namespace ShelfGuard.Domain.Entities.Erp;

/// <summary>Durable fiscal compensation, separate from the immutable inventory reversal.</summary>
public sealed class PosReturnFiscalization
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid SaleId { get; init; }
    public Guid ShiftId { get; init; }
    public Guid ClientRequestId { get; init; }
    public string RequestHash { get; init; } = "";
    public string Reason { get; init; } = "";
    public string PaymentType { get; init; } = "";
    public decimal Amount { get; init; }
    public decimal TaxAmount { get; init; }
    public string RequestJson { get; init; } = "{}";
    public string Status { get; set; } = "pending";
    public int Attempts { get; set; }
    public string? FiscalNumber { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
