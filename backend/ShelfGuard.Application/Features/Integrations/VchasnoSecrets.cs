using System.Text.Json.Nodes;

namespace ShelfGuard.Application.Features.Integrations;

/// <summary>
/// Write-only secret handling for the Вчасно integration config (TASK-317),
/// mirroring <see cref="PrroSecrets"/>: GET masks the API key («••••» + last 4
/// chars); PUT treats a masked placeholder as «keep stored value».
/// </summary>
public static class VchasnoSecrets
{
    private const string ApiKeyField = "api_key";

    /// <summary>Masks the Вчасно API key in a raw integration config object (generic GET path).</summary>
    public static void MaskInPlace(JsonObject config)
    {
        if (Str(config, ApiKeyField) is { Length: > 0 } key)
            config[ApiKeyField] = PrroSecrets.Mask(key, visibleSuffix: 4);
    }

    /// <summary>
    /// Replaces a masked api_key placeholder in an incoming raw config with the
    /// stored value (generic PUT path) so a round-tripped masked GET payload never
    /// corrupts the secret.
    /// </summary>
    public static void MergeMaskedFromStored(JsonObject incoming, JsonObject? stored)
    {
        if (!PrroSecrets.IsMasked(Str(incoming, ApiKeyField)))
            return;

        var storedValue = stored is null ? null : Str(stored, ApiKeyField);
        if (storedValue is { Length: > 0 })
            incoming[ApiKeyField] = storedValue;
        else
            incoming.Remove(ApiKeyField);
    }

    private static string? Str(JsonObject obj, string field) =>
        obj[field] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
