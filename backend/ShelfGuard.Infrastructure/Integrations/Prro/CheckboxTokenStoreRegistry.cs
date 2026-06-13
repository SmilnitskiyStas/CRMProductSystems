using System.Collections.Concurrent;

namespace ShelfGuard.Infrastructure.Integrations.Prro;

/// <summary>
/// Singleton registry of cashier token stores, keyed by tenant + base URL + license key
/// (ADR-013): each tenant's cash register caches its own bearer token, so multi-tenant
/// resolution never leaks or clobbers another register's session. The env-fallback
/// deployment register uses Guid.Empty as the tenant key.
/// </summary>
public sealed class CheckboxTokenStoreRegistry
{
    private readonly ConcurrentDictionary<string, CheckboxTokenStore> _stores =
        new(StringComparer.Ordinal);

    public CheckboxTokenStore GetOrAdd(Guid tenantId, string? baseUrl, string? licenseKey) =>
        _stores.GetOrAdd($"{tenantId:N}|{baseUrl}|{licenseKey}", _ => new CheckboxTokenStore());

    /// <summary>Number of distinct cached register sessions (diagnostics/tests).</summary>
    public int Count => _stores.Count;
}
