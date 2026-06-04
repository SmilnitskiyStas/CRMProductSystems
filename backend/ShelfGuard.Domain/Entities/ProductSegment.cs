namespace ShelfGuard.Domain.Entities;

public sealed class ProductSegment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public string Name { get; set; } = string.Empty;
    public Guid? CategoryId { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    public Tenant? Tenant { get; init; }
    public Category? Category { get; init; }
}
