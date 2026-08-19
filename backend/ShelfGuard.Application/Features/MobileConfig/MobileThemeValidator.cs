using System.Text.RegularExpressions;
using ShelfGuard.Application.Features.MobileConfig.Dtos;

namespace ShelfGuard.Application.Features.MobileConfig;

/// <summary>See <see cref="IMobileThemeValidator"/>.</summary>
public sealed partial class MobileThemeValidator : IMobileThemeValidator
{
    public MobileConfigValidationResult Validate(UpdateMobileThemeRequest request)
    {
        var errors = new List<MobileConfigValidationError>();

        ValidateHexColor(request.PrimaryColor, "primaryColor", errors);
        ValidateHexColor(request.SecondaryColor, "secondaryColor", errors);
        ValidateHexColor(request.BackgroundColor, "backgroundColor", errors);
        ValidateHexColor(request.SurfaceColor, "surfaceColor", errors);
        ValidateHexColor(request.TextPrimaryColor, "textPrimaryColor", errors);
        ValidateHexColor(request.TextSecondaryColor, "textSecondaryColor", errors);

        if (request.ButtonRadius < MobileThemeWhitelists.MinButtonRadius ||
            request.ButtonRadius > MobileThemeWhitelists.MaxButtonRadius)
        {
            errors.Add(new(
                "buttonRadius",
                $"buttonRadius must be between {MobileThemeWhitelists.MinButtonRadius} and " +
                $"{MobileThemeWhitelists.MaxButtonRadius} (got {request.ButtonRadius})."));
        }

        if (request.CardRadius < MobileThemeWhitelists.MinCardRadius ||
            request.CardRadius > MobileThemeWhitelists.MaxCardRadius)
        {
            errors.Add(new(
                "cardRadius",
                $"cardRadius must be between {MobileThemeWhitelists.MinCardRadius} and " +
                $"{MobileThemeWhitelists.MaxCardRadius} (got {request.CardRadius})."));
        }

        if (string.IsNullOrWhiteSpace(request.SpacingPreset) ||
            !MobileThemeWhitelists.SpacingPresets.Contains(request.SpacingPreset))
        {
            errors.Add(new(
                "spacingPreset",
                $"spacingPreset must be one of: {string.Join(", ", MobileThemeWhitelists.SpacingPresets)} " +
                $"(got '{request.SpacingPreset}')."));
        }

        ValidateLogoUrl(request.LogoUrl, errors);

        return errors.Count == 0
            ? MobileConfigValidationResult.Success()
            : MobileConfigValidationResult.Failure(errors);
    }

    private static void ValidateHexColor(string value, string field, List<MobileConfigValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value) || !HexColorRegex().IsMatch(value))
            errors.Add(new(field, $"{field} must be a hex color like \"#1E40AF\" (got '{value}')."));
    }

    /// <summary>
    /// <c>LogoUrl</c> is nullable on <see cref="ShelfGuard.Domain.Entities.MobileTheme"/> — a tenant
    /// may not have a logo yet, or may explicitly clear it (<c>PUT</c> with <c>logoUrl: null</c>).
    /// A non-null value must be a well-formed absolute http(s) URL, matching the same
    /// "declarative data only, nothing that could resolve to a dangerous scheme" posture the rest
    /// of this domain enforces.
    /// </summary>
    private static void ValidateLogoUrl(string? value, List<MobileConfigValidationError> errors)
    {
        if (value is null) return;

        if (value.Length == 0 || value.Length > MobileThemeWhitelists.MaxLogoUrlLength)
        {
            errors.Add(new(
                "logoUrl",
                $"logoUrl must be between 1 and {MobileThemeWhitelists.MaxLogoUrlLength} characters, or null."));
            return;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add(new("logoUrl", "logoUrl must be a valid absolute http(s) URL, or null."));
        }
    }

    [GeneratedRegex(MobileThemeWhitelists.HexColorPattern)]
    private static partial Regex HexColorRegex();
}
