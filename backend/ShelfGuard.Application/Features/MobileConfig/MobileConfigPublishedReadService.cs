using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using ShelfGuard.Application.Features.MobileConfig.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.MobileConfig;

/// <summary>
/// See <see cref="IMobileConfigPublishedReadService"/>.
///
/// THEME-COMPOSITION RECONCILIATION (TASK-544, resolving the sequencing note this class used to
/// carry): TASK-532 decided a draft <c>MobileConfigurationVersion.ConfigurationJson</c> never
/// carries a <c>"theme"</c> key, and that theme is composed into <c>ConfigurationJson</c> only at
/// publish time. That publish flow now exists — <c>MobileConfigPublishService</c> — so this
/// service was switched from option (a) (live join against <see cref="MobileTheme"/> on every
/// call) to option (b): it trusts <c>PublishedVersion.ConfigurationJson.theme</c>, the exact
/// snapshot <c>MobileConfigPublishService</c> composed at the moment of that publish. This brings
/// theme under the same "nothing changes until publish" guarantee pages/navigation/features
/// already had — a <c>PUT /api/v1/mobile/theme</c> (TASK-536) call no longer takes effect for real
/// consumers until the tenant publishes again.
///
/// A published row's <c>ConfigurationJson</c> missing a <c>"theme"</c> key at all (only possible
/// for a row published before this reconciliation shipped, or seeded directly by a test) falls
/// back to <see cref="MobileTheme.CreateDefault"/>'s hardcoded defaults via
/// <see cref="MobileThemeJson"/> — never to a live join, since <c>GetPublishedByTenantIdAsync</c>
/// deliberately no longer includes <see cref="MobileTheme"/> at all. This is a defensive
/// compatibility path only, not the normal read.
///
/// TENANT-ISOLATION NOTE: <see cref="MobileConfiguration"/>/<see cref="MobileConfigurationVersion"/>/
/// <see cref="MobileTheme"/> all carry the tenant_isolation/provider_bypass/worker_bypass RLS
/// triad (TASK-531), and the caller of this service is anonymous/consumer-shaped (no
/// <c>app.tenant_id</c> claim — see <c>TenantSessionOverride</c> remarks), so every read here runs
/// through <see cref="ITenantSessionOverride"/> exactly like
/// <c>ConsumerContentService</c>/<c>ConsumerContentController</c> already do for the same reason.
/// </summary>
public sealed class MobileConfigPublishedReadService : IMobileConfigPublishedReadService
{
    private readonly IMobileConfigurationRepository _repo;
    private readonly ITenantRepository _tenants;
    private readonly ITenantSessionOverride _tenantScope;

    public MobileConfigPublishedReadService(
        IMobileConfigurationRepository repo, ITenantRepository tenants, ITenantSessionOverride tenantScope)
    {
        _repo = repo;
        _tenants = tenants;
        _tenantScope = tenantScope;
    }

    public async Task<(string? DocumentJson, string? ETag, MobileConfigReadError? Error)> GetPublishedConfigAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        // Tenants carries no RLS at all (same precedent ConsumerContentService documents), so this
        // existence check runs before any ITenantSessionOverride is needed.
        var tenant = await _tenants.GetByIdAsync(tenantId, ct);
        if (tenant is null)
            return (null, null, new MobileConfigReadError(MobileConfigReadErrorType.TenantNotFound, "Tenant not found."));

        var config = await _tenantScope.ExecuteAsync(
            tenantId, () => _repo.GetPublishedByTenantIdAsync(tenantId, ct), ct);

        var publishedVersion = config?.PublishedVersion;
        if (publishedVersion is null || publishedVersion.Status != MobileConfigurationVersionStatus.Published)
            return (null, null, NoPublishedConfigurationError);

        JsonObject parsedBody;
        try
        {
            parsedBody = JsonNode.Parse(publishedVersion.ConfigurationJson) as JsonObject
                ?? throw new InvalidOperationException("Stored ConfigurationJson root is not a JSON object.");
        }
        catch (Exception)
        {
            // Stored ConfigurationJson is always a validator-approved object once a real publish
            // flow (TASK-544) exists — this is a defensive fallback only (corrupt/legacy row),
            // reported the same as "not published yet" rather than surfacing a raw 500.
            return (null, null, NoPublishedConfigurationError);
        }

        var documentJson = BuildDocument(tenant, publishedVersion, parsedBody, config!.Id, tenantId).ToJsonString();
        var etag = ComputeETag(documentJson);

        return (documentJson, etag, null);
    }

    private static readonly MobileConfigReadError NoPublishedConfigurationError = new(
        MobileConfigReadErrorType.NoPublishedConfiguration, "This tenant has no published mobile configuration yet.");

    private static JsonObject BuildDocument(
        Tenant tenant, MobileConfigurationVersion version, JsonObject parsedBody,
        Guid mobileConfigurationId, Guid tenantId) => new()
    {
        ["schemaVersion"] = ClonedOrDefault(parsedBody, "schemaVersion", MobileConfigWhitelists.CurrentSchemaVersion),
        ["configVersion"] = version.Version,
        ["tenant"] = new JsonObject
        {
            ["id"] = tenant.Id.ToString(),
            ["slug"] = tenant.Slug,
            ["name"] = tenant.Name,
            ["logoUrl"] = tenant.LogoUrl,
        },
        ["theme"] = ResolveTheme(parsedBody, mobileConfigurationId, tenantId),
        ["features"] = parsedBody["features"]?.DeepClone() ?? new JsonObject(),
        ["navigation"] = parsedBody["navigation"]?.DeepClone() ?? new JsonArray(),
        ["pages"] = parsedBody["pages"]?.DeepClone() ?? new JsonObject(),
    };

    /// <summary>
    /// Normal path: the published document's own <c>theme</c> key, snapshotted by
    /// <c>MobileConfigPublishService</c> at publish time. Defensive fallback: a published row that
    /// predates the TASK-544 reconciliation (or was seeded directly by a test) has no such key —
    /// use <see cref="MobileTheme.CreateDefault"/>'s hardcoded defaults instead of a live join.
    /// </summary>
    private static JsonNode ResolveTheme(JsonObject parsedBody, Guid mobileConfigurationId, Guid tenantId) =>
        parsedBody["theme"] is JsonObject themeNode
            ? themeNode.DeepClone()
            : MobileThemeJson.ToJsonObject(MobileTheme.CreateDefault(mobileConfigurationId, tenantId));

    private static JsonNode ClonedOrDefault(JsonObject parsedBody, string key, int fallback) =>
        parsedBody[key]?.DeepClone() ?? JsonValue.Create(fallback)!;

    /// <summary>
    /// Strong ETag over the exact served bytes (not just the version's identity) — deliberately
    /// covers the composed theme too. A theme edit alone (TASK-536, no republish) no longer changes
    /// this response at all post-TASK-544 (theme is now sourced from the published snapshot, not
    /// live), so the ETag correctly stays stable in that case; it still changes whenever a new
    /// publish actually changes the served document.
    /// </summary>
    private static string ComputeETag(string documentJson)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(documentJson));
        return $"\"{Convert.ToHexString(hash)}\"";
    }
}
