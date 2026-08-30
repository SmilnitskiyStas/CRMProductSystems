using ShelfGuard.Application.Services;

namespace ShelfGuard.Tests.Marketplace;

/// <summary>
/// TASK-643: test double for <see cref="IProviderRlsOverride"/> that simply invokes the delegate
/// instead of opening a real transaction and issuing SET LOCAL app.role — same pure pass-through
/// convention the suite already uses for <c>ITenantSessionOverride</c>. Used by repository tests
/// that run against the EF InMemory provider (which has no transaction support at all), so the
/// method under test can be exercised without a live Postgres.
///
/// Never a substitute for the real thing where the RLS behaviour itself is what is being tested:
/// the live-Postgres integration tests wire up the real
/// <c>ShelfGuard.Infrastructure.Services.ProviderRlsOverride</c>.
/// </summary>
internal sealed class PassThroughProviderRlsOverride : IProviderRlsOverride
{
    public Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken ct = default) => action();
}
