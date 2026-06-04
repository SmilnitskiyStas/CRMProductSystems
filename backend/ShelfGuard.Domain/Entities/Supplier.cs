namespace ShelfGuard.Domain.Entities;

public sealed class Supplier
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public string Name { get; set; } = string.Empty;
    public string? Edrpou { get; set; }
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public int DeliveryDays { get; set; } = 3;
    public bool HasSupplierPortal { get; set; }
    public bool ReturnPolicy { get; set; }
    public string? PaymentTerms { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;

    public Tenant? Tenant { get; init; }
}
