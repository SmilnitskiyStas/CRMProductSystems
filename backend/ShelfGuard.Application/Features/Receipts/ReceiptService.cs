using System.Text.Json;
using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.Receipts.Dtos;
using ShelfGuard.Application.Features.Stock;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Receipts;

public sealed class ReceiptService : IReceiptService
{
    private readonly IReceiptRepository _repo;
    private readonly INotificationRepository _notifications;

    public ReceiptService(IReceiptRepository repo, INotificationRepository notifications)
    {
        _repo = repo;
        _notifications = notifications;
    }

    public async Task<List<ReceiptDto>> GetAllAsync(Guid? storeId, string? status, CancellationToken ct = default)
    {
        var receipts = await _repo.GetAllAsync(storeId, status, ct);
        return receipts.Select(ToDto).ToList();
    }

    public async Task<PagedResult<ReceiptDto>> GetPagedAsync(
        Guid? storeId, string? status, string? search, string? sortBy, bool? sortDescending,
        int page, int pageSize,
        CancellationToken ct = default)
    {
        var (receipts, total) = await _repo.GetPagedAsync(storeId, status, search, sortBy, sortDescending, page, pageSize, ct);
        return new PagedResult<ReceiptDto>
        {
            Items = receipts.Select(ToDto).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<ReceiptDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var receipt = await _repo.GetByIdAsync(id, ct);
        return receipt is null ? null : ToDto(receipt);
    }

    public async Task<(ReceiptDto? Receipt, string? Error)> CreateAsync(
        Guid tenantId, Guid createdBy, CreateReceiptRequest request, CancellationToken ct = default)
    {
        if (request.Items is null || request.Items.Count == 0)
            return (null, "Receipt must contain at least one item.");

        foreach (var item in request.Items)
        {
            if (item.QuantityOrdered <= 0)
                return (null, $"QuantityOrdered must be greater than 0 for all items.");
        }

        var receipt = new StockReceipt
        {
            TenantId = tenantId,
            SupplierId = request.SupplierId,
            DestinationStoreId = request.DestinationStoreId,
            ViaCentralStore = request.ViaCentralStore,
            ExpectedAt = request.ExpectedAt,
            Notes = request.Notes,
            Status = "draft",
            CreatedBy = createdBy,
        };

        foreach (var itemReq in request.Items)
        {
            receipt.Items.Add(new StockReceiptItem
            {
                ReceiptId = receipt.Id,
                ProductId = itemReq.ProductId,
                QuantityOrdered = itemReq.QuantityOrdered,
                PricePurchase = itemReq.PricePurchase,
                ExpiryDate = itemReq.ExpiryDate,
                BatchNumber = itemReq.BatchNumber,
            });
        }

        await _repo.AddAsync(receipt, ct);
        await _repo.SaveChangesAsync(ct);

        var saved = await _repo.GetByIdAsync(receipt.Id, ct);
        return (saved is null ? null : ToDto(saved), null);
    }

    public async Task<(ReceiptDto? Receipt, string? Error)> UpdateItemsAsync(
        Guid id, UpdateItemsRequest request, CancellationToken ct = default)
    {
        var receipt = await _repo.GetByIdAsync(id, ct);
        if (receipt is null)
            return (null, "Receipt not found.");

        if (receipt.Status is "received" or "cancelled")
            return (null, $"Cannot update items on a '{receipt.Status}' receipt.");

        foreach (var payload in request.Items)
        {
            var item = receipt.Items.FirstOrDefault(i => i.Id == payload.ItemId);
            if (item is null)
                return (null, $"Item {payload.ItemId} not found on this receipt.");

            if (payload.QuantityReceived.HasValue && payload.QuantityReceived.Value < 0)
                return (null, "QuantityReceived cannot be negative.");

            item.QuantityReceived = payload.QuantityReceived;
            item.PricePurchase = payload.PricePurchase ?? item.PricePurchase;
            item.ExpiryDate = payload.ExpiryDate ?? item.ExpiryDate;
            item.BatchNumber = payload.BatchNumber ?? item.BatchNumber;
            item.DiscrepancyNotes = payload.DiscrepancyNotes;

            _repo.UpdateItem(item);
        }

        _repo.Update(receipt);
        await _repo.SaveChangesAsync(ct);

        var saved = await _repo.GetByIdAsync(id, ct);
        return (saved is null ? null : ToDto(saved), null);
    }

    public async Task<(ReceiptDto? Receipt, string? Error)> ReceiveAsync(
        Guid id, Guid receivedBy, CancellationToken ct = default)
    {
        var receipt = await _repo.GetByIdAsync(id, ct);
        if (receipt is null)
            return (null, "Receipt not found.");

        if (receipt.Status == "received")
            return (null, "Receipt is already received.");

        if (receipt.Status == "cancelled")
            return (null, "Cannot receive a cancelled receipt.");

        // All items must have ExpiryDate set before confirming
        var unprocessed = receipt.Items
            .Where(i => i.ExpiryDate is null)
            .ToList();

        if (unprocessed.Count > 0)
            return (null, $"{unprocessed.Count} item(s) are missing ExpiryDate. Fill in all items before receiving.");

        // Create ProductStock batches for each item
        foreach (var item in receipt.Items)
        {
            var qty = item.QuantityReceived ?? item.QuantityOrdered;
            if (qty <= 0) continue;

            var stock = new ProductStock
            {
                TenantId = receipt.TenantId,
                ProductId = item.ProductId,
                StoreId = receipt.DestinationStoreId,
                BatchNumber = item.BatchNumber,
                Quantity = qty,
                QuantityInitial = qty,
                ExpiryDate = item.ExpiryDate!.Value,
                Status = StockStatus.Compute(qty, item.ExpiryDate!.Value, DateTime.UtcNow),
                SourceType = "receipt",
                SourceId = receipt.Id,
                AddedBy = receivedBy,
            };

            await _repo.AddStockAsync(stock, ct);

            var movement = new StockMovement
            {
                TenantId = receipt.TenantId,
                MovementType = "receipt",
                ProductStockId = stock.Id,
                ProductId = item.ProductId,
                ToStoreId = receipt.DestinationStoreId,
                Quantity = qty,
                QuantityBefore = 0,
                QuantityAfter = qty,
                UnitPrice = item.PricePurchase,
                TotalAmount = item.PricePurchase.HasValue ? item.PricePurchase.Value * qty : null,
                ReferenceId = receipt.Id,
                ReferenceType = "stock_receipt",
                PerformedBy = receivedBy,
            };

            await _repo.AddMovementAsync(movement, ct);
        }

        receipt.Status = "received";
        receipt.ReceivedAt = DateTime.UtcNow;
        receipt.ReceivedBy = receivedBy;

        _repo.Update(receipt);
        await EnqueueReceivedNotificationAsync(receipt, ct);
        await _repo.SaveChangesAsync(ct);

        var saved = await _repo.GetByIdAsync(id, ct);
        return (saved is null ? null : ToDto(saved), null);
    }

    /// <summary>
    /// ADR-018 §2: Postgres outbox row for the worker's notification-dispatch job to pick
    /// up and fan out to store staff (EventType = "receipt.created"). UserId = null,
    /// Channel = "system", Status = "pending" — excluded from GetHistoryAsync until dispatched.
    /// </summary>
    private async Task EnqueueReceivedNotificationAsync(StockReceipt receipt, CancellationToken ct)
    {
        var supplierName = receipt.Supplier?.Name ?? "Постачальник";
        var payload = JsonSerializer.Serialize(new
        {
            receiptId = receipt.Id,
            supplierId = receipt.SupplierId,
            supplierName,
            destinationStoreId = receipt.DestinationStoreId,
            itemCount = receipt.Items.Count,
        });

        await _notifications.EnqueueAsync(new NotificationQueue
        {
            TenantId  = receipt.TenantId,
            UserId    = null,
            StoreId   = receipt.DestinationStoreId,
            Title     = $"Надходження товару: {supplierName}",
            Channel   = "system",
            EventType = "receipt.created",
            Payload   = payload,
            Status    = "pending",
        }, ct);
    }

    public async Task<(ReceiptDto? Receipt, string? Error)> CancelAsync(
        Guid id, CancellationToken ct = default)
    {
        var receipt = await _repo.GetByIdAsync(id, ct);
        if (receipt is null)
            return (null, "Receipt not found.");

        if (receipt.Status == "received")
            return (null, "Cannot cancel a received receipt.");

        if (receipt.Status == "cancelled")
            return (null, "Receipt is already cancelled.");

        receipt.Status = "cancelled";
        _repo.Update(receipt);
        await _repo.SaveChangesAsync(ct);

        var saved = await _repo.GetByIdAsync(id, ct);
        return (saved is null ? null : ToDto(saved), null);
    }

    // ── mapping ────────────────────────────────────────────────────────────

    private static ReceiptDto ToDto(StockReceipt r) => new(
        r.Id,
        r.SupplierId,
        r.Supplier?.Name,
        r.DestinationStoreId,
        r.DestinationStore?.Name ?? "—",
        r.ViaCentralStore,
        r.Status,
        r.ExpectedAt,
        r.ReceivedAt,
        r.Notes,
        r.CreatedAt,
        r.Items.Select(ToItemDto).ToList()
    );

    private static ReceiptItemDto ToItemDto(StockReceiptItem i) => new(
        i.Id,
        i.ProductId,
        i.Product?.Name ?? "—",
        i.Product?.Barcodes.Count > 0 ? i.Product.Barcodes[0] : null,
        i.QuantityOrdered,
        i.QuantityReceived,
        i.PricePurchase,
        i.ExpiryDate,
        i.BatchNumber,
        i.DiscrepancyNotes,
        IsProcessed: i.QuantityReceived.HasValue && i.ExpiryDate.HasValue
    );
}
