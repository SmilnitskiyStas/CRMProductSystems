using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShelfGuard.Application.Features.Pos.Fiscal;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Integrations.Prro;

/// <summary>
/// Per-tenant IFiscalService resolution (ADR-013). Resolution order:
/// 1. tenant's integration_configs row (service='prro', IsEnabled) via IIntegrationRepository
/// 2. deployment-level PRRO__* env vars (env fallback for single-tenant deployments / CI)
/// 3. NoopFiscalService (offline-first — sale succeeds, fiscalization stays pending)
///
/// Singleton: creates short-lived HttpClient per call (stateless transport) while
/// sharing token stores per tenant+register via the singleton CheckboxTokenStoreRegistry.
/// </summary>
public sealed class FiscalServiceFactory : IFiscalServiceFactory
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CheckboxTokenStoreRegistry _registry;
    private readonly IHttpClientFactory _httpFactory;

    /// <inheritdoc/>
    public PrroConnectionConfig? EnvFallback { get; }

    public FiscalServiceFactory(
        IServiceScopeFactory scopeFactory,
        CheckboxTokenStoreRegistry registry,
        IHttpClientFactory httpFactory,
        IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _registry = registry;
        _httpFactory = httpFactory;
        EnvFallback = BuildEnvFallback(config);
    }

    /// <inheritdoc/>
    public async Task<IFiscalService> GetForTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        // Always create a fresh scope so we get a scoped IIntegrationRepository
        // that carries the correct RLS context for this tenantId.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IIntegrationRepository>();

        var row = await repo.GetByServiceAsync(tenantId, "prro", ct);
        if (row is { IsEnabled: true })
        {
            var dbConfig = PrroConnectionConfig.FromJson(row.Config);
            if (dbConfig is { IsCheckbox: true })
                return Create(tenantId, dbConfig);
        }

        // Env fallback — for single-tenant deployments / CI where PRRO__* env vars are set.
        if (EnvFallback is { IsCheckbox: true })
            return Create(Guid.Empty, EnvFallback);

        return new NoopFiscalService();
    }

    /// <inheritdoc/>
    public IFiscalService Create(Guid tenantId, PrroConnectionConfig config)
    {
        var baseUrl = (string.IsNullOrWhiteSpace(config.BaseUrl)
            ? EnvFallback?.BaseUrl
            : config.BaseUrl) ?? "https://api.checkbox.in.ua/api/v1";

        var http = _httpFactory.CreateClient("checkbox");
        http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");

        var opts = new PrroOptions
        {
            Provider = config.Provider,
            BaseUrl = baseUrl,
            LicenseKey = config.LicenseKey?.Trim() ?? string.Empty,
            Cashier = new PrroOptions.CashierOptions
            {
                Login = config.CashierLogin ?? string.Empty,
                Password = config.CashierPassword ?? string.Empty,
                PinCode = config.CashierPinCode ?? string.Empty,
            },
        };

        var tokenStore = _registry.GetOrAdd(tenantId, baseUrl, config.LicenseKey);
        return new CheckboxFiscalClient(
            http,
            Microsoft.Extensions.Options.Options.Create(opts),
            tokenStore);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static PrroConnectionConfig? BuildEnvFallback(IConfiguration config)
    {
        var provider = config["PRRO:PROVIDER"];
        if (!string.Equals(provider, "checkbox", StringComparison.OrdinalIgnoreCase))
            return null;

        return new PrroConnectionConfig(
            Provider: PrroConnectionConfig.ProviderCheckbox,
            BaseUrl: config["PRRO:BASEURL"],
            LicenseKey: config["PRRO:LICENSEKEY"],
            CashierLogin: config["PRRO:CASHIER:LOGIN"],
            CashierPassword: config["PRRO:CASHIER:PASSWORD"],
            CashierPinCode: config["PRRO:CASHIER:PINCODE"]);
    }
}
