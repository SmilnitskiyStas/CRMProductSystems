namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Custom staff role scoped to a single supplier tenant (unlike ProviderRole,
/// which is global to the platform). Each supplier tenant manages its own set
/// of roles independently.
/// </summary>
public sealed class SupplierRole
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string BaseRole { get; private set; } = string.Empty;
    public List<string> Permissions { get; private set; } = [];
    public bool IsSystem { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private SupplierRole() { }

    public static SupplierRole Create(Guid tenantId, string displayName, string baseRole, IEnumerable<string> permissions) => new()
    {
        Id          = Guid.NewGuid(),
        TenantId    = tenantId,
        DisplayName = displayName,
        BaseRole    = baseRole,
        Permissions = permissions.ToList(),
        IsSystem    = false,
        CreatedAt   = DateTime.UtcNow,
    };

    public void Update(string displayName, string baseRole, IEnumerable<string> permissions)
    {
        DisplayName = displayName;
        BaseRole    = baseRole;
        Permissions = permissions.ToList();
    }
}
