namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Single ingredient line within a recipe.
/// No TenantId — tenant scope inherited from parent Recipe via JOIN.
/// Maps to "recipe_ingredients" table.
/// </summary>
public sealed class RecipeIngredient
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid RecipeId { get; init; }
    public Guid ItemId { get; set; }
    public decimal Qty { get; set; }
    public string Unit { get; set; } = string.Empty;

    public Recipe? Recipe { get; init; }
    public Item? Item { get; init; }
}
