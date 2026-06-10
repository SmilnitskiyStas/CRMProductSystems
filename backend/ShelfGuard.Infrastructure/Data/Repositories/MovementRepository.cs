using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class MovementRepository : IMovementRepository
{
    private readonly AppDbContext _db;

    public MovementRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<StockMovement>> GetAsync(
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
        var query = BuildQuery(tenantId, productId, storeId, type, from, to);

        return await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public Task<int> CountAsync(
        Guid tenantId,
        Guid? productId,
        Guid? storeId,
        string? type,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default)
    {
        return BuildQuery(tenantId, productId, storeId, type, from, to).CountAsync(ct);
    }

    private IQueryable<StockMovement> BuildQuery(
        Guid tenantId,
        Guid? productId,
        Guid? storeId,
        string? type,
        DateOnly? from,
        DateOnly? to)
    {
        var query = _db.StockMovements
            .Where(m => m.TenantId == tenantId)
            .AsQueryable();

        if (productId.HasValue)
            query = query.Where(m => m.ProductId == productId);

        if (storeId.HasValue)
            query = query.Where(m => m.FromStoreId == storeId || m.ToStoreId == storeId);

        if (!string.IsNullOrWhiteSpace(type))
            query = query.Where(m => m.MovementType == type);

        if (from.HasValue)
        {
            var fromUtc = from.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(m => m.CreatedAt >= fromUtc);
        }

        if (to.HasValue)
        {
            var toUtc = to.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            query = query.Where(m => m.CreatedAt <= toUtc);
        }

        return query;
    }
}
