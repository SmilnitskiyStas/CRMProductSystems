using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class RegisterReportRepository : IRegisterReportRepository
{
    private const string CancelledStatus = "cancelled";
    private readonly AppDbContext _db;

    public RegisterReportRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<RegisterOperatorRow>> GetCurrentOperatorsAsync(
        IReadOnlyCollection<Guid> registerIds, CancellationToken ct = default) =>
        await _db.RegisterSessions.AsNoTracking()
            .Where(s => registerIds.Contains(s.RegisterId) && s.EndedAt == null)
            .OrderByDescending(s => s.StartedAt)
            .Select(s => new RegisterOperatorRow(s.RegisterId, s.CashierId, s.Cashier!.FullName, s.StartedAt))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<RegisterSalesAggregate>> GetSalesAggregatesAsync(
        IReadOnlyCollection<Guid> registerIds, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        var groups = await _db.PosTransactions.AsNoTracking()
            .Where(t => t.CashRegisterId != null && registerIds.Contains(t.CashRegisterId.Value)
                        && t.CreatedAt >= fromUtc && t.CreatedAt < toUtc && t.Status != CancelledStatus)
            .GroupBy(t => new { RegisterId = t.CashRegisterId!.Value, t.CashierId, t.PaymentType })
            .Select(g => new
            {
                g.Key.RegisterId, g.Key.CashierId, g.Key.PaymentType,
                Receipts = g.Count(), Turnover = g.Sum(x => x.TotalAmount),
            })
            .ToListAsync(ct);

        var cashierIds = groups.Where(g => g.CashierId != null).Select(g => g.CashierId!.Value).Distinct().ToList();
        var names = await _db.Users.AsNoTracking()
            .Where(u => cashierIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName })
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        return groups.Select(g => new RegisterSalesAggregate(
            g.RegisterId, g.CashierId,
            g.CashierId.HasValue && names.TryGetValue(g.CashierId.Value, out var n) ? n : null,
            g.PaymentType, g.Receipts, g.Turnover)).ToList();
    }

    public async Task<(IReadOnlyList<RegisterSessionRow> Items, int Total)> GetSessionsPageAsync(
        Guid registerId, int skip, int take, CancellationToken ct = default)
    {
        var q = _db.RegisterSessions.AsNoTracking().Where(s => s.RegisterId == registerId);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(s => s.StartedAt).Skip(skip).Take(take)
            .Select(s => new RegisterSessionRow(
                s.Id, s.CashierId, s.Cashier!.FullName, s.StartedAt, s.EndedAt, s.EndReason))
            .ToListAsync(ct);
        return (items, total);
    }

    public async Task<(IReadOnlyList<RegisterShiftRow> Items, int Total)> GetShiftsPageAsync(
        Guid registerId, int skip, int take, CancellationToken ct = default)
    {
        var q = _db.PosShifts.AsNoTracking().Where(s => s.RegisterId == registerId);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(s => s.OpenedAt).Skip(skip).Take(take)
            .Select(s => new RegisterShiftRow(
                s.Id, s.ShiftNumber, s.CashierId, s.Cashier != null ? s.Cashier.FullName : null,
                s.OpenedAt, s.ClosedAt, s.OpeningCash, s.ClosingCash, s.TotalSales,
                _db.PosTransactions.Count(t => t.ShiftId == s.Id && t.Status != CancelledStatus)))
            .ToListAsync(ct);
        return (items, total);
    }

    public async Task<IReadOnlyList<PosEmployeeRow>> GetPosEmployeesAsync(
        Guid storeId, IReadOnlyCollection<string> roles, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _db.Users.AsNoTracking()
            .Where(u => u.StoreId == storeId && u.IsActive && roles.Contains(u.Role))
            .OrderBy(u => u.FullName)
            .Select(u => new PosEmployeeRow(
                u.Id, u.FullName, u.Role, u.PosPinHash != null, u.PosPinSetAt,
                u.PosPinLockoutUntil != null && u.PosPinLockoutUntil > now))
            .ToListAsync(ct);
    }
}
