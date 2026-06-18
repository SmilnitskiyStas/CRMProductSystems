using ShelfGuard.Application.Features.Production;
using ShelfGuard.Application.Features.Production.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Production;

// ── In-memory fake repository ────────────────────────────────────────────────

file sealed class FakeProductionRepo : IProductionRepository
{
    public List<Recipe>                      Recipes       { get; } = [];
    public List<RecipeIngredient>            Ingredients   { get; } = [];
    public List<ProductionOrder>             Orders        { get; } = [];
    public List<ProductionOrderConsumption>  Consumptions  { get; } = [];
    public List<Item>                        Items         { get; } = [];
    public List<ProductStock>                Stock         { get; } = [];
    public List<StockEvent>                  StockEvents   { get; } = [];

    // ── Recipes ────────────────────────────────────────────────────────────

    public Task<List<Recipe>> GetRecipesAsync(bool includeInactive, CancellationToken ct = default) =>
        Task.FromResult(Recipes
            .Where(r => includeInactive || r.IsActive)
            .ToList());

    public Task<Recipe?> GetRecipeByIdAsync(Guid id, CancellationToken ct = default)
    {
        var recipe = Recipes.FirstOrDefault(r => r.Id == id);
        if (recipe is not null)
        {
            // Attach ingredients
            foreach (var ing in Ingredients.Where(i => i.RecipeId == recipe.Id))
            {
                if (!recipe.Ingredients.Contains(ing))
                    ((List<RecipeIngredient>)recipe.Ingredients).Add(ing);
                // Attach item navigation via reflection
                var item = Items.FirstOrDefault(x => x.Id == ing.ItemId);
                if (item is not null)
                    ing.GetType().GetProperty("Item")!.SetValue(ing, item);
            }
            // Attach output item
            var outputItem = Items.FirstOrDefault(x => x.Id == recipe.OutputItemId);
            if (outputItem is not null)
                recipe.GetType().GetProperty("OutputItem")!.SetValue(recipe, outputItem);
        }
        return Task.FromResult(recipe);
    }

    public Task<bool> HasActiveOrdersForRecipeAsync(Guid recipeId, CancellationToken ct = default) =>
        Task.FromResult(Orders.Any(o =>
            o.RecipeId == recipeId &&
            (o.Status == ProductionOrderStatus.Planned || o.Status == ProductionOrderStatus.InProgress)));

    public Task AddRecipeAsync(Recipe recipe, CancellationToken ct = default)
    {
        Recipes.Add(recipe);
        return Task.CompletedTask;
    }

    public void UpdateRecipe(Recipe recipe) { }

    // ── Recipe Ingredients ─────────────────────────────────────────────────

    public void RemoveIngredients(IEnumerable<RecipeIngredient> ingredients)
    {
        foreach (var ing in ingredients.ToList())
        {
            Ingredients.Remove(ing);
            ((List<RecipeIngredient>)ing.Recipe!.Ingredients).Remove(ing);
        }
    }

    public Task AddIngredientAsync(RecipeIngredient ingredient, CancellationToken ct = default)
    {
        Ingredients.Add(ingredient);
        return Task.CompletedTask;
    }

    // ── Production Orders ──────────────────────────────────────────────────

    public Task<List<ProductionOrder>> GetOrdersAsync(
        string? status, Guid? recipeId, Guid? locationId, CancellationToken ct = default) =>
        Task.FromResult(Orders.ToList());

    public Task<ProductionOrder?> GetOrderByIdAsync(Guid id, CancellationToken ct = default)
    {
        var order = Orders.FirstOrDefault(o => o.Id == id);
        if (order is not null)
        {
            // Attach recipe
            var recipe = Recipes.FirstOrDefault(r => r.Id == order.RecipeId);
            if (recipe is not null)
            {
                order.GetType().GetProperty("Recipe")!.SetValue(order, recipe);
                // Attach ingredients to recipe
                foreach (var ing in Ingredients.Where(i => i.RecipeId == recipe.Id))
                {
                    if (!recipe.Ingredients.Contains(ing))
                        ((List<RecipeIngredient>)recipe.Ingredients).Add(ing);
                    var item = Items.FirstOrDefault(x => x.Id == ing.ItemId);
                    if (item is not null)
                        ing.GetType().GetProperty("Item")!.SetValue(ing, item);
                }
            }
            // Attach consumptions
            foreach (var c in Consumptions.Where(c => c.ProductionOrderId == order.Id))
            {
                if (!order.Consumptions.Contains(c))
                    ((List<ProductionOrderConsumption>)order.Consumptions).Add(c);
            }
        }
        return Task.FromResult(order);
    }

    public Task AddOrderAsync(ProductionOrder order, CancellationToken ct = default)
    {
        Orders.Add(order);
        return Task.CompletedTask;
    }

    public void UpdateOrder(ProductionOrder order) { }

    // ── Consumptions ───────────────────────────────────────────────────────

    public Task AddConsumptionAsync(ProductionOrderConsumption consumption, CancellationToken ct = default)
    {
        Consumptions.Add(consumption);
        return Task.CompletedTask;
    }

    // ── Stock / Items ──────────────────────────────────────────────────────

    public Task<Item?> GetItemByIdAsync(Guid itemId, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(i => i.Id == itemId));

    public Task<List<ProductStock>> GetFefoOrderedAsync(
        Guid itemId, Guid locationId, CancellationToken ct = default) =>
        Task.FromResult(Stock
            .Where(s => s.ProductId == itemId && s.Quantity > 0)
            .OrderBy(s => s.ExpiryDate)
            .ToList());

    public void UpdateStock(ProductStock batch) { }

    public Task AddStockAsync(ProductStock stock, CancellationToken ct = default)
    {
        Stock.Add(stock);
        return Task.CompletedTask;
    }

    public Task AddStockEventAsync(StockEvent ev, CancellationToken ct = default)
    {
        StockEvents.Add(ev);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

// ── Test data builders ───────────────────────────────────────────────────────

file static class Build
{
    private static readonly Guid TenantId    = Guid.NewGuid();
    private static readonly Guid LocationId  = Guid.NewGuid();

    public static Item RawMaterial(string name = "Flour") =>
        new() { TenantId = TenantId, Name = name, ItemType = "raw_material" };

    public static Item FinishedProduct(string name = "Bread") =>
        new() { TenantId = TenantId, Name = name, ItemType = "product", ShelfLifeDays = 3 };

    public static Recipe Recipe(Guid outputItemId, string name = "Bread Recipe") =>
        new() { TenantId = TenantId, Name = name, OutputItemId = outputItemId, OutputQty = 1m, Unit = "шт", IsActive = true };

    public static RecipeIngredient Ingredient(Guid recipeId, Guid itemId, decimal qty = 1m) =>
        new() { RecipeId = recipeId, ItemId = itemId, Qty = qty, Unit = "kg" };

    public static ProductionOrder Order(
        Guid recipeId,
        ProductionOrderStatus status = ProductionOrderStatus.Planned,
        decimal plannedQty = 1m) =>
        new()
        {
            TenantId   = TenantId,
            RecipeId   = recipeId,
            LocationId = LocationId,
            PlannedQty = plannedQty,
            Status     = status,
        };

    public static ProductStock StockBatch(Guid itemId, decimal qty, int daysUntilExpiry = 30) =>
        new()
        {
            TenantId        = TenantId,
            ProductId       = itemId,
            StoreId         = LocationId,
            Quantity        = qty,
            QuantityInitial = qty,
            ExpiryDate      = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(daysUntilExpiry)),
            Status          = "safe",
        };
}

// ── Tests ────────────────────────────────────────────────────────────────────

public sealed class ProductionServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId   = Guid.NewGuid();

    private static ProductionService BuildSvc(IProductionRepository repo) => new(repo);

    // ── CompleteOrderAsync — wrong status (Done) → 409 ───────────────────────

    [Fact]
    public async Task CompleteOrderAsync_WrongStatus_Done_Returns409()
    {
        var repo        = new FakeProductionRepo();
        var svc         = BuildSvc(repo);
        var output      = Build.FinishedProduct();
        var recipe      = Build.Recipe(output.Id);
        var order       = Build.Order(recipe.Id, ProductionOrderStatus.Done);

        repo.Items.Add(output);
        repo.Recipes.Add(recipe);
        repo.Orders.Add(order);

        var (resultOrder, error, statusCode, _, _) =
            await svc.CompleteOrderAsync(order.Id, TenantId, UserId);

        Assert.Null(resultOrder);
        Assert.Equal(409, statusCode);
        Assert.NotNull(error);
    }

    // ── CompleteOrderAsync — insufficient stock → 422, no partial writes ─────

    [Fact]
    public async Task CompleteOrderAsync_InsufficientStock_Returns422_NoPartialWrites()
    {
        var repo       = new FakeProductionRepo();
        var svc        = BuildSvc(repo);
        var rawMat     = Build.RawMaterial("Flour");
        var output     = Build.FinishedProduct("Bread");
        var recipe     = Build.Recipe(output.Id);
        var ingredient = Build.Ingredient(recipe.Id, rawMat.Id, qty: 5m);
        var order      = Build.Order(recipe.Id, ProductionOrderStatus.Planned);

        repo.Items.Add(rawMat);
        repo.Items.Add(output);
        repo.Recipes.Add(recipe);
        repo.Ingredients.Add(ingredient);
        repo.Orders.Add(order);

        // Only 2 units available; need 5
        repo.Stock.Add(Build.StockBatch(rawMat.Id, qty: 2m));

        var (resultOrder, error, statusCode, insufficientItemId, _) =
            await svc.CompleteOrderAsync(order.Id, TenantId, UserId);

        Assert.Null(resultOrder);
        Assert.Equal(422, statusCode);
        Assert.NotNull(error);
        Assert.Contains("Flour", error);
        Assert.Equal(rawMat.Id, insufficientItemId);

        // Stock must NOT have been decremented
        Assert.Equal(2m, repo.Stock.First().Quantity);
        // No consumptions or stock events created
        Assert.Empty(repo.Consumptions);
        Assert.Empty(repo.StockEvents);
    }

    // ── CompleteOrderAsync — happy path (Planned status) ─────────────────────

    [Fact]
    public async Task CompleteOrderAsync_HappyPath_FromPlanned_CreatesConsumptionsAndOutputBatch()
    {
        var repo       = new FakeProductionRepo();
        var svc        = BuildSvc(repo);
        var rawMat     = Build.RawMaterial("Flour");
        var output     = Build.FinishedProduct("Bread");
        var recipe     = Build.Recipe(output.Id);
        var ingredient = Build.Ingredient(recipe.Id, rawMat.Id, qty: 1m);
        // plannedQty = 2, recipe outputQty = 1 → scale = 2 → need 2 kg flour
        var order      = Build.Order(recipe.Id, ProductionOrderStatus.Planned, plannedQty: 2m);

        repo.Items.Add(rawMat);
        repo.Items.Add(output);
        repo.Recipes.Add(recipe);
        repo.Ingredients.Add(ingredient);
        repo.Orders.Add(order);

        // Two FEFO batches: nearest expiry first
        var batch1 = Build.StockBatch(rawMat.Id, qty: 1.5m, daysUntilExpiry: 5);
        var batch2 = Build.StockBatch(rawMat.Id, qty: 3m,   daysUntilExpiry: 30);
        repo.Stock.Add(batch1);
        repo.Stock.Add(batch2);

        var (resultOrder, error, statusCode, insufficientItemId, outputStockBatchId) =
            await svc.CompleteOrderAsync(order.Id, TenantId, UserId);

        Assert.Null(error);
        Assert.Null(insufficientItemId);
        Assert.Equal(ProductionOrderStatus.Done, order.Status);
        Assert.NotNull(order.CompletedAt);

        // FEFO: batch1 (1.5) fully consumed, batch2 (0.5) partially consumed
        Assert.Equal(0m, batch1.Quantity);
        Assert.Equal(2.5m, batch2.Quantity);

        // Consumptions created: one per batch
        Assert.Equal(2, repo.Consumptions.Count);

        // Stock events: 2 consumption + 1 output
        var consumptionEvents = repo.StockEvents.Where(e => e.EventType == "production_consumption").ToList();
        var outputEvents      = repo.StockEvents.Where(e => e.EventType == "production_output").ToList();
        Assert.Equal(2, consumptionEvents.Count);
        Assert.Single(outputEvents);

        // Finished product stock batch created with correct qty
        var outputBatch = repo.Stock.FirstOrDefault(s => s.ProductId == output.Id);
        Assert.NotNull(outputBatch);
        Assert.Equal(2m, outputBatch.Quantity);
        Assert.Equal(outputBatch.Id, outputStockBatchId);
        Assert.StartsWith("PROD-", outputBatch.BatchNumber);
    }

    // ── CompleteOrderAsync — happy path (InProgress status) ──────────────────

    [Fact]
    public async Task CompleteOrderAsync_HappyPath_FromInProgress_Succeeds()
    {
        var repo       = new FakeProductionRepo();
        var svc        = BuildSvc(repo);
        var rawMat     = Build.RawMaterial("Sugar");
        var output     = Build.FinishedProduct("Cake");
        var recipe     = Build.Recipe(output.Id, "Cake Recipe");
        var ingredient = Build.Ingredient(recipe.Id, rawMat.Id, qty: 2m);
        var order      = Build.Order(recipe.Id, ProductionOrderStatus.InProgress);

        repo.Items.Add(rawMat);
        repo.Items.Add(output);
        repo.Recipes.Add(recipe);
        repo.Ingredients.Add(ingredient);
        repo.Orders.Add(order);
        repo.Stock.Add(Build.StockBatch(rawMat.Id, qty: 10m));

        var (resultOrder, error, statusCode, _, outputStockBatchId) =
            await svc.CompleteOrderAsync(order.Id, TenantId, UserId);

        Assert.Null(error);
        Assert.NotNull(resultOrder);
        Assert.Equal(ProductionOrderStatus.Done, order.Status);
        Assert.NotNull(outputStockBatchId);
    }

    // ── CancelOrderAsync — done order → 409 ──────────────────────────────────

    [Fact]
    public async Task CancelOrderAsync_DoneOrder_Returns409()
    {
        var repo   = new FakeProductionRepo();
        var svc    = BuildSvc(repo);
        var output = Build.FinishedProduct();
        var recipe = Build.Recipe(output.Id);
        var order  = Build.Order(recipe.Id, ProductionOrderStatus.Done);

        repo.Items.Add(output);
        repo.Recipes.Add(recipe);
        repo.Orders.Add(order);

        var (resultOrder, error, statusCode) = await svc.CancelOrderAsync(order.Id);

        Assert.Null(resultOrder);
        Assert.Equal(409, statusCode);
        Assert.NotNull(error);
    }

    // ── CancelOrderAsync — planned order → succeeds ───────────────────────────

    [Fact]
    public async Task CancelOrderAsync_PlannedOrder_Succeeds()
    {
        var repo   = new FakeProductionRepo();
        var svc    = BuildSvc(repo);
        var output = Build.FinishedProduct();
        var recipe = Build.Recipe(output.Id);
        var order  = Build.Order(recipe.Id, ProductionOrderStatus.Planned);

        repo.Items.Add(output);
        repo.Recipes.Add(recipe);
        repo.Orders.Add(order);

        var (resultOrder, error, statusCode) = await svc.CancelOrderAsync(order.Id);

        Assert.Null(error);
        Assert.Equal(ProductionOrderStatus.Cancelled, order.Status);
    }

    // ── CreateRecipeAsync — empty ingredients → 400 ───────────────────────────

    [Fact]
    public async Task CreateRecipeAsync_EmptyIngredients_Returns400()
    {
        var repo = new FakeProductionRepo();
        var svc  = BuildSvc(repo);

        var dto = new RecipeCreateDto(
            Name:          "Empty Recipe",
            OutputItemId:  Guid.NewGuid(),
            OutputQty:     1m,
            Unit:          "шт",
            Notes:         null,
            Ingredients:   new List<RecipeIngredientCreateDto>());

        var (recipe, error, statusCode) = await svc.CreateRecipeAsync(dto, TenantId);

        Assert.Null(recipe);
        Assert.Equal(400, statusCode);
        Assert.NotNull(error);
    }

    // ── DeactivateRecipeAsync — has active orders → 409 ──────────────────────

    [Fact]
    public async Task DeactivateRecipeAsync_HasActiveOrders_Returns409()
    {
        var repo   = new FakeProductionRepo();
        var svc    = BuildSvc(repo);
        var output = Build.FinishedProduct();
        var recipe = Build.Recipe(output.Id);
        var order  = Build.Order(recipe.Id, ProductionOrderStatus.Planned);

        repo.Items.Add(output);
        repo.Recipes.Add(recipe);
        repo.Orders.Add(order);

        var (ok, error, statusCode) = await svc.DeactivateRecipeAsync(recipe.Id);

        Assert.False(ok);
        Assert.Equal(409, statusCode);
        Assert.NotNull(error);
    }

    // ── DeactivateRecipeAsync — no active orders → succeeds ──────────────────

    [Fact]
    public async Task DeactivateRecipeAsync_NoActiveOrders_SetsInactive()
    {
        var repo   = new FakeProductionRepo();
        var svc    = BuildSvc(repo);
        var output = Build.FinishedProduct();
        var recipe = Build.Recipe(output.Id);

        repo.Items.Add(output);
        repo.Recipes.Add(recipe);

        var (ok, error, statusCode) = await svc.DeactivateRecipeAsync(recipe.Id);

        Assert.True(ok);
        Assert.Null(error);
        Assert.False(recipe.IsActive);
    }
}
