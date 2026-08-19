using ShelfGuard.Application.Features.MobileConfig;

namespace ShelfGuard.Application.Features.MobileConfig.Dtos;

/// <summary>
/// One tenant's current draft <c>MobileConfigurationVersion</c> (TASK-532). <c>Theme</c> is
/// deliberately absent — it lives on the separate <c>MobileTheme</c> row (TASK-531) and is only
/// composed into a version's <c>ConfigurationJson.theme</c> at publish time (TASK-544), not
/// carried on the draft. See <c>.claude/docs/domain-model.md</c> for the full rationale.
/// </summary>
public sealed record MobileConfigDraftDto(
    Guid MobileConfigurationId,
    Guid VersionId,
    int Version,
    int SchemaVersion,
    string Status,
    string ConfigurationJson,
    Guid? CreatedBy,
    DateTime CreatedAt);

/// <summary>
/// <c>PUT</c> request body for <see cref="MobileConfigDraftController"/> (TASK-538b) — a thin
/// wrapper around <see cref="IMobileConfigDraftService.SaveDraftAsync"/>'s <c>configurationJson</c>
/// parameter. <c>tenantId</c>/<c>actingUserId</c> are deliberately absent here: they are resolved
/// server-side from <c>ITenantContext</c>/the JWT, never trusted from the request body.
/// </summary>
public sealed record SaveMobileConfigDraftRequest(string ConfigurationJson);

/// <summary>
/// <see cref="MobileConfigDraftController"/>'s GET/PUT response shape (TASK-538b).
/// <c>HasDraft: false</c> is the "brand-new tenant, never saved a draft yet" case — a sensible
/// empty/default shape returned with <c>200 OK</c>, not a <c>404</c>, so a first-time App Builder
/// visit (TASK-539) renders an empty canvas instead of an error state. This deliberately does NOT
/// fabricate a fully valid <c>ConfigurationJson</c> document (e.g. faking 2 required navigation
/// items would invent product content that isn't this endpoint's decision to make) — same
/// "propose defaults, don't fabricate a save" spirit as <c>MobileThemeDto.UpdatedAt: null</c>,
/// adapted for a document shape that has no safe universal default.
/// </summary>
public sealed record MobileConfigDraftResponse(
    bool HasDraft,
    Guid? MobileConfigurationId,
    Guid? VersionId,
    int Version,
    int SchemaVersion,
    string Status,
    string? ConfigurationJson,
    Guid? CreatedBy,
    DateTime? CreatedAt)
{
    public static MobileConfigDraftResponse FromDraft(MobileConfigDraftDto draft) => new(
        HasDraft: true,
        draft.MobileConfigurationId,
        draft.VersionId,
        draft.Version,
        draft.SchemaVersion,
        draft.Status,
        draft.ConfigurationJson,
        draft.CreatedBy,
        draft.CreatedAt);

    public static MobileConfigDraftResponse Empty() => new(
        HasDraft: false,
        MobileConfigurationId: null,
        VersionId: null,
        Version: 0,
        SchemaVersion: MobileConfigWhitelists.CurrentSchemaVersion,
        Status: "draft",
        ConfigurationJson: null,
        CreatedBy: null,
        CreatedAt: null);
}
