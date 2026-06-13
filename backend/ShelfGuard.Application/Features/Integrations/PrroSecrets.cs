using System.Text.Json.Nodes;

namespace ShelfGuard.Application.Features.Integrations;

/// <summary>
/// Write-only secret handling for the ПРРО integration config (ADR-013 p.4).
/// GET returns masked values; PUT treats a masked placeholder as «keep stored value».
/// Mask format: license key → «••••» + last 4 chars; password/PIN → «••••» only.
/// </summary>
public static class PrroSecrets
{
    public const string MaskToken = "••••";

    /// <summary>JSONB field names of fully-masked secrets (no visible suffix).</summary>
    private static readonly string[] FullyMaskedFields = ["cashier_password", "cashier_pin_code"];

    private const string LicenseKeyField = "license_key";

    /// <summary>null when unset; «••••» (+ last <paramref name="visibleSuffix"/> chars) when set.</summary>
    public static string? Mask(string? secret, int visibleSuffix = 0) =>
        string.IsNullOrEmpty(secret)
            ? null
            : visibleSuffix > 0 && secret.Length > visibleSuffix
                ? MaskToken + secret[^visibleSuffix..]
                : MaskToken;

    public static bool IsMasked(string? value) =>
        value is not null && value.StartsWith(MaskToken, StringComparison.Ordinal);

    /// <summary>
    /// PUT semantics for a secret field: null or masked placeholder → keep stored;
    /// empty string → clear; anything else → new value.
    /// </summary>
    public static string? Merge(string? incoming, string? stored) =>
        incoming is null || IsMasked(incoming) ? stored
        : incoming.Length == 0 ? null
        : incoming;

    /// <summary>Masks ПРРО secret fields in a raw integration config object (generic GET path).</summary>
    public static void MaskInPlace(JsonObject config)
    {
        if (Str(config, LicenseKeyField) is { Length: > 0 } key)
            config[LicenseKeyField] = Mask(key, visibleSuffix: 4);

        foreach (var field in FullyMaskedFields)
        {
            if (Str(config, field) is { Length: > 0 })
                config[field] = MaskToken;
        }
    }

    /// <summary>
    /// Replaces masked placeholders in an incoming raw config with the stored values
    /// (generic PUT path) so a round-tripped masked GET payload never corrupts secrets.
    /// </summary>
    public static void MergeMaskedFromStored(JsonObject incoming, JsonObject? stored)
    {
        foreach (var field in (string[])[LicenseKeyField, .. FullyMaskedFields])
        {
            if (!IsMasked(Str(incoming, field)))
                continue;

            var storedValue = stored is null ? null : Str(stored, field);
            if (storedValue is { Length: > 0 })
                incoming[field] = storedValue;
            else
                incoming.Remove(field);
        }
    }

    private static string? Str(JsonObject obj, string field) =>
        obj[field] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
