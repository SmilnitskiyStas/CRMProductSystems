namespace ShelfGuard.Domain.Entities;

public sealed class PosShift
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid StoreId { get; init; }
    public Guid? CashierId { get; init; }
    public int ShiftNumber { get; init; }
    // Shift number assigned by ДПС; null while in test mode / not yet fiscalized
    public string? FiscalShiftNumber { get; set; }
    public DateTime OpenedAt { get; init; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }
    public decimal? OpeningCash { get; init; }
    public decimal? ClosingCash { get; set; }
    public decimal TotalSales { get; set; }
    public string? ZReportUrl { get; set; }

    // Navigation properties
    public Store? Store { get; init; }
    public User? Cashier { get; init; }
}
