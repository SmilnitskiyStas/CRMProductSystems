namespace ShelfGuard.Application.Features.Inventory.Dtos;

public record ProductDto(
    Guid Id,
    string Sku,
    string Name,
    string? Description,
    string Category,
    string Unit,
    decimal CostPrice,
    decimal SalePrice,
    decimal StockQuantity,
    decimal ReorderLevel,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record CreateProductRequest(
    string Sku,
    string Name,
    string? Description,
    string Category,
    string Unit,
    decimal CostPrice,
    decimal SalePrice,
    decimal ReorderLevel
);

public record UpdateProductRequest(
    string Name,
    string? Description,
    string Category,
    string Unit,
    decimal CostPrice,
    decimal SalePrice,
    decimal ReorderLevel,
    bool IsActive
);
