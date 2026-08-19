using ShelfGuard.Application.Features.MobileConfig.Dtos;

namespace ShelfGuard.Application.Features.MobileConfig;

/// <summary>
/// Whitelist-based validator for <see cref="UpdateMobileThemeRequest"/> (TASK-536) — the theme
/// domain's counterpart to <see cref="IMobileConfigValidator"/>. Unlike that validator, this one
/// checks a typed request DTO rather than raw JSON, since the theme <c>PUT</c> endpoint has a fixed,
/// non-declarative shape (colors/radii/spacing/logo), not an open document. Reuses
/// <see cref="MobileConfigValidationResult"/>/<see cref="MobileConfigValidationError"/> so callers
/// get the exact same "which field, what was wrong" per-field error style
/// <see cref="MobileConfigValidator"/> already established.
/// </summary>
public interface IMobileThemeValidator
{
    /// <summary>Never throws — every rejection is reported as a field-scoped validation error.</summary>
    MobileConfigValidationResult Validate(UpdateMobileThemeRequest request);
}
