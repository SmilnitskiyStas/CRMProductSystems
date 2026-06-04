namespace ShelfGuard.Domain.Entities;

public sealed class Product
{
    public Guid Id { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string Category { get; private set; } = string.Empty;
    public string Unit { get; private set; } = string.Empty;
    public decimal CostPrice { get; private set; }
    public decimal SalePrice { get; private set; }
    public decimal StockQuantity { get; private set; }
    public decimal ReorderLevel { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    // Required by EF Core
    private Product() { }

    public static Product Create(
        string sku,
        string name,
        string? description,
        string category,
        string unit,
        decimal costPrice,
        decimal salePrice,
        decimal reorderLevel) => new()
    {
        Id = Guid.NewGuid(),
        Sku = sku,
        Name = name,
        Description = description,
        Category = category,
        Unit = unit,
        CostPrice = costPrice,
        SalePrice = salePrice,
        StockQuantity = 0,
        ReorderLevel = reorderLevel,
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    public void Update(
        string name,
        string? description,
        string category,
        string unit,
        decimal costPrice,
        decimal salePrice,
        decimal reorderLevel,
        bool isActive)
    {
        Name = name;
        Description = description;
        Category = category;
        Unit = unit;
        CostPrice = costPrice;
        SalePrice = salePrice;
        ReorderLevel = reorderLevel;
        IsActive = isActive;
        UpdatedAt = DateTime.UtcNow;
    }

    public void AdjustStock(decimal delta)
    {
        StockQuantity += delta;
        UpdatedAt = DateTime.UtcNow;
    }
}
