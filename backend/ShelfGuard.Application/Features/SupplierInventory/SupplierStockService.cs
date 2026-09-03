using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.SupplierInventory.Dtos;
using ShelfGuard.Application.Features.Stock;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Exceptions;

namespace ShelfGuard.Application.Features.SupplierInventory;

/// <summary>
/// Supplier-portal expansion — Phase 2 (plan `1-partitioned-book.md`, decision D2).
///
/// Deliberate parallel of the retail <see cref="StockService"/> — FEFO consumption is
/// duplicated (D2), not extracted: <see cref="ProductStock"/> is keyed on a mandatory
/// <c>ProductId → items</c> FK and carries a RESTRICTIVE <c>store_scope</c> policy, neither
/// of which fits the supplier catalog (<see cref="SupplierItem"/>, nullable <c>ItemId</c>,
/// no <c>user_locations</c>). Batch <c>Status</c> is computed with the shared pure helper
/// <see cref="StockStatus"/>.
/// </summary>
public sealed class SupplierStockService : ISupplierStockService
{
    private readonly ISupplierStockRepository _repo;

    public SupplierStockService(ISupplierStockRepository repo) => _repo = repo;

    public async Task<PagedResult<SupplierStockDto>> GetStockAsync(
        Guid tenantId, Guid? warehouseId, Guid? supplierItemId,
        int page, int pageSize, CancellationToken ct = default)
    {
        var (items, total) = await _repo.GetPagedAsync(
            tenantId, warehouseId, supplierItemId, page, pageSize, ct);

        return new PagedResult<SupplierStockDto>
        {
            Items = items.Select(ToDto).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<(SupplierStockDto? Stock, string? Error)> AddBatchAsync(
        Guid tenantId, Guid warehouseId, Guid supplierItemId, DateOnly expiryDate,
        decimal quantity, string? batchNumber, Guid addedBy, CancellationToken ct = default)
    {
        if (quantity <= 0)
            return (null, "Кількість має бути більшою за 0.");

        if (expiryDate <= DateOnly.FromDateTime(DateTime.UtcNow))
            return (null, "Термін придатності має бути в майбутньому.");

        if (!await _repo.WarehouseExistsAsync(tenantId, warehouseId, ct))
            return (null, "Склад не знайдено.");

        if (!await _repo.SupplierItemExistsAsync(tenantId, supplierItemId, ct))
            return (null, "Товар не знайдено в каталозі постачальника.");

        var stock = new SupplierStock
        {
            TenantId = tenantId,
            SupplierItemId = supplierItemId,
            WarehouseId = warehouseId,
            ExpiryDate = expiryDate,
            Quantity = quantity,
            QuantityInitial = quantity,
            BatchNumber = batchNumber,
            Status = StockStatus.Compute(quantity, expiryDate, DateTime.UtcNow),
            SourceType = "manual",
            AddedBy = addedBy,
        };

        await _repo.AddAsync(stock, ct);
        await _repo.AddMovementAsync(new SupplierStockMovement
        {
            TenantId = tenantId,
            MovementType = "receipt",
            SupplierStockId = stock.Id,
            SupplierItemId = supplierItemId,
            ToWarehouseId = warehouseId,
            Quantity = quantity,
            QuantityBefore = 0,
            QuantityAfter = quantity,
            PerformedBy = addedBy,
        }, ct);

        await _repo.SaveChangesAsync(ct);

        var saved = await _repo.GetByIdAsync(tenantId, stock.Id, ct);
        return (saved is null ? null : ToDto(saved), null);
    }

    public async Task<(SupplierStockDto? Stock, string? Error)> AdjustAsync(
        Guid tenantId, Guid batchId, decimal newQuantity, string? reason,
        Guid performedBy, CancellationToken ct = default)
    {
        if (newQuantity < 0)
            return (null, "Кількість не може бути від'ємною.");

        var batch = await _repo.GetByIdAsync(tenantId, batchId, ct);
        if (batch is null)
            return (null, "Партію не знайдено.");

        var before = batch.Quantity;
        if (before == newQuantity)
            return (ToDto(batch), null);

        var delta = newQuantity - before;
        batch.Quantity = newQuantity;
        batch.Status = StockStatus.Compute(newQuantity, batch.ExpiryDate, batch.LastCheckedAt);
        batch.LastCheckedAt = DateTime.UtcNow;

        _repo.Update(batch);
        await _repo.AddMovementAsync(new SupplierStockMovement
        {
            TenantId = tenantId,
            MovementType = "adjust",
            SupplierStockId = batch.Id,
            SupplierItemId = batch.SupplierItemId,
            FromWarehouseId = delta < 0 ? batch.WarehouseId : null,
            ToWarehouseId = delta > 0 ? batch.WarehouseId : null,
            Quantity = Math.Abs(delta),
            QuantityBefore = before,
            QuantityAfter = newQuantity,
            PerformedBy = performedBy,
            Notes = reason,
        }, ct);

        try
        {
            await _repo.SaveChangesAsync(ct);
        }
        catch (ConcurrencyConflictException)
        {
            // SupplierStock.Quantity carries an xmin token (see AppDbContext) — another writer
            // (a concurrent adjust, or a Phase 3 shipment) touched this batch. Ask the caller
            // to retry with fresh data rather than silently overwriting.
            return (null, "Партію щойно змінила інша операція. Оновіть дані та спробуйте ще раз.");
        }

        var saved = await _repo.GetByIdAsync(tenantId, batch.Id, ct);
        return (saved is null ? null : ToDto(saved), null);
    }

    public async Task<SupplierFefoConsumeResult> FefoConsumeAsync(
        Guid tenantId, Guid supplierItemId, Guid warehouseId, decimal qty,
        string? referenceType, Guid? referenceId, Guid performedBy, CancellationToken ct = default)
    {
        if (qty <= 0)
            return new SupplierFefoConsumeResult(0, 0, []);

        // FEFO: nearest expiry first (mirror of StockService.FefoConsumeAsync).
        var batches = await _repo.GetFefoOrderedAsync(tenantId, supplierItemId, warehouseId, ct);

        var remaining = qty;
        var consumed = new List<SupplierBatchConsumed>();

        foreach (var batch in batches)
        {
            if (remaining <= 0) break;

            var take = Math.Min(batch.Quantity, remaining);
            if (take <= 0) continue;

            var before = batch.Quantity;
            batch.Quantity -= take;
            batch.Status = StockStatus.Compute(batch.Quantity, batch.ExpiryDate, batch.LastCheckedAt);
            remaining -= take;

            _repo.Update(batch);
            await _repo.AddMovementAsync(new SupplierStockMovement
            {
                TenantId = tenantId,
                MovementType = "ship",
                SupplierStockId = batch.Id,
                SupplierItemId = supplierItemId,
                FromWarehouseId = warehouseId,
                Quantity = take,
                QuantityBefore = before,
                QuantityAfter = batch.Quantity,
                ReferenceType = referenceType,
                ReferenceId = referenceId,
                PerformedBy = performedBy,
            }, ct);

            consumed.Add(new SupplierBatchConsumed(batch.Id, batch.BatchNumber, batch.ExpiryDate, take));
        }

        if (consumed.Count > 0)
            await _repo.SaveChangesAsync(ct);

        return new SupplierFefoConsumeResult(
            QuantityConsumed: qty - remaining,
            Shortfall: remaining,
            BatchesConsumed: consumed);
    }

    // ── mapping ────────────────────────────────────────────────────────────

    internal static SupplierStockDto ToDto(SupplierStock s) => new(
        s.Id,
        s.SupplierItemId,
        s.SupplierItem?.CustomName ?? s.SupplierItem?.Item?.Name ?? string.Empty,
        s.WarehouseId,
        s.Warehouse?.Name ?? string.Empty,
        s.ExpiryDate,
        StockStatus.DaysLeft(s.ExpiryDate),
        s.Quantity,
        s.QuantityInitial,
        s.BatchNumber,
        StockStatus.Compute(s.Quantity, s.ExpiryDate, s.LastCheckedAt),
        s.SourceType,
        s.AddedAt,
        s.LastCheckedAt);
}
