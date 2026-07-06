using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ShelfGuard.Application.Features.Marketplace.Vchasno;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Integrations.Vchasno;

/// <summary>
/// Per-tenant IVchasnoClient resolution (TASK-317) — mirrors FiscalServiceFactory
/// (ADR-013). Reads the tenant's integration_configs row (service = "vchasno",
/// config: { "api_key": "...", "base_url"?: "..." }); returns null when the
/// integration is absent, disabled, or has no api_key.
/// Singleton: short-lived HttpClient per call from the named "vchasno" pool.
/// </summary>
public sealed class VchasnoClientFactory : IVchasnoClientFactory
{
    public const string ServiceName = "vchasno";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpFactory;

    public VchasnoClientFactory(IServiceScopeFactory scopeFactory, IHttpClientFactory httpFactory)
    {
        _scopeFactory = scopeFactory;
        _httpFactory = httpFactory;
    }

    public async Task<IVchasnoClient?> GetForTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        // Fresh scope → scoped IIntegrationRepository with the correct RLS context.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IIntegrationRepository>();

        var row = await repo.GetByServiceAsync(tenantId, ServiceName, ct);
        if (row is not { IsEnabled: true })
            return null;

        var (apiKey, baseUrl) = ParseConfig(row.Config);
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        return new VchasnoClient(_httpFactory.CreateClient(ServiceName), apiKey, baseUrl);
    }

    private static (string? ApiKey, string? BaseUrl) ParseConfig(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null);

            return (Str(root, "api_key"), Str(root, "base_url"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string? Str(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
