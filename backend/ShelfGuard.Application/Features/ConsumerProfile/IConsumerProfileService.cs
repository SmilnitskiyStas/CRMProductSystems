using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.ConsumerProfile.Dtos;

namespace ShelfGuard.Application.Features.ConsumerProfile;

/// <summary>
/// Self-service profile editing for <see cref="ShelfGuard.Domain.Entities.ConsumerAccount"/>
/// (TASK-614, plan §2 "Розширення профілю клієнта" — see
/// <c>.claude/logs/handoffs/613-to-backend_database-engineer.md</c> for the schema this
/// implements against). Every write here also appends a
/// <see cref="ShelfGuard.Domain.Entities.ConsumerAccountProfileChange"/> audit row in the SAME
/// <c>SaveChangesAsync</c> call as the <c>ConsumerAccount</c> update — see
/// <see cref="ConsumerProfileService"/> for how that atomicity is achieved.
/// </summary>
public interface IConsumerProfileService
{
    /// <summary>Status codes: 404 unknown/inactive consumer account.</summary>
    Task<(ConsumerProfileDto? Profile, string? Error, int? StatusCode)> GetProfileAsync(
        Guid consumerAccountId, CancellationToken ct = default);

    /// <summary>
    /// Updates <c>FullName</c> and/or <c>Email</c> when provided (null = leave unchanged; for
    /// <paramref name="email"/>, an empty/whitespace string clears it). Writes one
    /// <see cref="ShelfGuard.Domain.Entities.ConsumerAccountProfileChange"/> row per field that
    /// actually changed — a no-op call (values equal to what's already stored) writes nothing.
    /// Status codes: 404 unknown/inactive consumer account, 400 <paramref name="fullName"/>
    /// provided but blank, 409 <paramref name="email"/> already used by another consumer
    /// account.
    /// </summary>
    Task<(ConsumerProfileDto? Profile, string? Error, int? StatusCode)> UpdateNameOrEmailAsync(
        Guid consumerAccountId, string? fullName, string? email, CancellationToken ct = default);

    /// <summary>
    /// Changes <c>Phone</c> after verifying <paramref name="currentPassword"/> against the
    /// account's <c>PasswordHash</c>. Password re-entry stands in for SMS/OTP by design (plan
    /// §2's confirmed judgment call): no SMS gateway exists in this repo, and registration
    /// itself never verifies the phone either, so gating a later edit behind OTP would be
    /// strictly stronger than the initial signup. Status codes: 404 unknown/inactive consumer
    /// account, 400 wrong <paramref name="currentPassword"/> or unparseable
    /// <paramref name="newPhone"/>, 409 <paramref name="newPhone"/> already used by another
    /// consumer account. Setting the phone to its current (normalized) value is a no-op success
    /// that writes no audit row.
    /// </summary>
    Task<(ConsumerProfileDto? Profile, string? Error, int? StatusCode)> ChangePhoneAsync(
        Guid consumerAccountId, string newPhone, string currentPassword, CancellationToken ct = default);

    /// <summary>Paged, newest first. Status codes: 404 unknown/inactive consumer account.</summary>
    Task<(PagedResult<ConsumerProfileChangeDto>? History, string? Error, int? StatusCode)> GetProfileChangeHistoryAsync(
        Guid consumerAccountId, int page, int pageSize, CancellationToken ct = default);
}
