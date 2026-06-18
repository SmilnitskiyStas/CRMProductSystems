using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.Stock;
using ShelfGuard.Application.Features.Transfers.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Transfers;

public sealed class TransferService : ITransferService
{
    private readonly ITransferRepository _repo;

    public TransferService(ITransferRepository repo) => _repo = repo;

    public async Task<List<TransferDto>> GetAllAsync(Guid? storeId, string? status, CancellationToken ct = default)
    {
        var transfers = await _repo.GetAllAsync(storeId, status, ct);
        return transfers.Select(ToDto).ToList();
    }

    public async Task<PagedResult<TransferDto>> GetPagedAsync(
        Guid? storeId, string? status, int page, int pageSize,
        CancellationToken ct = default)
    {
        var (transfers, total) = await _repo.GetPagedAsync(storeId, status, page, pageSize, ct);
        return new PagedResult<TransferDto>
        {
            Items = transfers.Select(ToDto).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<TransferDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var transfer = await _repo.GetByIdAsync(id, ct);
        return transfer is null ? null : ToDto(transfer);
    }

    public async Task<(TransferDto? Transfer, string? Error)> CreateAsync(
        Guid tenantId, Guid initiatedBy, CreateTransferRequest request, CancellationToken ct = default)
    {
        if (request.Items is null || request.Items.Count == 0)
            return (null, "Transfer must contain at least one item.");

        if (request.FromStoreId == request.ToStoreId)
            return (null, "Source and destination stores must be different.");

        if (!IsValidTransferType(request.TransferType))
            return (null, $"Invalid transfer type '{request.TransferType}'. Valid: store_to_store, cs_to_store, store_to_production.");

        var transfer = new StockTransfer
        {
            TenantId = tenantId,
            FromStoreId = request.FromStoreId,
            ToStoreId = request.ToStoreId,
            TransferType = request.TransferType,
            Notes = request.Notes,
            Status = "in_transit",
            InitiatedBy = initiatedBy,
        };

        // Validate and deduct source stock for each item
        foreach (var itemReq in request.Items)
        {
            if (itemReq.Quantity <= 0)
                return (null, "All item quantities must be greater than 0.");

            var sourceStock = await _repo.GetStockByIdAsync(itemReq.ProductStockId, ct);
            if (sourceStock is null)
                return (null, $"Stock batch {itemReq.ProductStockId} not found.");

            if (sourceStock.StoreId != request.FromStoreId)
                return (null, $"Stock batch {itemReq.ProductStockId} does not belong to the source store.");

            if (sourceStock.Quantity < itemReq.Quantity)
                return (null, $"Insufficient quantity in batch {itemReq.ProductStockId}. Available: {sourceStock.Quantity}, requested: {itemReq.Quantity}.");

            // Deduct from source immediately (stock is in transit)
            sourceStock.Quantity -= itemReq.Quantity;
            sourceStock.Status = StockStatus.Compute(sourceStock.Quantity, sourceStock.ExpiryDate, sourceStock.LastCheckedAt);
            _repo.UpdateStock(sourceStock);

            // expiry_date and batch_number copied as-is (spec rule: never change on transfer)
            transfer.Items.Add(new StockTransferItem
            {
                TransferId = transfer.Id,
                ProductStockId = itemReq.ProductStockId,
                ProductId = sourceStock.ProductId,
                Quantity = itemReq.Quantity,
                ExpiryDate = sourceStock.ExpiryDate,
                BatchNumber = sourceStock.BatchNumber,
            });

            // Log outbound movement
            await _repo.AddMovementAsync(new StockMovement
            {
                TenantId = tenantId,
                MovementType = "transfer",
                ProductStockId = sourceStock.Id,
                ProductId = sourceStock.ProductId,
                FromStoreId = request.FromStoreId,
                ToStoreId = request.ToStoreId,
                Quantity = itemReq.Quantity,
                QuantityBefore = sourceStock.Quantity + itemReq.Quantity,
                QuantityAfter = sourceStock.Quantity,
                ReferenceId = transfer.Id,
                ReferenceType = "stock_transfer",
                PerformedBy = initiatedBy,
            }, ct);
        }

        await _repo.AddAsync(transfer, ct);
        await _repo.SaveChangesAsync(ct);

        var saved = await _repo.GetByIdAsync(transfer.Id, ct);
        return (saved is null ? null : ToDto(saved), null);
    }

    public async Task<(TransferDto? Transfer, string? Error)> ConfirmAsync(
        Guid id, Guid confirmedBy, CancellationToken ct = default)
    {
        var transfer = await _repo.GetByIdAsync(id, ct);
        if (transfer is null)
            return (null, "Transfer not found.");

        if (transfer.Status == "received")
            return (null, "Transfer is already received.");

        if (transfer.Status == "cancelled")
            return (null, "Cannot confirm a cancelled transfer.");

        // Create new ProductStock batches at destination store
        foreach (var item in transfer.Items)
        {
            var destStock = new ProductStock
            {
                TenantId = transfer.TenantId,
                ProductId = item.ProductId,
                StoreId = transfer.ToStoreId,
                BatchNumber = item.BatchNumber,    // copied as-is
                Quantity = item.Quantity,
                QuantityInitial = item.Quantity,
                ExpiryDate = item.ExpiryDate,      // copied as-is
                Status = StockStatus.Compute(item.Quantity, item.ExpiryDate, DateTime.UtcNow),
                SourceType = "transfer",
                SourceId = transfer.Id,
                AddedBy = confirmedBy,
            };

            await _repo.AddStockAsync(destStock, ct);

            await _repo.AddMovementAsync(new StockMovement
            {
                TenantId = transfer.TenantId,
                MovementType = "transfer",
                ProductStockId = destStock.Id,
                ProductId = item.ProductId,
                FromStoreId = transfer.FromStoreId,
                ToStoreId = transfer.ToStoreId,
                Quantity = item.Quantity,
                QuantityBefore = 0,
                QuantityAfter = item.Quantity,
                ReferenceId = transfer.Id,
                ReferenceType = "stock_transfer",
                PerformedBy = confirmedBy,
            }, ct);
        }

        transfer.Status = "received";
        transfer.ConfirmedBy = confirmedBy;
        _repo.Update(transfer);
        await _repo.SaveChangesAsync(ct);

        var saved = await _repo.GetByIdAsync(id, ct);
        return (saved is null ? null : ToDto(saved), null);
    }

    public async Task<(TransferDto? Transfer, string? Error)> CancelAsync(
        Guid id, CancellationToken ct = default)
    {
        var transfer = await _repo.GetByIdAsync(id, ct);
        if (transfer is null)
            return (null, "Transfer not found.");

        if (transfer.Status == "received")
            return (null, "Cannot cancel a received transfer.");

        if (transfer.Status == "cancelled")
            return (null, "Transfer is already cancelled.");

        // Restore deducted source stock
        foreach (var item in transfer.Items)
        {
            var sourceStock = await _repo.GetStockByIdAsync(item.ProductStockId, ct);
            if (sourceStock is not null)
            {
                sourceStock.Quantity += item.Quantity;
                sourceStock.Status = StockStatus.Compute(sourceStock.Quantity, sourceStock.ExpiryDate, sourceStock.LastCheckedAt);
                _repo.UpdateStock(sourceStock);
            }
        }

        transfer.Status = "cancelled";
        _repo.Update(transfer);
        await _repo.SaveChangesAsync(ct);

        var saved = await _repo.GetByIdAsync(id, ct);
        return (saved is null ? null : ToDto(saved), null);
    }

    // ── mapping ────────────────────────────────────────────────────────────

    private static TransferDto ToDto(StockTransfer t) => new(
        t.Id,
        t.FromStoreId,
        t.FromStore?.Name ?? "—",
        t.ToStoreId,
        t.ToStore?.Name ?? "—",
        t.TransferType,
        t.Status,
        t.Notes,
        t.CreatedAt,
        t.Items.Select(i => new TransferItemDto(
            i.Id, i.ProductStockId, i.ProductId,
            i.Product?.Name ?? "—", i.Quantity, i.ExpiryDate, i.BatchNumber
        )).ToList()
    );

    private static bool IsValidTransferType(string? type) =>
        type is null or "store_to_store" or "cs_to_store" or "store_to_production";
}
