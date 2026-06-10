using ShelfGuard.Application.Features.Stock.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Stock;

public sealed class StockService : IStockService
{
    private readonly IStockRepository _repo;

    public StockService(IStockRepository repo) => _repo = repo;

    public async Task<List<ProductStockDto>> GetAllAsync(
        Guid? storeId, string? status, Guid? zoneId, Guid? productId,
        CancellationToken ct = default)
    {
        var batches = await _repo.GetAllAsync(storeId, status, zoneId, productId, ct);
        return batches.Select(ToDto).ToList();
    }

    public async Task<ProductStockDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var batch = await _repo.GetByIdAsync(id, ct);
        return batch is null ? null : ToDto(batch);
    }

    public async Task<List<ProductStockDto>> GetExpiringAsync(Guid? storeId, int days, CancellationToken ct = default)
    {
        var batches = await _repo.GetExpiringAsync(storeId, days, ct);
        return batches.Select(ToDto).ToList();
    }

    public async Task<List<ProductStockDto>> GetExpiredAsync(Guid? storeId, CancellationToken ct = default)
    {
        var batches = await _repo.GetExpiredAsync(storeId, ct);
        return batches.Select(ToDto).ToList();
    }

    public async Task<List<ProductStockDto>> GetNeedsCheckAsync(Guid? storeId, CancellationToken ct = default)
    {
        var batches = await _repo.GetNeedsCheckAsync(storeId, ct);
        return batches.Select(ToDto).ToList();
    }

    public async Task<StockSummaryDto> GetSummaryAsync(Guid? storeId, CancellationToken ct = default)
    {
        var counts = await _repo.GetStatusCountsAsync(storeId, ct);
        counts.TryGetValue("safe", out var safe);
        counts.TryGetValue("warning", out var warning);
        counts.TryGetValue("critical", out var critical);
        counts.TryGetValue("expired", out var expired);
        counts.TryGetValue("needs_verification", out var needsVerification);
        return new StockSummaryDto(safe, warning, critical, expired, needsVerification,
            safe + warning + critical + expired + needsVerification);
    }

    public async Task<List<SuggestionDto>> GetSuggestionsAsync(Guid? storeId, CancellationToken ct = default)
    {
        var batches = await _repo.GetActionRequiredAsync(storeId, ct);
        var productionStores = await _repo.GetProductionStoresAsync(ct);

        var suggestions = new List<SuggestionDto>();

        foreach (var batch in batches)
        {
            var actions = await BuildActionsAsync(batch, productionStores, ct);
            suggestions.Add(ToSuggestionDto(batch, actions));
        }

        return suggestions;
    }

    public async Task<(ProductStockDto? Stock, string? Error)> CreateAsync(
        Guid tenantId, Guid performedBy, CreateStockRequest request, CancellationToken ct = default)
    {
        if (request.Quantity <= 0)
            return (null, "Quantity must be greater than 0.");

        if (request.ExpiryDate <= DateOnly.FromDateTime(DateTime.UtcNow))
            return (null, "Expiry date must be in the future.");

        var status = StockStatus.Compute(request.Quantity, request.ExpiryDate, DateTime.UtcNow);

        var stock = new ProductStock
        {
            TenantId = tenantId,
            ProductId = request.ProductId,
            StoreId = request.StoreId,
            ZoneId = request.ZoneId,
            ShelfNumber = request.ShelfNumber,
            BatchNumber = request.BatchNumber,
            Quantity = request.Quantity,
            QuantityInitial = request.Quantity,
            ExpiryDate = request.ExpiryDate,
            Status = status,
            SourceType = request.SourceType ?? "receipt",
            SourceId = request.SourceId,
            AddedBy = performedBy,
        };

        await _repo.AddAsync(stock, ct);

        var movement = new StockMovement
        {
            TenantId = tenantId,
            MovementType = "receipt",
            ProductStockId = stock.Id,
            ProductId = stock.ProductId,
            ToStoreId = stock.StoreId,
            ToZoneId = stock.ZoneId,
            Quantity = stock.Quantity,
            QuantityBefore = 0,
            QuantityAfter = stock.Quantity,
            PerformedBy = performedBy,
        };

        await _repo.AddMovementAsync(movement, ct);
        await _repo.SaveChangesAsync(ct);

        var saved = await _repo.GetByIdAsync(stock.Id, ct);
        return (saved is null ? null : ToDto(saved), null);
    }

    public async Task<(ProductStockDto? Stock, string? Error)> UpdateAsync(
        Guid id, UpdateStockRequest request, CancellationToken ct = default)
    {
        var stock = await _repo.GetByIdAsync(id, ct);
        if (stock is null)
            return (null, "Stock batch not found.");

        if (request.Quantity < 0)
            return (null, "Quantity cannot be negative.");

        stock.ZoneId = request.ZoneId;
        stock.ShelfNumber = request.ShelfNumber;
        stock.BatchNumber = request.BatchNumber;
        stock.Quantity = request.Quantity;
        stock.Status = StockStatus.Compute(request.Quantity, request.ExpiryDate, stock.LastCheckedAt);

        _repo.Update(stock);
        await _repo.SaveChangesAsync(ct);

        return (ToDto(stock), null);
    }

    public async Task<(ProductStockDto? Stock, string? Error)> VerifyAsync(
        Guid id, Guid performedBy, CancellationToken ct = default)
    {
        var stock = await _repo.GetByIdAsync(id, ct);
        if (stock is null)
            return (null, "Stock batch not found.");

        stock.LastCheckedAt = DateTime.UtcNow;
        stock.Status = StockStatus.Compute(stock.Quantity, stock.ExpiryDate, stock.LastCheckedAt);

        _repo.Update(stock);
        await _repo.SaveChangesAsync(ct);

        return (ToDto(stock), null);
    }

    public async Task<FefoConsumeResult> FefoConsumeAsync(
        Guid tenantId, Guid performedBy, FefoConsumeRequest request, CancellationToken ct = default)
    {
        if (request.Quantity <= 0)
            return new FefoConsumeResult(false, 0, 0, [], "Quantity must be greater than 0.");

        // FEFO: batches ordered by expiry_date ASC (nearest first)
        var batches = await _repo.GetFefoOrderedAsync(request.ProductId, request.StoreId, ct);

        if (batches.Count == 0)
            return new FefoConsumeResult(false, 0, request.Quantity, [], "No stock available for this product in the specified store.");

        var remaining = request.Quantity;
        var consumed = new List<BatchConsumed>();

        foreach (var batch in batches)
        {
            if (remaining <= 0) break;

            var take = Math.Min(batch.Quantity, remaining);
            var before = batch.Quantity;

            batch.Quantity -= take;
            batch.Status = StockStatus.Compute(batch.Quantity, batch.ExpiryDate, batch.LastCheckedAt);
            remaining -= take;

            var movement = new StockMovement
            {
                TenantId = tenantId,
                MovementType = "write_off",
                ProductStockId = batch.Id,
                ProductId = batch.ProductId,
                FromStoreId = batch.StoreId,
                FromZoneId = batch.ZoneId,
                Quantity = take,
                QuantityBefore = before,
                QuantityAfter = batch.Quantity,
                PerformedBy = performedBy,
                Notes = request.Notes,
            };

            _repo.Update(batch);
            await _repo.AddMovementAsync(movement, ct);

            consumed.Add(new BatchConsumed(batch.Id, batch.BatchNumber, batch.ExpiryDate, take));
        }

        await _repo.SaveChangesAsync(ct);

        var totalConsumed = request.Quantity - remaining;
        return new FefoConsumeResult(
            Success: remaining == 0,
            QuantityConsumed: totalConsumed,
            QuantityShortfall: remaining,
            BatchesConsumed: consumed,
            Error: remaining > 0 ? $"Insufficient stock. Short by {remaining}." : null
        );
    }

    // ── suggestions ────────────────────────────────────────────────────────

    private async Task<List<StockAction>> BuildActionsAsync(
        ProductStock batch, List<Store> productionStores, CancellationToken ct)
    {
        var actions = new List<StockAction>();

        // 1. Transfer to store with deficit
        var deficitStores = await _repo.GetDeficitStocksAsync(batch.ProductId, batch.StoreId, ct);
        var firstDeficit = deficitStores.FirstOrDefault();
        if (firstDeficit?.Store is not null)
        {
            actions.Add(new StockAction(
                "transfer",
                $"Перемістити в {firstDeficit.Store.Name}",
                firstDeficit.Store.Name,
                firstDeficit.StoreId
            ));
        }

        // 2. Transfer to production/culinary in same network
        var productionStore = productionStores.FirstOrDefault();
        if (productionStore is not null && productionStore.Id != batch.StoreId)
        {
            actions.Add(new StockAction(
                "production",
                $"Передати в {productionStore.Name}",
                productionStore.Name,
                productionStore.Id
            ));
        }

        // 3. Discount if quantity > min_stock * 1.5
        var minStock = batch.Product?.MinStock ?? 0;
        if (minStock > 0 && batch.Quantity > minStock * 1.5m)
        {
            var daysLeft = StockStatus.DaysLeft(batch.ExpiryDate);
            var discountPct = daysLeft <= 3 ? 50 : daysLeft <= 6 ? 30 : 20;
            actions.Add(new StockAction(
                "discount",
                $"Встановити знижку {discountPct}%",
                null, null
            ));
        }

        // 4. Return to supplier if return_policy exists
        if (batch.Product?.DefaultSupplier?.ReturnPolicy == true)
        {
            actions.Add(new StockAction(
                "return",
                "Повернути постачальнику",
                null, null
            ));
        }

        // 5. Write-off as fallback
        if (actions.Count == 0)
        {
            actions.Add(new StockAction("write_off", "Списати", null, null));
        }

        return actions;
    }

    // ── mapping ────────────────────────────────────────────────────────────

    private static ProductStockDto ToDto(ProductStock s) => new(
        s.Id,
        s.ProductId,
        s.Product?.Name ?? string.Empty,
        s.Product?.Barcode,
        s.StoreId,
        s.Store?.Name ?? string.Empty,
        s.ZoneId,
        s.Zone?.Name,
        s.ShelfNumber,
        s.BatchNumber,
        s.Quantity,
        s.QuantityInitial,
        s.ExpiryDate,
        StockStatus.DaysLeft(s.ExpiryDate),
        StockStatus.Compute(s.Quantity, s.ExpiryDate, s.LastCheckedAt),
        s.SourceType,
        s.AddedAt,
        s.LastCheckedAt
    );

    private static SuggestionDto ToSuggestionDto(ProductStock s, List<StockAction> actions) => new(
        s.Id,
        s.ProductId,
        s.Product?.Name ?? string.Empty,
        s.StoreId,
        s.Store?.Name ?? string.Empty,
        s.BatchNumber,
        s.ExpiryDate,
        StockStatus.DaysLeft(s.ExpiryDate),
        s.Quantity,
        StockStatus.Compute(s.Quantity, s.ExpiryDate, s.LastCheckedAt),
        actions
    );
}
