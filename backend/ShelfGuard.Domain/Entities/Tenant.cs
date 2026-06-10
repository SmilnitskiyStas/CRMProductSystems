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

    /// <summary>Changes the billing plan (provider-only).</summary>
    public string? UpdatePlan(string plan)
    {
        var valid = new[] { "basic", "standard", "enterprise", "trial" };
        if (!valid.Contains(plan, StringComparer.OrdinalIgnoreCase))
            return $"Unknown plan '{plan}'. Valid: {string.Join(", ", valid)}.";
        Plan = plan.ToLowerInvariant();
        return null;
    }

    /// <summary>Replaces the enabled modules list (provider-only).</summary>
    public string? UpdateModules(IReadOnlyList<string> modules)
    {
        var valid = new[] { "shelf_manager", "crm", "notifications", "auto_order", "iot", "cv_camera" };
        var unknown = modules.Where(m => !valid.Contains(m, StringComparer.OrdinalIgnoreCase)).ToList();
        if (unknown.Count > 0)
            return $"Unknown modules: {string.Join(", ", unknown)}.";
        Modules = System.Text.Json.JsonSerializer.Serialize(modules);
        return null;
    }

    /// <summary>Soft-deactivates the tenant (provider-only).</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Re-activates a previously deactivated tenant (provider-only).</summary>
    public void Activate() => IsActive = true;
}
