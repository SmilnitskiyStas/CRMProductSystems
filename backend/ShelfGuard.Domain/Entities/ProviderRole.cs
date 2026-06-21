namespace ShelfGuard.Domain.Entities;

public sealed class ProviderRole
{
    public Guid Id { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string BaseRole { get; private set; } = string.Empty;
    public List<string> Permissions { get; private set; } = [];
    public bool IsSystem { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private ProviderRole() { }

    public static ProviderRole Create(string displayName, string baseRole, IEnumerable<string> permissions) => new()
    {
        Id          = Guid.NewGuid(),
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
