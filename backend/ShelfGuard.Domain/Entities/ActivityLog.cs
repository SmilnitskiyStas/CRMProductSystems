namespace ShelfGuard.Domain.Entities;

public sealed class ActivityLog
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? TenantId { get; init; }
    public Guid? UserId { get; init; }
    public string Action { get; init; } = string.Empty;
    public string? EntityType { get; init; }
    public Guid? EntityId { get; init; }
    public string? Meta { get; init; }
    public string? IpAddress { get; init; }
    public bool IsImpersonated { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
