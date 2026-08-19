using System.Text.Json.Nodes;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.MobileConfig;

/// <summary>
/// See <see cref="IMobileConfigPreviewService"/>.
///
/// READ-ONLY, NEVER MUTATES (TASK-547): unlike <see cref="MobileConfigDraftService"/>/
/// <see cref="MobileConfigPublishService"/>, this class never calls
/// <see cref="IMobileConfigurationRepository.SaveChangesAsync"/> and never calls any entity
/// mutation method (<c>UpdateConfigurationJson</c>, <c>Publish</c>, <c>SetDraftVersion</c>, etc.).
/// <see cref="IMobileConfigurationRepository.GetByTenantIdAsync"/> returns tracked entities (other
/// callers need that to mutate in place), but that is harmless here — nothing this class does ever
/// calls <c>SaveChangesAsync</c>, so no tracked change is ever persisted.
///
/// TENANT SCOPE: the <c>tenantId</c> passed to <see cref="GetPreviewAsync"/> always comes from the
/// caller's own <c>ITenantContext</c> (see <c>MobileConfigPreviewController</c>) — a staff/admin
/// request reading their OWN tenant's draft, same as
/// <see cref="MobileConfigDraftService"/>. This runs entirely under the calling request's own
/// tenant RLS context; no <c>ITenantSessionOverride</c> needed (that is only for the anonymous,
/// cross-tenant consumer read in <see cref="MobileConfigPublishedReadService"/>).
///
/// THEME COMPOSITION: a draft's <c>ConfigurationJson</c> never carries a <c>"theme"</c> key
/// (TASK-532's invariant, preserved through TASK-544) — theme is normally composed in only at
/// publish time by <c>MobileConfigPublishService.ComposeTheme</c>. An admin previewing the draft
/// needs to see theme too, so this class performs the exact same composition, read-only, reusing
/// <see cref="MobileThemeJson.ToJsonObject"/> — the single source of truth for the theme JSON
/// shape — rather than re-implementing it. Sourced from the tenant's LIVE
/// <see cref="MobileTheme"/> row (via <see cref="MobileConfiguration.Theme"/>), falling back to
/// <see cref="MobileTheme.CreateDefault"/> for a tenant that has never saved a theme yet — same
/// fallback <c>MobileConfigPublishService.PublishAsync</c> itself uses.
/// </summary>
public sealed class MobileConfigPreviewService : IMobileConfigPreviewService
{
    private readonly IMobileConfigurationRepository _repo;
    private readonly ITenantRepository _tenants;

    public MobileConfigPreviewService(IMobileConfigurationRepository repo, ITenantRepository tenants)
    {
        _repo = repo;
        _tenants = tenants;
    }

    public async Task<string> GetPreviewAsync(Guid tenantId, CancellationToken ct = default)
    {
        var config = await _repo.GetByTenantIdAsync(tenantId, ct);
        var tenant = await _tenants.GetByIdAsync(tenantId, ct);

        var draft = config?.DraftVersion;
        var parsedBody = ParseDraftBody(draft?.ConfigurationJson);

        var theme = config?.Theme ?? MobileTheme.CreateDefault(config?.Id ?? Guid.Empty, tenantId);

        var document = BuildDocument(
            hasDraft: draft is not null, parsedBody, theme, tenant, configVersion: draft?.Version ?? 0);

        return document.ToJsonString();
    }

    /// <summary>
    /// A draft's <c>ConfigurationJson</c> is always a validator-approved JSON object once saved
    /// through <see cref="MobileConfigDraftService.SaveDraftAsync"/> — parse failure is
    /// unreachable in real usage. Defensive fallback only (corrupt/legacy row, or no draft at
    /// all): treat as an empty document rather than surfacing a raw 500, same posture
    /// <see cref="MobileConfigPublishedReadService"/> takes for its own parse failure case.
    /// </summary>
    private static JsonObject ParseDraftBody(string? configurationJson)
    {
        if (configurationJson is null)
            return new JsonObject();

        try
        {
            return JsonNode.Parse(configurationJson) as JsonObject ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }

    private static JsonObject BuildDocument(
        bool hasDraft, JsonObject parsedBody, MobileTheme theme, Tenant? tenant, int configVersion) => new()
    {
        ["hasDraft"] = hasDraft,
        ["schemaVersion"] = parsedBody["schemaVersion"]?.DeepClone() ?? JsonValue.Create(MobileConfigWhitelists.CurrentSchemaVersion),
        ["configVersion"] = configVersion,
        ["tenant"] = tenant is null
            ? null
            : new JsonObject
            {
                ["id"] = tenant.Id.ToString(),
                ["slug"] = tenant.Slug,
                ["name"] = tenant.Name,
                ["logoUrl"] = tenant.LogoUrl,
            },
        ["theme"] = MobileThemeJson.ToJsonObject(theme),
        ["features"] = parsedBody["features"]?.DeepClone() ?? new JsonObject(),
        ["navigation"] = parsedBody["navigation"]?.DeepClone() ?? new JsonArray(),
        ["pages"] = parsedBody["pages"]?.DeepClone() ?? new JsonObject(),
    };
}
