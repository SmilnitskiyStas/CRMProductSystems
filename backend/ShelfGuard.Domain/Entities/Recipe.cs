namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Production recipe — defines what item is produced, in what quantity,
/// and which ingredients are consumed. Maps to "recipes" table.
/// </summary>
public sealed class Recipe
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public string Name { get; set; } = string.Empty;
    /// <summary>The item that is produced by this recipe.</summary>
    public Guid OutputItemId { get; set; }
    public decimal OutputQty { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Tenant? Tenant { get; init; }
    public Item? OutputItem { get; init; }
    public ICollection<RecipeIngredient> Ingredients { get; init; } = new List<RecipeIngredient>();
}
