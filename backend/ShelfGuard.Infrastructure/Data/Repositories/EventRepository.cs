using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class EventRepository : IEventRepository
{
    private readonly AppDbContext _db;

    public EventRepository(AppDbContext db) => _db = db;

    public async Task<List<DemandEvent>> GetAsync(
        DateOnly? from, DateOnly? to, Guid? storeId, CancellationToken ct = default)
    {
        var query = _db.DemandEvents
            .Include(e => e.Coefficients)
            .AsQueryable();

        // Recurring events match any year — only filter non-recurring by range here;
        // recurring ones are kept and the caller/UI projects them onto the requested year.
        if (from.HasValue)
            query = query.Where(e => e.IsRecurring || e.EndsAt >= from);
        if (to.HasValue)
            query = query.Where(e => e.IsRecurring || e.StartsAt <= to);
        if (storeId.HasValue)
            query = query.Where(e => e.Scope == "network" || e.StoreId == storeId);

        return await query.OrderBy(e => e.StartsAt).ToListAsync(ct);
    }

    public Task<DemandEvent?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.DemandEvents
            .Include(e => e.Coefficients)
            .FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<List<DemandEvent>> GetCandidatesForDateAsync(
        Guid storeId, DateOnly date, CancellationToken ct = default)
    {
        // SQL narrows to plausible candidates; exact (incl. recurring month/day wrap)
        // matching happens in memory via DemandEvent.IsActiveOn.
        var candidates = await _db.DemandEvents
            .Include(e => e.Coefficients)
            .Where(e => e.Scope == "network" || e.StoreId == storeId)
            .Where(e => e.IsRecurring || (e.StartsAt <= date && e.EndsAt >= date))
            .ToListAsync(ct);

        return candidates.Where(e => e.IsActiveOn(date)).ToList();
    }

    public Task<DemandEventCoefficient?> GetCoefficientAsync(Guid coefId, CancellationToken ct = default) =>
        _db.DemandEventCoefficients.FirstOrDefaultAsync(c => c.Id == coefId, ct);

    public Task<int> CountByNameAndTypeAsync(Guid tenantId, string eventType, CancellationToken ct = default) =>
        _db.DemandEvents.CountAsync(e => e.TenantId == tenantId && e.EventType == eventType, ct);

    public async Task AddAsync(DemandEvent demandEvent, CancellationToken ct = default) =>
        await _db.DemandEvents.AddAsync(demandEvent, ct);

    public async Task AddCoefficientAsync(DemandEventCoefficient coefficient, CancellationToken ct = default) =>
        await _db.DemandEventCoefficients.AddAsync(coefficient, ct);

    public void Remove(DemandEvent demandEvent) =>
        _db.DemandEvents.Remove(demandEvent);

    public void RemoveCoefficient(DemandEventCoefficient coefficient) =>
        _db.DemandEventCoefficients.Remove(coefficient);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
