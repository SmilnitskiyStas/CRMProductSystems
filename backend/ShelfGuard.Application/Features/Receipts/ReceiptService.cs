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
    private readonly IActivityLogRepository _activityLogs;

    public ReceiptService(
        IReceiptRepository repo, INotificationRepository notifications, IActivityLogRepository activityLogs)
    {
        _repo = repo;
        _notifications = notifications;
        _activityLogs = activityLogs;
    }

    public async Task<List<ReceiptDto>> GetAllAsync(Guid? storeId, string? status, CancellationToken ct = default)
    {
        var receipts = await _repo.GetAllAsync(storeId, status, ct);
        return await MapAsync(receipts, ct);
    }

    public async Task<PagedResult<ReceiptDto>> GetPagedAsync(
        Guid? storeId, string? status, string? search, string? sortBy, bool? sortDescending,
        int page, int pageSize,
        Guid? categoryId = null, int? minItems = null, int? maxItems = null,
        string? source = null,
        CancellationToken ct = default)
    {
        var (receipts, total) = await _repo.GetPagedAsync(
            storeId, status, search, sortBy, sortDescending, page, pageSize,
            categoryId, minItems, maxItems, source, ct);
        return new PagedResult<ReceiptDto>
        {
            Items = await MapAsync(receipts, ct),
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<ReceiptDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var receipt = await _repo.GetByIdAsync(id, ct);
        return receipt is null ? null : await MapAsync(receipt, ct);
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
        return (saved is null ? null : await MapAsync(saved, ct), null);
    }

    public async Task<(ReceiptDto? Receipt, string? Error)> UpdateItemsAsync(
        Guid id, UpdateItemsRequest request, CancellationToken ct = default, Guid? callerUserId = null)
    {
        var receipt = await _repo.GetByIdAsync(id, ct);
        if (receipt is null)
            return (null, "Receipt not found.");

        if (receipt.Status is "received" or "cancelled")
            return (null, $"Cannot update items on a '{receipt.Status}' receipt.");

        // TASK-755: a draft taken into work by someone else is locked for editing.
        if (receipt.TakenByUserId is { } holder && holder != callerUserId)
            return (null, ReceiptErrors.TakenByOther);

        foreach (var payload in request.Items)
        {
            var item = receipt.Items.FirstOrDefault(i => i.Id == payload.ItemId);
            if (item is null)
                return (null, $"Item {payload.ItemId} not found on this receipt.");

            if (payload.QuantityReceived.HasValue && payload.QuantityReceived.Value < 0)
                return (null, "QuantityReceived cannot be negative.");

            if (payload.QuantityOrdered.HasValue)
            {
                if (receipt.Status != "draft")
                    return (null, "The ordered quantity can only be changed on a draft.");
                if (payload.QuantityOrdered.Value <= 0)
                    return (null, "QuantityOrdered must be greater than 0.");
                item.QuantityOrdered = payload.QuantityOrdered.Value;
            }

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
        return (saved is null ? null : await MapAsync(saved, ct), null);
    }

    public async Task<(ReceiptDto? Receipt, string? Error)> TakeAsync(
        Guid id, Guid userId, CancellationToken ct = default)
    {
        var receipt = await _repo.GetByIdAsync(id, ct);
        if (receipt is null) return (null, ReceiptErrors.NotFound);
        if (receipt.Status != "draft") return (null, "Only a draft order can be taken into work.");
        if (receipt.TakenByUserId == userId) return (await MapAsync(receipt, ct), null); // idempotent
        if (receipt.TakenByUserId is not null) return (null, ReceiptErrors.TakenByOther);

        // Compare-and-set: a concurrent taker that won the race leaves 0 rows for us -> 409.
        if (!await _repo.TryTakeAsync(id, userId, DateTime.UtcNow, ct))
            return (null, ReceiptErrors.TakenByOther);

        var saved = await _repo.GetByIdAsync(id, ct);
        return (saved is null ? null : await MapAsync(saved, ct), null);
    }

    public async Task<(ReceiptDto? Receipt, string? Error)> ReleaseAsync(
        Guid id, Guid userId, bool canOverride, CancellationToken ct = default)
    {
        var receipt = await _repo.GetByIdAsync(id, ct);
        if (receipt is null) return (null, ReceiptErrors.NotFound);
        if (receipt.TakenByUserId is null) return (await MapAsync(receipt, ct), null); // idempotent
        if (receipt.TakenByUserId != userId && !canOverride) return (null, ReceiptErrors.ReleaseForbidden);

        receipt.TakenByUserId = null;
        receipt.TakenAt = null;
        _repo.Update(receipt);
        await _repo.SaveChangesAsync(ct);

        var saved = await _repo.GetByIdAsync(id, ct);
        return (saved is null ? null : await MapAsync(saved, ct), null);
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
        return (saved is null ? null : await MapAsync(saved, ct), null);
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

    public async Task<(ReceiptDto? Receipt, string? Error)> PostponeAsync(
        Guid id, Guid userId, PostponeReceiptRequest request, CancellationToken ct = default)
    {
        var receipt = await _repo.GetByIdAsync(id, ct);
        if (receipt is null)
            return (null, "Receipt not found.");

        if (receipt.Status is not ("draft" or "ordered" or "in_transit"))
            return (null, "Only an open order can be postponed.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (request.NewExpectedAt <= today)
            return (null, "The new delivery date must be in the future.");

        if (request.NewExpectedAt > today.AddDays(365))
            return (null, "The new delivery date is too far in the future (max 365 days).");

        if (receipt.ExpectedAt is { } current && request.NewExpectedAt <= current && current > today)
            return (null, "The new date must be later than the current expected date.");

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (reason is { Length: > 500 })
            return (null, "Reason is too long (max 500 characters).");

        var previousExpectedAt = receipt.ExpectedAt;
        receipt.OriginalExpectedAt ??= receipt.ExpectedAt;
        receipt.ExpectedAt = request.NewExpectedAt;
        receipt.PostponeCount += 1;
        receipt.PostponedAt = DateTime.UtcNow;
        receipt.PostponedBy = userId;
        receipt.DelayReason = reason;

        _repo.Update(receipt);
        await _repo.SaveChangesAsync(ct);

        await _activityLogs.LogAsync(new ActivityLog
        {
            TenantId = receipt.TenantId,
            UserId = userId,
            Action = "receipt.postponed",
            EntityType = "stock_receipt",
            EntityId = receipt.Id,
            Meta = $"from={previousExpectedAt:yyyy-MM-dd}; to={request.NewExpectedAt:yyyy-MM-dd}; count={receipt.PostponeCount}",
        }, ct);
        await _activityLogs.SaveChangesAsync(ct);

        var saved = await _repo.GetByIdAsync(id, ct);
        return (saved is null ? null : await MapAsync(saved, ct), null);
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
        return (saved is null ? null : await MapAsync(saved, ct), null);
    }

    // ── mapping ────────────────────────────────────────────────────────────

    private async Task<ReceiptDto> MapAsync(StockReceipt r, CancellationToken ct) =>
        (await MapAsync([r], ct))[0];

    private async Task<List<ReceiptDto>> MapAsync(IReadOnlyCollection<StockReceipt> receipts, CancellationToken ct)
    {
        var dtos = receipts.Select(ToDto).ToList();
        var ids = receipts.Where(r => r.TakenByUserId.HasValue).Select(r => r.TakenByUserId!.Value).Distinct().ToList();
        if (ids.Count == 0) return dtos;

        var names = await _repo.GetUserNamesAsync(ids, ct);
        if (names is null) return dtos;
        return dtos.Select(d => d.TakenByUserId is { } u && names.TryGetValue(u, out var n) ? d with { TakenByName = n } : d).ToList();
    }

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
        r.Items.Select(ToItemDto).ToList(),
        r.PostponeCount,
        r.OriginalExpectedAt,
        r.PostponedAt,
        r.DelayReason,
        r.Source,
        r.TakenByUserId,
        null,
        r.TakenAt
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
