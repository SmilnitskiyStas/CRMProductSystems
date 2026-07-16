using ShelfGuard.Application.Features.Production.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Production;

/// <summary>
/// Business logic for the Production module.
/// FEFO write-down mirrors the pattern used in AutoServiceService.CompleteWorkOrderAsync.
/// </summary>
public sealed class ProductionService : IProductionService
{
    private readonly IProductionRepository _repo;

    public ProductionService(IProductionRepository repo) => _repo = repo;

    // ── Recipes ───────────────────────────────────────────────────────────────

    public async Task<List<RecipeListItemDto>> GetRecipesAsync(
        bool includeInactive, CancellationToken ct = default)
    {
        var recipes = await _repo.GetRecipesAsync(includeInactive, ct);
        return recipes.Select(r => new RecipeListItemDto(
            r.Id,
            r.Name,
            r.OutputItem?.Name ?? string.Empty,
            r.OutputQty,
            r.Unit,
            r.Ingredients.Count,
            r.IsActive)).ToList();
    }

    public async Task<RecipeDetailDto?> GetRecipeByIdAsync(
        Guid id, CancellationToken ct = default)
    {
        var recipe = await _repo.GetRecipeByIdAsync(id, ct);
        return recipe is null ? null : ToRecipeDetail(recipe);
    }

    public async Task<(RecipeDetailDto? Recipe, string? Error, int? StatusCode)> CreateRecipeAsync(
        RecipeCreateDto dto, Guid tenantId, CancellationToken ct = default)
    {
        if (dto.Ingredients is null || dto.Ingredients.Count == 0)
            return (null, "Recipe must have at least one ingredient.", 400);

        var recipe = new Recipe
        {
            TenantId     = tenantId,
            Name         = dto.Name,
            OutputItemId = dto.OutputItemId,
            OutputQty    = dto.OutputQty,
            Unit         = dto.Unit,
            Notes        = dto.Notes,
            IsActive     = true,
        };

        await _repo.AddRecipeAsync(recipe, ct);

        foreach (var ing in dto.Ingredients)
        {
            var ingredient = new RecipeIngredient
            {
                RecipeId = recipe.Id,
                ItemId   = ing.ItemId,
                Qty      = ing.Qty,
                Unit     = ing.Unit,
            };
            await _repo.AddIngredientAsync(ingredient, ct);
        }

        await _repo.SaveChangesAsync(ct);

        var loaded = await _repo.GetRecipeByIdAsync(recipe.Id, ct);
        return (ToRecipeDetail(loaded!), null, null);
    }

    public async Task<(RecipeDetailDto? Recipe, string? Error, int? StatusCode)> UpdateRecipeAsync(
        Guid id, RecipeUpdateDto dto, CancellationToken ct = default)
    {
        var recipe = await _repo.GetRecipeByIdAsync(id, ct);
        if (recipe is null) return (null, "Recipe not found.", 404);

        if (dto.Name is not null) recipe.Name = dto.Name;
        if (dto.Notes is not null) recipe.Notes = dto.Notes;
        if (dto.IsActive.HasValue) recipe.IsActive = dto.IsActive.Value;
        recipe.UpdatedAt = DateTimeOffset.UtcNow;

        _repo.UpdateRecipe(recipe);
        await _repo.SaveChangesAsync(ct);

        return (ToRecipeDetail(recipe), null, null);
    }

    public async Task<(RecipeDetailDto? Recipe, string? Error, int? StatusCode)> ReplaceIngredientsAsync(
        Guid recipeId, List<RecipeIngredientCreateDto> ingredients, CancellationToken ct = default)
    {
        if (ingredients is null || ingredients.Count == 0)
            return (null, "Recipe must have at least one ingredient.", 400);

        var recipe = await _repo.GetRecipeByIdAsync(recipeId, ct);
        if (recipe is null) return (null, "Recipe not found.", 404);

        // Remove existing ingredients and replace with new list
        _repo.RemoveIngredients(recipe.Ingredients.ToList());

        foreach (var ing in ingredients)
        {
            var ingredient = new RecipeIngredient
            {
                RecipeId = recipeId,
                ItemId   = ing.ItemId,
                Qty      = ing.Qty,
                Unit     = ing.Unit,
            };
            await _repo.AddIngredientAsync(ingredient, ct);
        }

        recipe.UpdatedAt = DateTimeOffset.UtcNow;
        _repo.UpdateRecipe(recipe);

        await _repo.SaveChangesAsync(ct);

        var loaded = await _repo.GetRecipeByIdAsync(recipeId, ct);
        return (ToRecipeDetail(loaded!), null, null);
    }

    public async Task<(bool Ok, string? Error, int? StatusCode)> DeactivateRecipeAsync(
        Guid id, CancellationToken ct = default)
    {
        var recipe = await _repo.GetRecipeByIdAsync(id, ct);
        if (recipe is null) return (false, "Recipe not found.", 404);

        var hasActive = await _repo.HasActiveOrdersForRecipeAsync(id, ct);
        if (hasActive)
            return (false, "Cannot deactivate recipe with active production orders.", 409);

        recipe.IsActive  = false;
        recipe.UpdatedAt = DateTimeOffset.UtcNow;
        _repo.UpdateRecipe(recipe);
        await _repo.SaveChangesAsync(ct);

        return (true, null, null);
    }

    // ── Production Orders ─────────────────────────────────────────────────────

    public async Task<List<ProductionOrderListItemDto>> GetOrdersAsync(
        string? status, Guid? recipeId, Guid? locationId, CancellationToken ct = default)
    {
        var orders = await _repo.GetOrdersAsync(status, recipeId, locationId, ct);
        return orders.Select(o => new ProductionOrderListItemDto(
            o.Id,
            o.Recipe?.Name ?? string.Empty,
            o.Location?.Name ?? string.Empty,
            o.PlannedQty,
            o.Status.ToString(),
            o.CreatedAt,
            o.CompletedAt)).ToList();
    }

    public async Task<ProductionOrderDetailDto?> GetOrderByIdAsync(
        Guid id, CancellationToken ct = default)
    {
        var order = await _repo.GetOrderByIdAsync(id, ct);
        return order is null ? null : ToOrderDetail(order);
    }

    public async Task<(ProductionOrderDetailDto? Order, string? Error, int? StatusCode)> CreateOrderAsync(
        ProductionOrderCreateDto dto, Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        if (dto.PlannedQty <= 0)
            return (null, "PlannedQty must be greater than 0.", 400);

        var recipe = await _repo.GetRecipeByIdAsync(dto.RecipeId, ct);
        if (recipe is null)
            return (null, "Recipe not found.", 404);

        if (!recipe.IsActive)
            return (null, "Cannot create order for inactive recipe.", 409);

        var order = new ProductionOrder
        {
            TenantId   = tenantId,
            RecipeId   = dto.RecipeId,
            LocationId = dto.LocationId,
            PlannedQty = dto.PlannedQty,
            Status     = ProductionOrderStatus.Planned,
            Notes      = dto.Notes,
            CreatedBy  = userId,
        };

        await _repo.AddOrderAsync(order, ct);
        await _repo.SaveChangesAsync(ct);

        var loaded = await _repo.GetOrderByIdAsync(order.Id, ct);
        return (ToOrderDetail(loaded!), null, null);
    }

    public async Task<(ProductionOrderDetailDto? Order, string? Error, int? StatusCode)> UpdateOrderAsync(
        Guid id, ProductionOrderUpdateDto dto, CancellationToken ct = default)
    {
        var order = await _repo.GetOrderByIdAsync(id, ct);
        if (order is null) return (null, "Production order not found.", 404);

        if (dto.Status is not null)
        {
            if (!Enum.TryParse<ProductionOrderStatus>(dto.Status, true, out var newStatus))
                return (null, $"Invalid status value '{dto.Status}'.", 400);
            order.Status = newStatus;
        }

        if (dto.Notes is not null)
            order.Notes = dto.Notes;

        order.UpdatedAt = DateTimeOffset.UtcNow;
        _repo.UpdateOrder(order);
        await _repo.SaveChangesAsync(ct);

        return (ToOrderDetail(order), null, null);
    }

    public async Task<(ProductionOrderDetailDto? Order, string? Error, int? StatusCode)> StartOrderAsync(
        Guid id, CancellationToken ct = default)
    {
        var order = await _repo.GetOrderByIdAsync(id, ct);
        if (order is null) return (null, "Production order not found.", 404);

        if (order.Status != ProductionOrderStatus.Planned)
            return (null, $"Cannot start order in '{order.Status}' status. Order must be Planned.", 409);

        order.Status    = ProductionOrderStatus.InProgress;
        order.StartedAt = DateTimeOffset.UtcNow;
        order.UpdatedAt = DateTimeOffset.UtcNow;
        _repo.UpdateOrder(order);
        await _repo.SaveChangesAsync(ct);

        return (ToOrderDetail(order), null, null);
    }

    public async Task<(ProductionOrderDetailDto? Order, string? Error, int? StatusCode, Guid? InsufficientItemId, Guid? OutputStockBatchId)> CompleteOrderAsync(
        Guid id, Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var order = await _repo.GetOrderByIdAsync(id, ct);
        if (order is null)
            return (null, "Production order not found.", 404, null, null);

        // Allow completing from Planned or InProgress (not Done/Cancelled)
        if (order.Status != ProductionOrderStatus.Planned && order.Status != ProductionOrderStatus.InProgress)
            return (null, $"Cannot complete order in '{order.Status}' status. Order must be Planned or InProgress.", 409, null, null);

        var recipe = order.Recipe;
        if (recipe is null || recipe.Ingredients.Count == 0)
            return (null, "Recipe has no ingredients.", 422, null, null);

        // Output item must have a real shelf life configured — the produced batch's
        // ExpiryDate is derived from it (FEFO is sacred: no silent placeholder dates).
        var outputItem = await _repo.GetItemByIdAsync(recipe.OutputItemId, ct);
        if (outputItem is null)
            return (null, $"Output item '{recipe.OutputItemId}' not found.", 422, null, null);

        if (outputItem.ShelfLifeDays is not int shelfLifeDays || shelfLifeDays <= 0)
            return (null,
                $"Item '{outputItem.Name}' has no ShelfLifeDays configured — set a shelf life " +
                "before completing production so the produced batch gets a real expiry date.",
                422, null, null);

        // Scale factor: how many times the recipe output_qty fits into planned_qty
        decimal scale = order.PlannedQty / recipe.OutputQty;

        // Pre-validate ALL ingredients before consuming any (atomic guarantee)
        var consumptionPlan =
            new List<(RecipeIngredient Ingredient, Item Item, List<(ProductStock Batch, decimal Consume)> Batches)>();

        foreach (var ingredient in recipe.Ingredients)
        {
            var item = await _repo.GetItemByIdAsync(ingredient.ItemId, ct);
            if (item is null)
                return (null, $"Ingredient item '{ingredient.ItemId}' not found.", 422, ingredient.ItemId, null);

            decimal requiredQty = ingredient.Qty * scale;

            var batches = await _repo.GetFefoOrderedAsync(ingredient.ItemId, order.LocationId, ct);
            decimal available = batches.Sum(b => b.Quantity);

            if (available < requiredQty)
                return (null, $"Insufficient stock for ingredient '{item.Name}'", 422, item.Id, null);

            // Plan the FEFO consumption
            decimal remaining = requiredQty;
            var planned = new List<(ProductStock Batch, decimal Consume)>();
            foreach (var batch in batches)
            {
                if (remaining <= 0) break;
                var consume = Math.Min(remaining, batch.Quantity);
                planned.Add((batch, consume));
                remaining -= consume;
            }

            consumptionPlan.Add((ingredient, item, planned));
        }

        // All ingredients have sufficient stock — now apply write-downs
        foreach (var (ingredient, item, planned) in consumptionPlan)
        {
            foreach (var (batch, consume) in planned)
            {
                batch.Quantity -= consume;
                _repo.UpdateStock(batch);

                await _repo.AddConsumptionAsync(new ProductionOrderConsumption
                {
                    ProductionOrderId = order.Id,
                    ItemId            = item.Id,
                    ProductStockId    = batch.Id,
                    QtyConsumed       = consume,
                }, ct);

                await _repo.AddStockEventAsync(new StockEvent
                {
                    TenantId       = tenantId,
                    EventType      = "production_consumption",
                    ProductStockId = batch.Id,
                    ProductId      = item.Id,
                    StoreId        = order.LocationId,
                    QuantityDelta  = -consume,
                    Confidence     = 100,
                    Meta           = $"{{\"production_order_id\":\"{order.Id}\"}}",
                    PerformedBy    = userId,
                }, ct);
            }
        }

        // Create finished product stock batch — expiry derived from the shelf life
        // validated above (production date + ShelfLifeDays), never a placeholder.
        var actualExpiry = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(shelfLifeDays));

        var shortId = order.Id.ToString("N")[..8].ToUpper();
        var dateStr = DateTime.UtcNow.ToString("yyyyMMdd");
        var batchNumber = $"PROD-{shortId}-{dateStr}";

        var outputStock = new ProductStock
        {
            TenantId        = tenantId,
            ProductId       = recipe.OutputItemId,
            StoreId         = order.LocationId,
            Quantity        = order.PlannedQty,
            QuantityInitial = order.PlannedQty,
            ExpiryDate      = actualExpiry,
            BatchNumber     = batchNumber,
            SourceType      = "production",
            SourceId        = order.Id,
            AddedBy         = userId,
            Status          = "safe",
        };

        await _repo.AddStockAsync(outputStock, ct);

        await _repo.AddStockEventAsync(new StockEvent
        {
            TenantId       = tenantId,
            EventType      = "production_output",
            ProductStockId = outputStock.Id,
            ProductId      = recipe.OutputItemId,
            StoreId        = order.LocationId,
            QuantityDelta  = order.PlannedQty,
            Confidence     = 100,
            Meta           = $"{{\"production_order_id\":\"{order.Id}\",\"batch_number\":\"{batchNumber}\"}}",
            PerformedBy    = userId,
        }, ct);

        order.Status      = ProductionOrderStatus.Done;
        order.CompletedAt = DateTimeOffset.UtcNow;
        order.UpdatedAt   = DateTimeOffset.UtcNow;
        _repo.UpdateOrder(order);

        await _repo.SaveChangesAsync(ct);

        return (ToOrderDetail(order), null, null, null, outputStock.Id);
    }

    public async Task<(ProductionOrderDetailDto? Order, string? Error, int? StatusCode)> CancelOrderAsync(
        Guid id, CancellationToken ct = default)
    {
        var order = await _repo.GetOrderByIdAsync(id, ct);
        if (order is null) return (null, "Production order not found.", 404);

        if (order.Status == ProductionOrderStatus.Done)
            return (null, "Cannot cancel a completed production order.", 409);

        if (order.Status == ProductionOrderStatus.Cancelled)
            return (null, "Production order is already cancelled.", 409);

        order.Status    = ProductionOrderStatus.Cancelled;
        order.UpdatedAt = DateTimeOffset.UtcNow;
        _repo.UpdateOrder(order);
        await _repo.SaveChangesAsync(ct);

        return (ToOrderDetail(order), null, null);
    }

    // ── Mapping helpers ───────────────────────────────────────────────────────

    private static RecipeDetailDto ToRecipeDetail(Recipe r) =>
        new(r.Id,
            r.Name,
            r.OutputItemId,
            r.OutputItem?.Name ?? string.Empty,
            r.OutputQty,
            r.Unit,
            r.Notes,
            r.IsActive,
            r.Ingredients.Select(i => new RecipeIngredientDto(
                i.Id,
                i.ItemId,
                i.Item?.Name ?? string.Empty,
                i.Qty,
                i.Unit)).ToList());

    private static ProductionOrderDetailDto ToOrderDetail(ProductionOrder o) =>
        new(o.Id,
            o.RecipeId,
            o.Recipe?.Name ?? string.Empty,
            o.Location?.Name ?? string.Empty,
            o.PlannedQty,
            o.Status.ToString(),
            o.Notes,
            o.CreatedAt,
            o.StartedAt,
            o.CompletedAt,
            o.Consumptions.Select(c => new ProductionConsumptionDto(
                c.ItemId,
                c.Item?.Name ?? string.Empty,
                c.QtyConsumed,
                c.ProductStock?.BatchNumber,
                c.ConsumedAt)).ToList());
}
