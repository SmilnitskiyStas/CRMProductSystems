using System.Text.Json;

namespace ShelfGuard.Application.Features.Pos.Fiscal;

/// <summary>
/// Provider-agnostic ПРРО connection settings (ADR-013). Stored per tenant in
/// integration_configs (service='prro', JSONB snake_case: provider, base_url,
/// license_key, cashier_login, cashier_password, cashier_pin_code).
/// </summary>
public sealed record PrroConnectionConfig(
    string Provider,
    string? BaseUrl = null,
    string? LicenseKey = null,
    string? CashierLogin = null,
    string? CashierPassword = null,
    string? CashierPinCode = null)
{
    public const string ProviderCheckbox = "checkbox";
    public const string ProviderDisabled = "disabled";

    public bool IsCheckbox =>
        string.Equals(Provider, ProviderCheckbox, StringComparison.OrdinalIgnoreCase);

    /// <summary>Parses the integration_configs JSONB blob. Null on malformed JSON or missing provider.</summary>
    public static PrroConnectionConfig? FromJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            var provider = Str(doc.RootElement, "provider");
            if (string.IsNullOrWhiteSpace(provider))
                return null;

            return new PrroConnectionConfig(
                Provider: provider!,
                BaseUrl: Str(doc.RootElement, "base_url"),
                LicenseKey: Str(doc.RootElement, "license_key"),
                CashierLogin: Str(doc.RootElement, "cashier_login"),
                CashierPassword: Str(doc.RootElement, "cashier_password"),
                CashierPinCode: Str(doc.RootElement, "cashier_pin_code"));
        }
        catch (JsonException)
        {
            return null;
        }

        static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(p.GetString()) ? p.GetString() : null;
    }

    /// <summary>Serializes to the integration_configs JSONB shape (snake_case, ADR-013).</summary>
    public string ToJson()
    {
        var map = new Dictionary<string, string?>
        {
            ["provider"] = Provider,
            ["base_url"] = BaseUrl,
            ["license_key"] = LicenseKey,
            ["cashier_login"] = CashierLogin,
            ["cashier_password"] = CashierPassword,
            ["cashier_pin_code"] = CashierPinCode,
        };
        return JsonSerializer.Serialize(
            map.Where(kv => !string.IsNullOrEmpty(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value));
    }
}

/// <summary>
/// Per-tenant fiscal provider resolution (ADR-013). Replaces the startup-time
/// PRRO:PROVIDER DI switch: consumers (POS endpoints, fiscalization retry job)
/// resolve IFiscalService through this factory at call time, never via direct DI.
/// </summary>
public interface IFiscalServiceFactory
{
    /// <summary>
    /// Resolves the fiscal provider for a specific tenant. Resolution order:
    /// integration_configs row (service='prro', IsEnabled) →
    /// deployment-level PRRO__* env fallback → NoopFiscalService.
    /// </summary>
    Task<IFiscalService> GetForTenantAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Builds a fiscal client from an explicit config (settings «test connection»
    /// flow — exercises submitted-but-not-yet-saved credentials).
    /// </summary>
    IFiscalService Create(Guid tenantId, PrroConnectionConfig config);

    /// <summary>Deployment-level PRRO__* env config, or null when env is not configured.</summary>
    PrroConnectionConfig? EnvFallback { get; }
}
