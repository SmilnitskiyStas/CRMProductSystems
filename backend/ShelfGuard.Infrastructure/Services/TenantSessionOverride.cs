using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Services;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Infrastructure.Services;

/// <summary>
/// EF Core/Postgres-backed implementation — see <see cref="ITenantSessionOverride"/> for the
/// contract and the security rules any new call site must satisfy. TASK-417.
/// </summary>
public sealed class TenantSessionOverride : ITenantSessionOverride
{
    private readonly AppDbContext _db;

    public TenantSessionOverride(AppDbContext db) => _db = db;

    public async Task<T> ExecuteAsync<T>(Guid tenantId, Func<Task<T>> action, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // SET LOCAL only takes effect for the current transaction — Postgres automatically
        // reverts app.tenant_id back to whatever TenantConnectionInterceptor set at
        // connection-open time (the null-UUID, for the consumer session this override exists
        // for) the instant this transaction commits or rolls back, including on an unhandled
        // exception from `action` below (the `await using` disposes the transaction, which
        // rolls back if CommitAsync was never reached). tenantId is a Guid, not a raw string,
        // so formatting it with :D can never contain anything other than hex digits and
        // dashes — no injection surface, same reasoning TenantConnectionInterceptor.BuildSetSql
        // already relies on for its own SET statements. Postgres's SET/SET LOCAL grammar does
        // not accept a bind parameter in the value position, so the usual
        // ExecuteSqlInterpolatedAsync (parameterized) path isn't available here — EF1002 is a
        // false positive given tenantId's Guid-typed origin, not a raw external string.
#pragma warning disable EF1002
        await _db.Database.ExecuteSqlRawAsync($"SET LOCAL app.tenant_id = '{tenantId:D}'", ct);
#pragma warning restore EF1002

        var result = await action();

        await tx.CommitAsync(ct);
        return result;
    }
}
