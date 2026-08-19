using System.Text.Json.Nodes;
using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Application.Features.MobileConfig;

/// <summary>
/// Single source of truth for the mobile config document's <c>"theme"</c> JSON shape, derived from
/// a <see cref="MobileTheme"/> row (TASK-544). Two callers need this exact mapping and must never
/// drift apart:
/// <list type="bullet">
/// <item><see cref="MobileConfigPublishService"/> composes it into a version's
/// <c>ConfigurationJson.theme</c> at publish time.</item>
/// <item><see cref="MobileConfigPublishedReadService"/> falls back to it (via
/// <see cref="MobileTheme.CreateDefault"/>) only for a published row that predates TASK-544's
/// reconciliation and therefore has no <c>theme</c> key of its own — a defensive compatibility
/// path, not the normal read.</item>
/// </list>
/// Matches the pre-TASK-544 inline shape <c>MobileConfigPublishedReadService.BuildDocument</c> used
/// to build directly from a live <see cref="MobileTheme"/> join.
/// </summary>
internal static class MobileThemeJson
{
    public static JsonObject ToJsonObject(MobileTheme theme) => new()
    {
        ["logoUrl"] = theme.LogoUrl,
        ["colors"] = new JsonObject
        {
            ["primary"] = theme.PrimaryColor,
            ["secondary"] = theme.SecondaryColor,
            ["background"] = theme.BackgroundColor,
            ["surface"] = theme.SurfaceColor,
            ["textPrimary"] = theme.TextPrimaryColor,
            ["textSecondary"] = theme.TextSecondaryColor,
        },
        ["buttons"] = new JsonObject { ["radius"] = theme.ButtonRadius },
        ["cards"] = new JsonObject { ["radius"] = theme.CardRadius },
        ["spacing"] = theme.SpacingPreset,
    };

    /// <summary>
    /// Inverse of <c>MobileConfigPublishService.ComposeTheme</c> (TASK-545 rollback). A published or
    /// archived <see cref="ShelfGuard.Domain.Entities.MobileConfigurationVersion.ConfigurationJson"/>
    /// already has <c>"theme"</c> baked in (composed at that version's original publish time); this
    /// splits it back into the theme node (or <c>null</c> if the row predates the TASK-544
    /// reconciliation and never had one — a legacy/defensive case) and the remaining theme-less body,
    /// exactly the shape <see cref="IMobileConfigValidator"/> accepts. Splitting off <c>"theme"</c>
    /// BEFORE validation matters: <c>MobileConfigValidator</c>'s top-level key whitelist has no
    /// <c>"theme"</c> entry, so validating the composed document as-is would always fail with
    /// "unknown field 'theme'", regardless of whether the historical document is otherwise valid.
    /// </summary>
    public static (JsonObject? Theme, string ThemelessBodyJson) SplitTheme(string composedConfigurationJson)
    {
        var node = JsonNode.Parse(composedConfigurationJson) as JsonObject
            ?? throw new InvalidOperationException(
                "Stored ConfigurationJson root is not a JSON object — this should be unreachable for a " +
                "version that was ever published through MobileConfigPublishService.");

        var themeNode = node["theme"] as JsonObject;
        node.Remove("theme");

        return (themeNode, node.ToJsonString());
    }

    /// <summary>
    /// Applies a historical <paramref name="themeJson"/> node (as produced by <see cref="ToJsonObject"/>
    /// at some earlier publish, and recovered via <see cref="SplitTheme"/>) onto the tenant's live,
    /// mutable <paramref name="theme"/> row — TASK-545 rollback's "restore the theme" step. This is a
    /// full-replace, same as <see cref="MobileTheme.Update"/> itself; every field is expected to be
    /// present because <paramref name="themeJson"/> was always produced by <see cref="ToJsonObject"/>,
    /// which never omits a key (only <c>logoUrl</c> can be JSON <c>null</c>).
    /// </summary>
    public static void ApplyTo(JsonObject themeJson, MobileTheme theme)
    {
        var colors = themeJson["colors"]!.AsObject();
        var buttons = themeJson["buttons"]!.AsObject();
        var cards = themeJson["cards"]!.AsObject();

        theme.Update(
            logoUrl: themeJson["logoUrl"]?.GetValue<string>(),
            primaryColor: colors["primary"]!.GetValue<string>(),
            secondaryColor: colors["secondary"]!.GetValue<string>(),
            backgroundColor: colors["background"]!.GetValue<string>(),
            surfaceColor: colors["surface"]!.GetValue<string>(),
            textPrimaryColor: colors["textPrimary"]!.GetValue<string>(),
            textSecondaryColor: colors["textSecondary"]!.GetValue<string>(),
            buttonRadius: buttons["radius"]!.GetValue<int>(),
            cardRadius: cards["radius"]!.GetValue<int>(),
            spacingPreset: themeJson["spacing"]!.GetValue<string>());
    }
}
