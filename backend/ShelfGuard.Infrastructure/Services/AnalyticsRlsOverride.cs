using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Services;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Infrastructure.Services;

/// <summary>
/// EF Core/Postgres-backed implementation — see <see cref="IAnalyticsRlsOverride"/> for the
/// contract and the security rules any new call site must satisfy. TASK-508/KI-033, ADR-028.
/// </summary>
public sealed class AnalyticsRlsOverride : IAnalyticsRlsOverride
{
    private readonly AppDbContext _db;

    public AnalyticsRlsOverride(AppDbContext db) => _db = db;

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // SET LOCAL only takes effect for the current transaction — Postgres automatically
        // reverts app.role back to whatever TenantConnectionInterceptor set at connection-open
        // time the instant this transaction commits or rolls back, including on an unhandled
        // exception from `action` below (the `await using` disposes the transaction, which
        // rolls back if CommitAsync was never reached). The string is a fixed literal — nothing
        // interpolated, so there is no injection surface and no EF1002 suppression is needed.
        await _db.Database.ExecuteSqlRawAsync("SET LOCAL app.role = 'marketing_analytics_bypass'", ct);

        var result = await action();

        await tx.CommitAsync(ct);
        return result;
    }
}
