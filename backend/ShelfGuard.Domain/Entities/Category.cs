namespace ShelfGuard.Domain.Entities;

public sealed class Category
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public bool IsActive { get; set; } = true;

    public Tenant? Tenant { get; init; }
    public Category? Parent { get; init; }
}
