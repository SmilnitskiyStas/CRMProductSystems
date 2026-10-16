using Microsoft.Extensions.Diagnostics.HealthChecks;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Api.Infrastructure;

/// <summary>Readiness probe: the API can reach PostgreSQL. Runs without a tenant context.</summary>
public sealed class DbReadinessHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            return await db.Database.CanConnectAsync(cts.Token)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("database unreachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("database check failed", ex);
        }
    }
}
