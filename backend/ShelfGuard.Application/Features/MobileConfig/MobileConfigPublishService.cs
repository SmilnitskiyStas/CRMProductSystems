using System.Text.Json.Nodes;
using ShelfGuard.Application.Features.MobileConfig.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Exceptions;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.MobileConfig;

/// <summary>
/// See <see cref="IMobileConfigPublishService"/>. Closes out the reconciliation TASK-532/534/536/543
/// each explicitly deferred here:
/// <list type="bullet">
/// <item>TASK-532 decided a draft's <c>ConfigurationJson</c> never carries a <c>"theme"</c> key,
/// and that theme is composed in only at publish time — this class is that composition, and it
/// deliberately validates the draft's ORIGINAL (theme-less) JSON via <see cref="IMobileConfigValidator"/>
/// BEFORE adding <c>"theme"</c>, because <see cref="MobileConfigValidator"/>'s top-level key
/// whitelist does not include <c>theme</c> — composing first would make every publish fail
/// validation.</item>
/// <item><c>MobileConfigPublishedReadService</c> (TASK-534) used to read <c>theme</c> LIVE off the
/// tenant's <see cref="MobileTheme"/> row on every call, explicitly flagged as a stopgap pending
/// this task. It now reads <c>ConfigurationJson.theme</c> from the published version this class
/// writes instead — see that service's remarks for the other half of the reconciliation.</item>
/// <item><c>MobileThemeService</c>/<c>MobileThemeController</c> (TASK-536) documented every
/// <c>PUT /api/v1/mobile/theme</c> as taking effect in production immediately, pending this task.
/// That is no longer true: <see cref="MobileTheme"/> is now the tenant's PENDING theme state —
/// composed into the document only at the next publish. This class does not touch
/// <c>MobileThemeService</c>/<c>MobileThemeController</c> themselves; only how their one live row
/// gets consumed changes.</item>
/// </list>
///
/// DRAFT-CONTINUITY (property #2): <see cref="MobileConfigDraftService.SaveDraftAsync"/> mutates
/// <c>MobileConfiguration.DraftVersion</c> in place. If Publish left <c>DraftVersionId</c> pointing
/// at the row it just flipped to Published, the very next <c>SaveDraftAsync</c> call would illegally
/// mutate published, historical content (<see cref="MobileConfigurationVersion.UpdateConfigurationJson"/>
/// throws for a non-Draft version, but only once someone notices — the real risk is silent
/// corruption if that guard were ever bypassed). So this class always clones the just-published
/// version's ORIGINAL (theme-less) content forward into a brand-new Draft row and repoints
/// <c>DraftVersionId</c> at it — the next edit starts from exactly what was just published, and the
/// draft invariant ("no theme key") holds by construction rather than by stripping one back out.
///
/// CONCURRENCY: relies on the xmin optimistic-concurrency tokens now on <see cref="MobileConfiguration"/>
/// and <see cref="MobileConfigurationVersion"/> (same mechanism as <c>ProductStock</c>/
/// <c>LoyaltyMembership</c> — see <c>AppDbContext</c> remarks), plus the pre-existing unique index on
/// <c>(MobileConfigurationId, Version)</c> as a second line of defense against two concurrent
/// publishes computing the same next version number. Every mutation in <see cref="PublishAsync"/>
/// (mark-published, insert-new-draft, repoint pointers) happens on one <see cref="IMobileConfigurationRepository.SaveChangesAsync"/>
/// call, so it is atomic — a losing concurrent writer's entire batch rolls back together, never
/// partially applying. The previous published version, if any, is archived (see TASK-545 below) —
/// only <c>MobileConfiguration.PublishedVersionId</c> moves to point elsewhere; nothing about that
/// row's stored content changes.
///
/// TASK-545 (Version History + Rollback) closes out the "how superseded versions are represented"
/// decision this class explicitly deferred:
/// <list type="bullet">
/// <item><b>Archiving:</b> whenever a publish (normal or rollback) supersedes a previously-published
/// version, that old version is flipped <c>published → archived</c> via
/// <see cref="MobileConfigurationVersion.Archive"/> — in the SAME atomic <c>SaveChangesAsync</c> call
/// as everything else, never left dangling as <c>Published</c> forever. See <see cref="PublishVersionAsync"/>.</item>
/// <item><b>Rollback</b> (<see cref="RollbackAsync"/>) publishes a NEW version cloned from a
/// historical one — never a destructive revert; the historical version itself is never mutated
/// (except that if it happens to be the current published version, that case is rejected outright —
/// see <see cref="MobileConfigPublishErrorType.CannotRollbackToCurrentVersion"/>).</item>
/// <item><b>VALIDATION-BYPASS REASONING (rollback):</b> a historical published/archived version's
/// <c>ConfigurationJson</c> already has <c>"theme"</c> composed in — feeding that whole document
/// through <see cref="IMobileConfigValidator"/> as-is would always fail ("unknown field 'theme'"),
/// even for a document that was perfectly valid at its original publish time, because the
/// validator's top-level whitelist has no <c>theme</c> entry (TASK-532's decision, matching every
/// draft's shape). <see cref="RollbackAsync"/> therefore splits <c>theme</c> out FIRST
/// (<c>MobileThemeJson.SplitTheme</c>) and only then validates the remaining theme-less body — the
/// exact shape the validator has always accepted. That re-validation is technically redundant in
/// real usage (the theme-less body already passed this same validator once, at the target version's
/// original publish time, and the document shape cannot have changed since a
/// <see cref="MobileConfigurationVersion"/> is immutable once non-Draft) — it is kept anyway as cheap
/// defense-in-depth against a future stricter <see cref="IMobileConfigValidator"/> revision rejecting
/// an old document that was valid under the rules in force when it was first published; it can never
/// re-trigger the "unknown field 'theme'" rejection because theme is already split out by the time
/// this validation call happens.</item>
/// <item><b>THEME-RESTORATION REQUIREMENT (rollback):</b> the historical <c>theme</c> node extracted
/// above is written onto the tenant's LIVE <see cref="MobileTheme"/> row (<c>MobileThemeJson.ApplyTo</c>)
/// BEFORE publishing — not merely composed into the new published document. This is required, not
/// cosmetic: <see cref="PublishAsync"/> always composes a version's theme from whatever
/// <see cref="MobileTheme"/> currently holds. Skipping this step would mean the very next NORMAL
/// publish silently overwrites the rollback's restored theme with the tenant's pre-rollback
/// <see cref="MobileTheme"/> values, partially undoing the rollback the admin just performed.</item>
/// </list>
/// </summary>
public sealed class MobileConfigPublishService : IMobileConfigPublishService
{
    private readonly IMobileConfigurationRepository _repo;
    private readonly IMobileConfigValidator _validator;
    private readonly IActivityLogRepository _activityLogs;

    public MobileConfigPublishService(
        IMobileConfigurationRepository repo, IMobileConfigValidator validator, IActivityLogRepository activityLogs)
    {
        _repo = repo;
        _validator = validator;
        _activityLogs = activityLogs;
    }

    public async Task<(MobileConfigPublishedDto? Published, MobileConfigPublishError? Error)> PublishAsync(
        Guid tenantId, Guid? actingUserId, CancellationToken ct = default)
    {
        var config = await _repo.GetByTenantIdAsync(tenantId, ct);
        if (config?.DraftVersion is null)
        {
            return (null, new MobileConfigPublishError(
                MobileConfigPublishErrorType.NoDraftToPublish,
                "This tenant has no draft to publish.",
                []));
        }

        var draft = config.DraftVersion;
        var originalDraftJson = draft.ConfigurationJson;

        // Validate the ORIGINAL, theme-less draft JSON — MobileConfigValidator's top-level key
        // whitelist has no "theme" entry, so this must happen before composition below.
        var validation = _validator.Validate(originalDraftJson);
        if (!validation.IsValid)
        {
            return (null, new MobileConfigPublishError(
                MobileConfigPublishErrorType.ValidationFailed,
                "Draft configuration failed validation and was not published.",
                validation.Errors));
        }

        var theme = config.Theme ?? MobileTheme.CreateDefault(config.Id, tenantId);

        var result = await PublishVersionAsync(
            config, tenantId, draft, originalDraftJson, draft.SchemaVersion, theme, actingUserId, ct);

        if (result.Published is not null)
            await LogPublishedAsync(tenantId, actingUserId, result.Published, ct);

        return result;
    }

    public async Task<(MobileConfigPublishedDto? Published, MobileConfigPublishError? Error)> RollbackAsync(
        Guid tenantId, Guid targetVersionId, Guid? actingUserId, CancellationToken ct = default)
    {
        var config = await _repo.GetByTenantIdAsync(tenantId, ct);
        if (config is null)
        {
            return (null, new MobileConfigPublishError(
                MobileConfigPublishErrorType.VersionNotFound,
                "This tenant has no mobile configuration to roll back.",
                []));
        }

        if (targetVersionId == config.PublishedVersionId || targetVersionId == config.DraftVersionId)
        {
            return (null, new MobileConfigPublishError(
                MobileConfigPublishErrorType.CannotRollbackToCurrentVersion,
                "The selected version is already the tenant's current published or draft version.",
                []));
        }

        var target = await _repo.GetVersionByIdAsync(tenantId, targetVersionId, ct);
        if (target is null)
        {
            return (null, new MobileConfigPublishError(
                MobileConfigPublishErrorType.VersionNotFound,
                "The selected version does not exist for this tenant.",
                []));
        }

        // Inverse of ComposeTheme below — MUST split "theme" out before any validation call, or
        // MobileConfigValidator rejects every historical document with "unknown field 'theme'".
        var (themeNode, themelessBodyJson) = MobileThemeJson.SplitTheme(target.ConfigurationJson);

        // Defensive re-validation of the now theme-less body — see class remarks
        // "VALIDATION-BYPASS REASONING (rollback)" for why this is expected to always pass in real
        // usage, and why re-running it here is still cheap and safe (theme is already split out).
        var validation = _validator.Validate(themelessBodyJson);
        if (!validation.IsValid)
        {
            return (null, new MobileConfigPublishError(
                MobileConfigPublishErrorType.ValidationFailed,
                "The selected historical version no longer passes validation and cannot be restored.",
                validation.Errors));
        }

        if (config.DraftVersion is null)
        {
            // Invariant: any tenant with a rollback-able history has published at least once, and
            // PublishAsync/RollbackAsync always leave a fresh Draft row behind (property #2) — so a
            // tenant that reaches this point always has a current draft row. Not a user-facing error.
            throw new InvalidOperationException(
                "Cannot roll back: tenant has no current draft row to repurpose as the newly published version.");
        }

        // THEME-RESTORATION REQUIREMENT — see class remarks. Must happen before PublishVersionAsync,
        // which composes from the resolved `theme` instance below — passed explicitly rather than
        // re-derived from `config.Theme` inside the shared helper, so this exact (possibly
        // just-created, not-yet-flushed) instance is guaranteed to be what gets composed, with no
        // dependency on EF Core's navigation-fixup timing for a newly Added entity.
        MobileTheme theme;
        var hadExistingTheme = config.Theme is not null;
        if (themeNode is not null)
        {
            theme = config.Theme ?? MobileTheme.CreateDefault(config.Id, tenantId);
            MobileThemeJson.ApplyTo(themeNode, theme);
            if (hadExistingTheme)
                _repo.UpdateTheme(theme);
            else
                await _repo.AddThemeAsync(theme, ct);
        }
        else
        {
            // Target predates the TASK-544 theme-composition reconciliation and never had a theme
            // key at all — defensive legacy path only. Leave the tenant's live MobileTheme (or the
            // ephemeral default) untouched, same fallback PublishAsync itself uses.
            theme = config.Theme ?? MobileTheme.CreateDefault(config.Id, tenantId);
        }

        var result = await PublishVersionAsync(
            config, tenantId, config.DraftVersion, themelessBodyJson, target.SchemaVersion, theme, actingUserId, ct);

        if (result.Published is not null)
            await LogRolledBackAsync(tenantId, actingUserId, result.Published, target, ct);

        return result;
    }

    /// <summary>
    /// Shared tail for both <see cref="PublishAsync"/> and <see cref="RollbackAsync"/> (TASK-545) —
    /// archive-previous / compose-theme / mark-published / clone-new-draft / repoint-pointers /
    /// atomic-save, all in one <see cref="IMobileConfigurationRepository.SaveChangesAsync"/> call.
    /// <paramref name="draftRowToPublish"/> is the row that gets mutated in place into the new
    /// published version (its OWN version number is reused, never a freshly-allocated one) — for a
    /// normal publish this is the tenant's current draft with its own edits; for a rollback this is
    /// also the tenant's current draft row, but repurposed to carry the restored historical content
    /// instead — same "one mutable draft slot becomes published, then a fresh draft is cloned
    /// forward" mechanics either way. Caller is responsible for validating
    /// <paramref name="themelessBodyJson"/> BEFORE calling this, and for resolving
    /// <paramref name="theme"/> itself (rather than this helper re-deriving it from
    /// <c>config.Theme</c>) — <see cref="RollbackAsync"/> may pass a theme instance it just
    /// created/mutated in this same unit of work, which is not guaranteed to be reachable through
    /// <c>config.Theme</c>'s navigation property yet.
    /// </summary>
    private async Task<(MobileConfigPublishedDto? Published, MobileConfigPublishError? Error)> PublishVersionAsync(
        MobileConfiguration config, Guid tenantId, MobileConfigurationVersion draftRowToPublish,
        string themelessBodyJson, int schemaVersion, MobileTheme theme, Guid? actingUserId, CancellationToken ct)
    {
        // Archive whatever is currently published — it is about to be superseded. No-op on a
        // tenant's very first publish (PublishedVersionId is still null).
        if (config.PublishedVersionId is { } previousPublishedId)
        {
            var previousPublished = await _repo.GetVersionByIdAsync(tenantId, previousPublishedId, ct);
            if (previousPublished is not null)
            {
                previousPublished.Archive();
                _repo.UpdateVersion(previousPublished);
            }
        }

        var composedJson = ComposeTheme(themelessBodyJson, theme);

        var nextVersionNumber = await _repo.GetMaxVersionNumberAsync(tenantId, ct) + 1;
        var newDraft = MobileConfigurationVersion.Create(
            mobileConfigurationId: config.Id,
            tenantId: tenantId,
            version: nextVersionNumber,
            schemaVersion: schemaVersion,
            configurationJson: themelessBodyJson, // property #2 — no "theme" key, exactly what was validated
            createdBy: actingUserId);

        // UpdateConfigurationJson requires Status == Draft, so set the composed content before
        // flipping status.
        draftRowToPublish.UpdateConfigurationJson(composedJson);
        var utcNow = DateTime.UtcNow;
        draftRowToPublish.Publish(utcNow);

        await _repo.AddVersionAsync(newDraft, ct);
        _repo.UpdateVersion(draftRowToPublish);

        config.SetPublishedVersion(draftRowToPublish.Id);
        config.SetDraftVersion(newDraft.Id);
        _repo.Update(config);

        try
        {
            await _repo.SaveChangesAsync(ct);
        }
        catch (ConcurrencyConflictException)
        {
            return (null, new MobileConfigPublishError(
                MobileConfigPublishErrorType.ConcurrentPublish,
                "Another publish is already in progress for this tenant. Please retry.",
                []));
        }

        return (new MobileConfigPublishedDto(
            config.Id,
            draftRowToPublish.Id,
            draftRowToPublish.Version,
            draftRowToPublish.SchemaVersion,
            draftRowToPublish.ConfigurationJson,
            draftRowToPublish.CreatedBy,
            draftRowToPublish.CreatedAt,
            draftRowToPublish.PublishedAt!.Value), null);
    }

    // ── Audit logging (TASK-550) ────────────────────────────────────────────
    //
    // Deliberately logged at the PublishAsync/RollbackAsync call sites, not inside the shared
    // PublishVersionAsync tail — "config published" and "config rolled back" are different
    // user-facing actions that happen to share plumbing; the Action string must reflect which one
    // the caller actually invoked.

    private async Task LogPublishedAsync(
        Guid tenantId, Guid? actingUserId, MobileConfigPublishedDto published, CancellationToken ct)
    {
        await _activityLogs.LogAsync(new ActivityLog
        {
            TenantId = tenantId,
            UserId = actingUserId,
            Action = "mobileconfig.published",
            EntityType = "mobile_configuration_version",
            EntityId = published.VersionId,
            Meta = $"{{\"version\":{published.Version}}}",
        }, ct);
        await _activityLogs.SaveChangesAsync(ct);
    }

    /// <summary>Meta records which historical version was rolled back to, per TASK-550's DoD.</summary>
    private async Task LogRolledBackAsync(
        Guid tenantId, Guid? actingUserId, MobileConfigPublishedDto published,
        MobileConfigurationVersion rolledBackTo, CancellationToken ct)
    {
        await _activityLogs.LogAsync(new ActivityLog
        {
            TenantId = tenantId,
            UserId = actingUserId,
            Action = "mobileconfig.rolled_back",
            EntityType = "mobile_configuration_version",
            EntityId = published.VersionId,
            Meta = $"{{\"rolledBackToVersion\":{rolledBackTo.Version}," +
                   $"\"rolledBackToVersionId\":\"{rolledBackTo.Id}\"," +
                   $"\"newVersion\":{published.Version}}}",
        }, ct);
        await _activityLogs.SaveChangesAsync(ct);
    }

    private static string ComposeTheme(string configurationJson, MobileTheme theme)
    {
        var node = JsonNode.Parse(configurationJson) as JsonObject
            ?? throw new InvalidOperationException(
                "Validated draft ConfigurationJson root is not a JSON object — MobileConfigValidator should have rejected this.");

        node["theme"] = MobileThemeJson.ToJsonObject(theme);

        return node.ToJsonString();
    }
}
