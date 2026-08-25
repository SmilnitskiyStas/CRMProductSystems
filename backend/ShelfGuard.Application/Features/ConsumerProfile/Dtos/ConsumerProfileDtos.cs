namespace ShelfGuard.Application.Features.ConsumerProfile.Dtos;

/// <summary>Full self-service profile view for the calling <c>ConsumerAccount</c> (TASK-614).</summary>
public sealed record ConsumerProfileDto(
    Guid ConsumerAccountId,
    string FullName,
    string? Email,
    string Phone,
    DateTimeOffset RegisteredAt);

/// <summary>
/// PUT /api/consumer/profile. Each field is independently optional — null leaves it unchanged.
/// For <see cref="Email"/>, an empty/whitespace string clears it (sets it to null); <see
/// cref="FullName"/> may not be blank if provided.
/// </summary>
public sealed record UpdateConsumerProfileRequest(string? FullName, string? Email);

/// <summary>
/// PUT /api/consumer/profile/phone. Gated by password re-entry rather than SMS/OTP — see
/// <see cref="IConsumerProfileService.ChangePhoneAsync"/> for the rationale.
/// </summary>
public sealed record ChangeConsumerPhoneRequest(string NewPhone, string CurrentPassword);

/// <summary>One row of <see cref="ShelfGuard.Domain.Entities.ConsumerAccountProfileChange"/>.</summary>
public sealed record ConsumerProfileChangeDto(
    string FieldName,
    string? OldValue,
    string? NewValue,
    DateTimeOffset ChangedAt);
