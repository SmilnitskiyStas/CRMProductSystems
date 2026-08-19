namespace ShelfGuard.Application.Features.MobileConfig.Dtos;

/// <summary>
/// One rejected field. <see cref="Field"/> is a JSON-path-ish pointer into the document
/// (<c>"$"</c> for the root, <c>"features.unknownKey"</c>, <c>"navigation[2].type"</c>,
/// <c>"pages.home.blocks[0].props"</c>) so a future App Builder UI (TASK-535+) can attach the
/// error to the exact field being edited instead of showing one generic failure banner.
/// </summary>
public sealed record MobileConfigValidationError(string Field, string Message);

/// <summary>Result of <see cref="IMobileConfigValidator.Validate"/>.</summary>
public sealed class MobileConfigValidationResult
{
    public IReadOnlyList<MobileConfigValidationError> Errors { get; }

    public bool IsValid => Errors.Count == 0;

    private MobileConfigValidationResult(IReadOnlyList<MobileConfigValidationError> errors)
    {
        Errors = errors;
    }

    public static MobileConfigValidationResult Success() => new([]);

    public static MobileConfigValidationResult Failure(IReadOnlyList<MobileConfigValidationError> errors) =>
        new(errors);
}
