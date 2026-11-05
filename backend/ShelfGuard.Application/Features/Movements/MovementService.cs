using ShelfGuard.Application.Features.Movements.Dtos;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Movements;

public sealed class MovementService : IMovementService
{
    private readonly IMovementRepository _repo;

    public MovementService(IMovementRepository repo) => _repo = repo;

    public async Task<StockHistoryDto> GetStockHistoryAsync(
        Guid tenantId, Guid productId, Guid? storeId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (productId == Guid.Empty || from > to || to > today || from < today.AddDays(-365))
            throw new ArgumentException("Provide a product and a date range within the last 366 days.");
        var snapshot = await _repo.GetStockHistoryAsync(tenantId, productId, storeId, from, ct);
        // Rewind the actual current balance. Changes AFTER the selected window matter too:
        // otherwise an old period would incorrectly end at today's balance.
        var balance = snapshot.CurrentStock;
        var points = new List<StockHistoryPointDto>();
        for (var day = snapshot.AsOfDate; day >= from; day = day.AddDays(-1))
        {
            if (day <= to) points.Add(new StockHistoryPointDto(day, balance));
            balance -= snapshot.DailyChanges.GetValueOrDefault(day);
            if (day == from) break;
        }
        points.Reverse();
        return new StockHistoryDto(snapshot.CurrentStock, points);
    }

    public async Task<IReadOnlyList<IncomingSupplyDto>> GetIncomingAsync(
        Guid tenantId, Guid productId, Guid? storeId, CancellationToken ct = default)
    {
        var rows = await _repo.GetIncomingAsync(tenantId, productId, storeId, ct);
        return rows
            .OrderBy(r => r.ExpectedAt ?? DateOnly.MaxValue)
            .ThenBy(r => r.OrderedAt)
            .Select(r => new IncomingSupplyDto(
                r.Source, r.Id, r.Number, r.Status, r.Quantity, r.Unit, r.OrderedAt, r.ExpectedAt,
                r.ShippedAt, r.SupplierName, r.DestinationStoreId, r.DestinationStoreName))
            .ToList();
    }

    public async Task<MovementPageDto> GetAsync(
        Guid tenantId,
        Guid? productId,
        Guid? storeId,
        string? type,
        DateOnly? from,
        DateOnly? to,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page     = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        // DbContext is not thread-safe: run the two queries sequentially (BUG-007).
        var items = await _repo.GetAsync(tenantId, productId, storeId, type, from, to, page, pageSize, ct);
        var total = await _repo.CountAsync(tenantId, productId, storeId, type, from, to, ct);

        // Resolve display data for just this page (one query per kind, none when there is nothing to look up).
        var storeIds = items.SelectMany(m => new[] { m.FromStoreId, m.ToStoreId })
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        var userIds = items.Where(m => m.PerformedBy.HasValue)
            .Select(m => m.PerformedBy!.Value).Distinct().ToList();
        var stockIds = items.Where(m => m.ProductStockId.HasValue)
            .Select(m => m.ProductStockId!.Value).Distinct().ToList();

        var storeNames = await _repo.GetStoreNamesAsync(storeIds, ct);
        var userNames = await _repo.GetUserNamesAsync(userIds, ct);
        var batches = await _repo.GetBatchesAsync(stockIds, ct);

        var dtos = items.Select(m =>
        {
            (string? BatchNumber, DateOnly ExpiryDate) batch = default;
            var hasBatch = m.ProductStockId.HasValue && batches.TryGetValue(m.ProductStockId.Value, out batch);
            return new MovementDto(
                m.Id,
                m.MovementType,
                m.ProductId,
                null,
                m.FromStoreId,
                m.FromStoreId.HasValue && storeNames.TryGetValue(m.FromStoreId.Value, out var fromName) ? fromName : null,
                m.ToStoreId,
                m.ToStoreId.HasValue && storeNames.TryGetValue(m.ToStoreId.Value, out var toName) ? toName : null,
                m.Quantity,
                m.QuantityBefore,
                m.QuantityAfter,
                m.UnitPrice,
                m.TotalAmount,
                m.ReferenceId,
                m.ReferenceType,
                m.Notes,
                m.CreatedAt,
                m.PerformedBy,
                m.PerformedBy.HasValue && userNames.TryGetValue(m.PerformedBy.Value, out var userName) ? userName : null,
                hasBatch ? batch.BatchNumber : null,
                hasBatch ? batch.ExpiryDate : null);
        }).ToList();

        return new MovementPageDto(dtos, total, page, pageSize);
    }
}
