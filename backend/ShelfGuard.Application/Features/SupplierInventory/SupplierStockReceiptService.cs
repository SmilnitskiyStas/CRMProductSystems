using ShelfGuard.Application.Features.SupplierInventory.Dtos;
using ShelfGuard.Application.Features.Stock;
using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Application.Features.SupplierInventory;

/// <summary>
/// Supplier-portal expansion — Phase 2 (plan `1-partitioned-book.md`, decision D3).
/// Mirrors <c>ReceiptService</c>'s draft → add lines → finalize shape. The finalize gate
/// requires every line to carry an <c>ExpiryDate</c> and a positive <c>Quantity</c>; each
/// line then produces exactly one <see cref="SupplierStock"/> batch and one
/// <see cref="SupplierStockMovement"/> (<c>receipt</c>).
/// </summary>
public sealed class SupplierStockReceiptService : ISupplierStockReceiptService
{
    private const string StatusDraft = "draft";
    private const string StatusReceived = "received";
    private const string StatusCancelled = "cancelled";

    private readonly ISupplierStockReceiptRepository _repo;
    private readonly ISupplierStockRepository _stockRepo;

    public SupplierStockReceiptService(
        ISupplierStockReceiptRepository repo, ISupplierStockRepository stockRepo)
    {
        _repo = repo;
        _stockRepo = stockRepo;
    }

    public async Task<(SupplierStockReceiptDto? Receipt, string? Error)> CreateDraftAsync(
        Guid tenantId, Guid warehouseId, string? reference, string? notes,
        Guid createdBy, CancellationToken ct = default)
    {
        if (!await _stockRepo.WarehouseExistsAsync(tenantId, warehouseId, ct))
            return (null, "Склад не знайдено.");

        var receipt = new SupplierStockReceipt
        {
            TenantId = tenantId,
            WarehouseId = warehouseId,
            Status = StatusDraft,
            Reference = reference,
            Notes = notes,
            CreatedBy = createdBy,
        };

        await _repo.AddAsync(receipt, ct);
        await _repo.SaveChangesAsync(ct);

        var saved = await _repo.GetByIdAsync(tenantId, receipt.Id, ct);
        return (saved is null ? null : ToDto(saved), null);
    }

    public async Task<SupplierStockReceiptDto?> GetAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        var receipt = await _repo.GetByIdAsync(tenantId, id, ct);
        return receipt is null ? null : ToDto(receipt);
    }

    public async Task<List<SupplierStockReceiptDto>> ListAsync(
        Guid tenantId, Guid? warehouseId, string? status, CancellationToken ct = default)
    {
        var receipts = await _repo.ListAsync(tenantId, warehouseId, status, ct);
        return receipts.Select(ToDto).ToList();
    }

    public async Task<(SupplierStockReceiptDto? Receipt, string? Error)> UpdateAsync(
        Guid tenantId, Guid id, UpdateSupplierReceiptRequest request, CancellationToken ct = default)
    {
        var receipt = await _repo.GetByIdAsync(tenantId, id, ct);
        if (receipt is null)
            return (null, "Прийом не знайдено.");
        if (receipt.Status != StatusDraft)
            return (null, "Змінювати можна лише чернетку прийому.");

        if (receipt.WarehouseId != request.WarehouseId
            && !await _stockRepo.WarehouseExistsAsync(tenantId, request.WarehouseId, ct))
            return (null, "Склад не знайдено.");

        receipt.WarehouseId = request.WarehouseId;
        receipt.Reference = request.Reference;
        receipt.Notes = request.Notes;

        _repo.Update(receipt);
        await _repo.SaveChangesAsync(ct);

        var saved = await _repo.GetByIdAsync(tenantId, id, ct);
        return (saved is null ? null : ToDto(saved), null);
    }

    public async Task<(SupplierStockReceiptDto? Receipt, string? Error)> AddLineAsync(
        Guid tenantId, Guid receiptId, AddSupplierReceiptLineRequest request, CancellationToken ct = default)
    {
        var receipt = await _repo.GetByIdAsync(tenantId, receiptId, ct);
        if (receipt is null)
            return (null, "Прийом не знайдено.");
        if (receipt.Status != StatusDraft)
            return (null, "Додавати позиції можна лише до чернетки прийому.");
        if (request.Quantity <= 0)
            return (null, "Кількість має бути більшою за 0.");
        if (!await _stockRepo.SupplierItemExistsAsync(tenantId, request.SupplierItemId, ct))
            return (null, "Товар не знайдено в каталозі постачальника.");

        // Explicit AddItem (DbSet.Add), NOT receipt.Items.Add + _repo.Update(receipt): the item's
        // Id already carries a client-side Guid.NewGuid() default while the column is
        // store-generated, so a graph walk (Update / DetectChanges on the nav) infers Modified
        // from the non-empty key → "UPDATE ... WHERE Id=<new guid>" affects 0 rows →
        // DbUpdateConcurrencyException → 500 on every add-line (found in prod, TASK-697).
        _repo.AddItem(new SupplierStockReceiptItem
        {
            ReceiptId = receipt.Id,
            TenantId = tenantId,
            SupplierItemId = request.SupplierItemId,
            ExpiryDate = request.ExpiryDate,
            Quantity = request.Quantity,
            BatchNumber = request.BatchNumber,
            UnitCost = request.UnitCost,
            Notes = request.Notes,
        });

        await _repo.SaveChangesAsync(ct);

        var saved = await _repo.GetByIdAsync(tenantId, receiptId, ct);
        return (saved is null ? null : ToDto(saved), null);
    }

    public async Task<(SupplierStockReceiptDto? Receipt, string? Error)> RemoveLineAsync(
        Guid tenantId, Guid receiptId, Guid lineId, CancellationToken ct = default)
    {
        var receipt = await _repo.GetByIdAsync(tenantId, receiptId, ct);
        if (receipt is null)
            return (null, "Прийом не знайдено.");
        if (receipt.Status != StatusDraft)
            return (null, "Видаляти позиції можна лише з чернетки прийому.");

        var line = receipt.Items.FirstOrDefault(i => i.Id == lineId);
        if (line is null)
            return (null, "Позицію не знайдено.");

        receipt.Items.Remove(line);
        _repo.RemoveItem(line);
        await _repo.SaveChangesAsync(ct);

        var saved = await _repo.GetByIdAsync(tenantId, receiptId, ct);
        return (saved is null ? null : ToDto(saved), null);
    }

    public async Task<(SupplierStockReceiptDto? Receipt, string? Error)> ReceiveAsync(
        Guid tenantId, Guid receiptId, Guid receivedBy, CancellationToken ct = default)
    {
        var receipt = await _repo.GetByIdAsync(tenantId, receiptId, ct);
        if (receipt is null)
            return (null, "Прийом не знайдено.");
        if (receipt.Status == StatusReceived)
            return (null, "Прийом уже завершено.");
        if (receipt.Status == StatusCancelled)
            return (null, "Скасований прийом завершити не можна.");
        if (receipt.Items.Count == 0)
            return (null, "Прийом має містити хоча б одну позицію.");

        var invalid = receipt.Items
            .Where(i => i.ExpiryDate is null || i.Quantity <= 0)
            .ToList();
        if (invalid.Count > 0)
            return (null, $"{invalid.Count} позиц. без терміну придатності або з нульовою кількістю — заповніть усі позиції перед завершенням.");

        foreach (var line in receipt.Items)
        {
            var qty = line.Quantity;
            var expiry = line.ExpiryDate!.Value;

            var stock = new SupplierStock
            {
                TenantId = tenantId,
                SupplierItemId = line.SupplierItemId,
                WarehouseId = receipt.WarehouseId,
                ExpiryDate = expiry,
                Quantity = qty,
                QuantityInitial = qty,
                BatchNumber = line.BatchNumber,
                Status = StockStatus.Compute(qty, expiry, DateTime.UtcNow),
                SourceType = "supplier_receipt",
                SourceId = receipt.Id,
                AddedBy = receivedBy,
            };

            await _repo.AddStockAsync(stock, ct);
            await _repo.AddMovementAsync(new SupplierStockMovement
            {
                TenantId = tenantId,
                MovementType = "receipt",
                SupplierStockId = stock.Id,
                SupplierItemId = line.SupplierItemId,
                ToWarehouseId = receipt.WarehouseId,
                Quantity = qty,
                QuantityBefore = 0,
                QuantityAfter = qty,
                ReferenceType = "supplier_stock_receipt",
                ReferenceId = receipt.Id,
                PerformedBy = receivedBy,
            }, ct);
        }

        receipt.Status = StatusReceived;
        receipt.ReceivedBy = receivedBy;
        receipt.ReceivedAt = DateTime.UtcNow;
        _repo.Update(receipt);

        await _repo.SaveChangesAsync(ct);

        var saved = await _repo.GetByIdAsync(tenantId, receiptId, ct);
        return (saved is null ? null : ToDto(saved), null);
    }

    // ── mapping ────────────────────────────────────────────────────────────

    internal static SupplierStockReceiptDto ToDto(SupplierStockReceipt r) => new(
        r.Id,
        r.WarehouseId,
        r.Warehouse?.Name ?? string.Empty,
        r.Status,
        r.Reference,
        r.Notes,
        r.ReceivedAt,
        r.CreatedAt,
        r.Items
            .OrderBy(i => i.ExpiryDate ?? DateOnly.MaxValue)
            .Select(ToItemDto)
            .ToList());

    private static SupplierStockReceiptItemDto ToItemDto(SupplierStockReceiptItem i) => new(
        i.Id,
        i.SupplierItemId,
        i.SupplierItem?.CustomName ?? i.SupplierItem?.Item?.Name ?? string.Empty,
        i.ExpiryDate,
        i.Quantity,
        i.BatchNumber,
        i.UnitCost,
        i.Notes);
}
