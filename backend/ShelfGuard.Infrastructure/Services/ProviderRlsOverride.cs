using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Services;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Infrastructure.Services;

/// <summary>
/// EF Core/Postgres-backed implementation — see <see cref="IProviderRlsOverride"/> for the
/// contract and the security rules any new call site must satisfy. TASK-643/KI-036, ADR-035.
/// </summary>
public sealed class ProviderRlsOverride : IProviderRlsOverride
{
    private readonly AppDbContext _db;

    public ProviderRlsOverride(AppDbContext db) => _db = db;

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken ct = default)
    {
        // Deliberately does NOT join an ambient transaction: if a caller ever nests this inside
        // ITenantSessionOverride/IAnalyticsRlsOverride (forbidden by the contract), EF throws
        // InvalidOperationException loudly instead of silently widening that transaction's RLS
        // context for the rest of its body.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // SET LOCAL only takes effect for the current transaction — Postgres automatically
        // reverts app.role back to whatever TenantConnectionInterceptor set at connection-open
        // time the instant this transaction commits or rolls back, including on an unhandled
        // exception from `action` below (the `await using` disposes the transaction, which
        // rolls back if CommitAsync was never reached). This is the entire fix for KI-036: the
        // old code issued a session-level `SET app.role = 'provider'` on a manually opened
        // DbConnection and never reset it. The string is a fixed literal — nothing
        // interpolated, so there is no injection surface and no EF1002 suppression is needed.
        await _db.Database.ExecuteSqlRawAsync("SET LOCAL app.role = 'provider'", ct);

        var result = await action();

        await tx.CommitAsync(ct);
        return result;
    }
}
