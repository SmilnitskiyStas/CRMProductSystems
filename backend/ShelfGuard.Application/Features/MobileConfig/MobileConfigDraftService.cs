using System.Text.Json;
using ShelfGuard.Application.Features.MobileConfig.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.MobileConfig;

/// <summary>
/// See <see cref="IMobileConfigDraftService"/>.
///
/// Design decision (TASK-532): draft edits mutate <see cref="MobileConfigurationVersion"/> in
/// place via <c>UpdateConfigurationJson</c> rather than creating a new draft row per save — the
/// same shape as <c>Banner.Update()</c> staying separate from <c>Banner.Publish()</c>. A tenant
/// has at most one draft row at a time; a fresh append-only version is only minted once the draft
/// is actually published (TASK-544), which is when <see cref="MobileConfigurationVersion.Version"/>
/// truly needs to advance for history/rollback purposes. Saving a work-in-progress draft five
/// times does not need five version numbers.
/// </summary>
public sealed class MobileConfigDraftService : IMobileConfigDraftService
{
    private readonly IMobileConfigurationRepository _repo;
    private readonly IMobileConfigValidator _validator;
    private readonly IActivityLogRepository _activityLogs;

    public MobileConfigDraftService(
        IMobileConfigurationRepository repo, IMobileConfigValidator validator, IActivityLogRepository activityLogs)
    {
        _repo = repo;
        _validator = validator;
        _activityLogs = activityLogs;
    }

    public async Task<(MobileConfigDraftDto? Draft, IReadOnlyList<MobileConfigValidationError> Errors)> SaveDraftAsync(
        Guid tenantId, Guid? actingUserId, string configurationJson, CancellationToken ct = default)
    {
        var validation = _validator.Validate(configurationJson);
        if (!validation.IsValid)
            return (null, validation.Errors);

        // Already confirmed well-formed JSON with an integer schemaVersion by the validator above.
        var schemaVersion = JsonDocument.Parse(configurationJson).RootElement
            .GetProperty("schemaVersion").GetInt32();

        var config = await _repo.GetByTenantIdAsync(tenantId, ct);
        if (config is null)
        {
            config = MobileConfiguration.Create(tenantId);
            await _repo.AddAsync(config, ct);
        }

        // Captured BEFORE any mutation below — config.DraftVersion and the local draftVersion
        // variable become the SAME tracked object in the update-in-place branch, so this must be
        // read first or the "previous" value would already be overwritten (TASK-550 feature-flag
        // diff, see LogDraftSavedAsync).
        var previousJson = config.DraftVersion?.ConfigurationJson;

        MobileConfigurationVersion draftVersion;
        if (config.DraftVersion is not null)
        {
            draftVersion = config.DraftVersion;
            draftVersion.UpdateConfigurationJson(configurationJson);
            _repo.UpdateVersion(draftVersion);
        }
        else
        {
            var nextVersion = await _repo.GetMaxVersionNumberAsync(tenantId, ct) + 1;
            draftVersion = MobileConfigurationVersion.Create(
                mobileConfigurationId: config.Id,
                tenantId: tenantId,
                version: nextVersion,
                schemaVersion: schemaVersion,
                configurationJson: configurationJson,
                createdBy: actingUserId);
            await _repo.AddVersionAsync(draftVersion, ct);

            // Insert the new version row (and the new MobileConfiguration root row, if this is
            // the tenant's very first draft) BEFORE pointing MobileConfiguration.DraftVersionId
            // at it. MobileConfiguration.DraftVersionId and MobileConfigurationVersion.
            // MobileConfigurationId form a genuine row-level FK cycle when both rows are brand
            // new ("Added") entities in the same SaveChangesAsync call — EF Core throws "circular
            // dependency detected" against real Postgres (TASK-531's intentional
            // root+versions-with-pointer shape; found in TASK-534 — the mocked-repository unit
            // tests in MobileConfigDraftServiceTests never exercise real EF SaveChanges, so this
            // went uncaught). Two SaveChangesAsync calls — insert first, then point — same shape
            // MobileConfigPublishedReadRlsIntegrationTests' seeding helpers already use.
            await _repo.SaveChangesAsync(ct);

            config.SetDraftVersion(draftVersion.Id);
            _repo.Update(config);
        }

        await _repo.SaveChangesAsync(ct);

        await LogDraftSavedAsync(tenantId, actingUserId, draftVersion, previousJson, ct);

        return (ToDto(draftVersion), []);
    }

    public async Task<MobileConfigDraftDto?> GetDraftAsync(Guid tenantId, CancellationToken ct = default)
    {
        var config = await _repo.GetByTenantIdAsync(tenantId, ct);
        return config?.DraftVersion is null ? null : ToDto(config.DraftVersion);
    }

    // ── Audit logging (TASK-550) ────────────────────────────────────────────
    //
    // Two distinct ActivityLog entries per successful save, same pattern LoyaltyService already
    // established (one generic event, plus an extra specific one only when it applies):
    //   1. "mobileconfig.draft_saved" — always, one per save.
    //   2. "mobileconfig.feature_flags_changed" — only when the `features` object actually
    //      differs from the PREVIOUS draft content, and only from the second save onward (a
    //      tenant's very first-ever draft has nothing to diff against — its "features" are being
    //      created, not "changed"). This intentionally lives here, not in
    //      MobileConfigPublishService: Publish/Rollback never let an admin edit document content —
    //      they only promote/restore whatever a prior SaveDraftAsync call already produced — so
    //      SaveDraftAsync is the only place feature-flag VALUES can actually change.

    private async Task LogDraftSavedAsync(
        Guid tenantId, Guid? actingUserId, MobileConfigurationVersion draftVersion,
        string? previousJson, CancellationToken ct)
    {
        await _activityLogs.LogAsync(new ActivityLog
        {
            TenantId = tenantId,
            UserId = actingUserId,
            Action = "mobileconfig.draft_saved",
            EntityType = "mobile_configuration_version",
            EntityId = draftVersion.Id,
            Meta = $"{{\"version\":{draftVersion.Version}}}",
        }, ct);

        if (previousJson is not null)
        {
            var changedFlags = DiffFeatureFlags(previousJson, draftVersion.ConfigurationJson);
            if (changedFlags.Count > 0)
            {
                await _activityLogs.LogAsync(new ActivityLog
                {
                    TenantId = tenantId,
                    UserId = actingUserId,
                    Action = "mobileconfig.feature_flags_changed",
                    EntityType = "mobile_configuration_version",
                    EntityId = draftVersion.Id,
                    Meta = JsonSerializer.Serialize(changedFlags),
                }, ct);
            }
        }

        await _activityLogs.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Keys present in the NEW document whose boolean value differs from the previous document (or
    /// is newly present). A key that disappears entirely between saves is not reported — every
    /// draft carries a full `features` object by construction (the validator requires the key,
    /// <see cref="MobileConfigWhitelists.FeatureKeys"/> is the closed set of names it may contain),
    /// so silent removal without a replacement value is not a real-world shape worth a separate
    /// code path here.
    /// </summary>
    private static IReadOnlyDictionary<string, bool> DiffFeatureFlags(string previousJson, string currentJson)
    {
        var before = ExtractFeatureFlags(previousJson);
        var after = ExtractFeatureFlags(currentJson);

        var changed = new Dictionary<string, bool>();
        foreach (var (key, afterValue) in after)
        {
            if (!before.TryGetValue(key, out var beforeValue) || beforeValue != afterValue)
                changed[key] = afterValue;
        }
        return changed;
    }

    private static IReadOnlyDictionary<string, bool> ExtractFeatureFlags(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("features", out var featuresEl) ||
                featuresEl.ValueKind != JsonValueKind.Object)
                return new Dictionary<string, bool>();

            var result = new Dictionary<string, bool>();
            foreach (var prop in featuresEl.EnumerateObject())
            {
                if (prop.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    result[prop.Name] = prop.Value.GetBoolean();
            }
            return result;
        }
        catch (JsonException)
        {
            // Defensive only — both callers pass content that already passed MobileConfigValidator
            // at its own save time. Never surface a 500 from an audit-logging side path.
            return new Dictionary<string, bool>();
        }
    }

    private static MobileConfigDraftDto ToDto(MobileConfigurationVersion version) => new(
        version.MobileConfigurationId,
        version.Id,
        version.Version,
        version.SchemaVersion,
        version.Status,
        version.ConfigurationJson,
        version.CreatedBy,
        version.CreatedAt);
}
