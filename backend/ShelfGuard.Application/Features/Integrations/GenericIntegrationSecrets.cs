using System.Text.Json.Nodes;

namespace ShelfGuard.Application.Features.Integrations;

/// <summary>
/// Write-only secret handling for integrations that don't have a dedicated masking class
/// (claude, telegram, resend, webhook, iot) — mirrors <see cref="PrroSecrets"/> and
/// <see cref="VchasnoSecrets"/>: GET masks the secret field («••••» + last 4 chars); PUT
/// treats a masked placeholder as «keep stored value».
///
/// TASK-347 (security review of ADR-020/TASK-346): before this class existed,
/// <c>IntegrationService.GetByServiceAsync</c> only masked "prro"/"vchasno" — every other
/// service returned its config (including API keys/bot tokens) fully unmasked. That gap was
/// harmless while only <c>AtLeastStoreManager</c>+ roles could reach it, but ADR-020's new
/// "integrations.view" TenantRole capability made the same endpoint reachable by a `staff`-rank
/// (rank 0) user holding an "Accountant"/"HR"-style template — the first low-privilege reader
/// of this data. Field names sourced from `frontend/features/integrations/types.ts`
/// (`SERVICE_META[...].fields` — the ones marked `type: "password"`).
/// </summary>
public static class GenericIntegrationSecrets
{
    /// <summary>Secret JSONB field name per service. Services not listed here have nothing to mask.</summary>
    private static readonly Dictionary<string, string> SecretFieldByService = new()
    {
        ["claude"]   = "api_key",
        ["telegram"] = "bot_token",
        ["resend"]   = "api_key",
        ["webhook"]  = "secret",
        ["iot"]      = "api_key",
    };

    public static bool HasSecretField(string service) => SecretFieldByService.ContainsKey(service);

    /// <summary>Masks the secret field in a raw integration config object (generic GET path). No-op for services without a registered secret field.</summary>
    public static void MaskInPlace(string service, JsonObject config)
    {
        if (!SecretFieldByService.TryGetValue(service, out var field))
            return;

        if (Str(config, field) is { Length: > 0 } secret)
            config[field] = PrroSecrets.Mask(secret, visibleSuffix: 4);
    }

    /// <summary>
    /// Replaces a masked placeholder in an incoming raw config with the stored value (generic
    /// PUT path) so a round-tripped masked GET payload never corrupts the secret. No-op for
    /// services without a registered secret field.
    /// </summary>
    public static void MergeMaskedFromStored(string service, JsonObject incoming, JsonObject? stored)
    {
        if (!SecretFieldByService.TryGetValue(service, out var field))
            return;

        if (!PrroSecrets.IsMasked(Str(incoming, field)))
            return;

        var storedValue = stored is null ? null : Str(stored, field);
        if (storedValue is { Length: > 0 })
            incoming[field] = storedValue;
        else
            incoming.Remove(field);
    }

    private static string? Str(JsonObject obj, string field) =>
        obj[field] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
