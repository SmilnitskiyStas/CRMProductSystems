namespace ShelfGuard.Application.Features.MobileConfig.Dtos;

/// <summary>
/// One tenant's current <c>MobileTheme</c> row (TASK-531/536). <c>UpdatedAt</c> is <c>null</c>
/// when the tenant has never saved a theme yet — the response then reflects
/// <c>MobileTheme.CreateDefault</c>'s built-in defaults without a persisted row existing, same
/// "propose defaults, don't fabricate a save" convention <c>LoyaltySettingsController</c>/
/// <c>LoyaltyService.GetSettingsAsync</c> already use for <c>LoyaltyProgramSettingsDto</c>.
/// </summary>
public sealed record MobileThemeDto(
    string? LogoUrl,
    string PrimaryColor,
    string SecondaryColor,
    string BackgroundColor,
    string SurfaceColor,
    string TextPrimaryColor,
    string TextSecondaryColor,
    int ButtonRadius,
    int CardRadius,
    string SpacingPreset,
    DateTime? UpdatedAt);

/// <summary>
/// <c>PUT</c> request body for updating a tenant's <c>MobileTheme</c> row (TASK-536). Every field
/// is whitelist-validated by <c>MobileThemeValidator</c> against <see cref="MobileThemeWhitelists"/>
/// before anything is persisted — same full-replace shape as <c>MobileTheme.Update</c>.
/// </summary>
public sealed record UpdateMobileThemeRequest(
    string? LogoUrl,
    string PrimaryColor,
    string SecondaryColor,
    string BackgroundColor,
    string SurfaceColor,
    string TextPrimaryColor,
    string TextSecondaryColor,
    int ButtonRadius,
    int CardRadius,
    string SpacingPreset);
