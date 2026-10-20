using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class CashRegisterRepository : ICashRegisterRepository
{
    private readonly AppDbContext _db;

    public CashRegisterRepository(AppDbContext db) => _db = db;

    public Task<CashRegister?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.CashRegisters.Include(r => r.Store).FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<CashRegister?> GetByActivationCodeHashAsync(string codeHash, CancellationToken ct = default) =>
        _db.CashRegisters.Include(r => r.Store).FirstOrDefaultAsync(r => r.ActivationCodeHash == codeHash, ct);

    public async Task<IReadOnlyList<CashRegister>> ListAsync(Guid? storeId, CancellationToken ct = default)
    {
        var q = _db.CashRegisters.AsNoTracking().Include(r => r.Store).AsQueryable();
        if (storeId.HasValue) q = q.Where(r => r.StoreId == storeId.Value);
        return await q.OrderBy(r => r.Number).ToListAsync(ct);
    }

    public async Task<int> GetNextNumberAsync(Guid tenantId, CancellationToken ct = default)
    {
        var max = await _db.CashRegisters.Where(r => r.TenantId == tenantId)
            .Select(r => (int?)r.Number).MaxAsync(ct);
        return (max ?? 0) + 1;
    }

    public Task<bool> NumberExistsAsync(Guid tenantId, int number, CancellationToken ct = default) =>
        _db.CashRegisters.AnyAsync(r => r.TenantId == tenantId && r.Number == number, ct);

    // Explicit DbSet.Add (never via a navigation collection): a Guid-keyed entity reached through
    // a tracked parent would be marked Modified and UPDATE 0 rows.
    public Task AddAsync(CashRegister register, CancellationToken ct = default) =>
        _db.CashRegisters.AddAsync(register, ct).AsTask();

    public async Task<bool> TryActivateAsync(
        Guid registerId, string codeHash, string deviceTokenHash, DateTime nowUtc, CancellationToken ct = default)
    {
        var rows = await _db.CashRegisters
            .Where(r => r.Id == registerId
                        && r.Status != CashRegister.StatusRevoked
                        && r.ActivationCodeHash == codeHash
                        && r.ActivationCodeExpiresAt != null
                        && r.ActivationCodeExpiresAt > nowUtc)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, CashRegister.StatusActive)
                .SetProperty(r => r.DeviceTokenHash, deviceTokenHash)
                .SetProperty(r => r.ActivationCodeHash, (string?)null)
                .SetProperty(r => r.ActivationCodeExpiresAt, (DateTime?)null)
                .SetProperty(r => r.ActivatedAt, nowUtc), ct);
        return rows == 1;
    }

    public async Task<PinFailureOutcome> RegisterPinFailureAsync(
        Guid registerId, DateTime nowUtc, TimeSpan window, int maxDaily, CancellationToken ct = default)
    {
        var cutoff = nowUtc - window;
        var day = DateOnly.FromDateTime(nowUtc);

        // One statement: every SET expression reads the OLD row, so concurrent failures serialise
        // on the row lock and each sees (and increments) the committed count. Mirrors
        // CashRegister.RegisterPinFailure.
        var rows = await _db.Database.SqlQuery<PinFailureRow>($"""
            UPDATE cash_registers SET
              "PinFailCount" = CASE WHEN "PinFailWindowStart" IS NULL OR "PinFailWindowStart" <= {cutoff}
                                    THEN 1 ELSE "PinFailCount" + 1 END,
              "PinFailWindowStart" = CASE WHEN "PinFailWindowStart" IS NULL OR "PinFailWindowStart" <= {cutoff}
                                          THEN {nowUtc} ELSE "PinFailWindowStart" END,
              "PinFailDailyCount" = CASE WHEN "PinFailDay" = {day} THEN "PinFailDailyCount" + 1 ELSE 1 END,
              "PinFailDay" = {day},
              "Status" = CASE WHEN "Status" = 'active'
                               AND (CASE WHEN "PinFailDay" = {day} THEN "PinFailDailyCount" + 1 ELSE 1 END) >= {maxDaily}
                              THEN 'locked' ELSE "Status" END
            WHERE "Id" = {registerId}
            RETURNING "PinFailCount" AS "WindowCount", "PinFailDailyCount" AS "DailyCount", "Status" AS "Status"
            """).ToListAsync(ct);

        var row = rows.SingleOrDefault();
        return row is null
            ? new PinFailureOutcome(0, 0, CashRegister.StatusRevoked)
            : new PinFailureOutcome(row.WindowCount, row.DailyCount, row.Status);
    }

    private sealed class PinFailureRow
    {
        public int WindowCount { get; set; }
        public int DailyCount { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    public Task AddSessionAsync(RegisterSession session, CancellationToken ct = default) =>
        _db.RegisterSessions.AddAsync(session, ct).AsTask();

    public Task<RegisterSession?> GetSessionAsync(Guid sessionId, CancellationToken ct = default) =>
        _db.RegisterSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);

    public Task<bool> HadSessionAtAsync(
        Guid registerId, Guid cashierId, DateTime at, TimeSpan endTolerance, CancellationToken ct = default)
    {
        var endedAfter = at - endTolerance;
        return _db.RegisterSessions.AnyAsync(s =>
            s.RegisterId == registerId && s.CashierId == cashierId && s.StartedAt <= at
            && (s.EndedAt == null || s.EndedAt >= endedAfter), ct);
    }

    public async Task<IReadOnlyList<RegisterSession>> GetOpenSessionsAsync(Guid registerId, CancellationToken ct = default) =>
        await _db.RegisterSessions.Where(s => s.RegisterId == registerId && s.EndedAt == null).ToListAsync(ct);

    public async Task<IReadOnlyList<User>> GetPinUsersByStoreAsync(Guid storeId, CancellationToken ct = default) =>
        await _db.Users
            .Where(u => u.StoreId == storeId && u.IsActive && u.PosPinHash != null)
            .ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
