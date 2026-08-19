using ShelfGuard.Application.Features.MobileConfig.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.MobileConfig;

/// <summary>
/// See <see cref="IMobileThemeService"/>.
///
/// LIVE-EFFECT GAP (TASK-536, flagged explicitly per the task brief — not silently papered over):
/// <see cref="MobileTheme"/> is one row per <see cref="MobileConfiguration"/>, i.e. per tenant
/// (TASK-531) — there is no separate "draft theme" vs "published theme" row the way
/// <see cref="MobileConfigurationVersion"/> distinguishes draft/published documents. Meanwhile
/// <c>MobileConfigPublishedReadService</c> (TASK-534, already shipped, already public) reads
/// <c>theme</c> LIVE off this exact same row on every <c>GET /api/v1/mobile/config</c> call — not
/// from any published snapshot — because TASK-544 (the generalized Draft→Preview→Publish
/// reconciliation for theme) does not exist yet. The practical consequence: <b>every successful
/// <see cref="UpdateThemeAsync"/> call here takes effect in production immediately</b>, for every
/// consumer currently viewing that tenant's app, with no draft/publish protection at all — this
/// quietly contradicts MASTER SPEC's "Draft/Preview/Publish, production is not affected until
/// Publish" principle for every other part of the document (features/navigation/pages).
///
/// This was a deliberate scope decision, not an oversight: inventing a parallel mini-versioning
/// mechanism just for <see cref="MobileTheme"/> to satisfy that principle literally would duplicate
/// work TASK-544 needs to do properly anyway (composing theme into <c>ConfigurationJson</c> at
/// publish time), and risks conflicting with whatever shape TASK-544 lands on. <see cref="MobileTheme"/>
/// is updated in place — the only row that exists — exactly like <c>MobileConfigDraftService</c>
/// updates a draft <see cref="MobileConfigurationVersion"/> in place, except there is no published
/// copy shielding production from this one. TASK-544's author must treat this as one of the two
/// documented options <c>MobileConfigPublishedReadService</c>'s remarks already spell out: either
/// keep reading theme live (this immediate-effect behavior becomes permanent/intentional) or switch
/// to composing theme into <c>ConfigurationJson</c> at publish time (which then requires this
/// service to gain real draft/published separation, not just an in-place row).
/// </summary>
public sealed class MobileThemeService : IMobileThemeService
{
    private readonly IMobileConfigurationRepository _repo;
    private readonly IMobileThemeValidator _validator;
    private readonly IActivityLogRepository _activityLogs;

    public MobileThemeService(
        IMobileConfigurationRepository repo, IMobileThemeValidator validator, IActivityLogRepository activityLogs)
    {
        _repo = repo;
        _validator = validator;
        _activityLogs = activityLogs;
    }

    public async Task<MobileThemeDto> GetThemeAsync(Guid tenantId, CancellationToken ct = default)
    {
        var config = await _repo.GetByTenantIdAsync(tenantId, ct);
        if (config?.Theme is { } existing)
            return ToDto(existing, persisted: true);

        // No MobileConfiguration and/or no MobileTheme row yet — propose CreateDefault's built-in
        // defaults without persisting anything, same "propose defaults on GET" convention
        // LoyaltyService.GetSettingsAsync already uses for LoyaltyProgramSettingsDto.
        var placeholder = MobileTheme.CreateDefault(config?.Id ?? Guid.Empty, tenantId);
        return ToDto(placeholder, persisted: false);
    }

    public async Task<(MobileThemeDto? Theme, IReadOnlyList<MobileConfigValidationError> Errors)> UpdateThemeAsync(
        Guid tenantId, Guid? actingUserId, UpdateMobileThemeRequest request, CancellationToken ct = default)
    {
        var validation = _validator.Validate(request);
        if (!validation.IsValid)
            return (null, validation.Errors);

        var config = await _repo.GetByTenantIdAsync(tenantId, ct);
        if (config is null)
        {
            config = MobileConfiguration.Create(tenantId);
            await _repo.AddAsync(config, ct);
        }

        MobileTheme theme;
        if (config.Theme is { } existing)
        {
            existing.Update(
                request.LogoUrl,
                request.PrimaryColor,
                request.SecondaryColor,
                request.BackgroundColor,
                request.SurfaceColor,
                request.TextPrimaryColor,
                request.TextSecondaryColor,
                request.ButtonRadius,
                request.CardRadius,
                request.SpacingPreset);
            theme = existing;
            _repo.UpdateTheme(theme);
        }
        else
        {
            // MobileTheme.MobileConfigurationId points at MobileConfiguration.Id, which is
            // client-generated (Guid.NewGuid() in MobileConfiguration.Create — never DB-assigned),
            // so it's already known here even when config is a brand-new, not-yet-saved entity.
            // Unlike MobileConfigDraftService's first-draft path (TASK-534b), MobileConfiguration
            // holds no FK pointer back at MobileTheme, so there is no mutual-pointer cycle and both
            // rows can be inserted in the same SaveChangesAsync call below.
            theme = MobileTheme.CreateDefault(config.Id, tenantId);
            theme.Update(
                request.LogoUrl,
                request.PrimaryColor,
                request.SecondaryColor,
                request.BackgroundColor,
                request.SurfaceColor,
                request.TextPrimaryColor,
                request.TextSecondaryColor,
                request.ButtonRadius,
                request.CardRadius,
                request.SpacingPreset);
            await _repo.AddThemeAsync(theme, ct);
        }

        await _repo.SaveChangesAsync(ct);

        await LogThemeUpdatedAsync(tenantId, actingUserId, theme, ct);

        return (ToDto(theme, persisted: true), []);
    }

    /// <summary>
    /// TASK-550: theme edits are part of the same "mobile config changed" audit family as
    /// draft/publish/rollback, even though <see cref="MobileTheme"/> is a separate entity/service —
    /// see this class's LIVE-EFFECT GAP remarks for why it is not draft/publish-gated the way the
    /// rest of the document is.
    /// </summary>
    private async Task LogThemeUpdatedAsync(
        Guid tenantId, Guid? actingUserId, MobileTheme theme, CancellationToken ct)
    {
        await _activityLogs.LogAsync(new ActivityLog
        {
            TenantId = tenantId,
            UserId = actingUserId,
            Action = "mobileconfig.theme_updated",
            EntityType = "mobile_theme",
            EntityId = theme.Id,
        }, ct);
        await _activityLogs.SaveChangesAsync(ct);
    }

    private static MobileThemeDto ToDto(MobileTheme theme, bool persisted) => new(
        theme.LogoUrl,
        theme.PrimaryColor,
        theme.SecondaryColor,
        theme.BackgroundColor,
        theme.SurfaceColor,
        theme.TextPrimaryColor,
        theme.TextSecondaryColor,
        theme.ButtonRadius,
        theme.CardRadius,
        theme.SpacingPreset,
        persisted ? theme.UpdatedAt : null);
}
