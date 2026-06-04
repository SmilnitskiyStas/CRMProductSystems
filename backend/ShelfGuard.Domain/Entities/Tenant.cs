namespace ShelfGuard.Domain.Entities;

public sealed class Tenant
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string Plan { get; private set; } = "basic";
    public string Modules { get; private set; } = "[]";
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAt { get; private set; }

    public ICollection<User> Users { get; private set; } = new List<User>();

    private Tenant() { }

    public static Tenant Create(string name, string slug) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Slug = slug.ToLowerInvariant(),
        Plan = "basic",
        Modules = "[]",
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
    };
}
