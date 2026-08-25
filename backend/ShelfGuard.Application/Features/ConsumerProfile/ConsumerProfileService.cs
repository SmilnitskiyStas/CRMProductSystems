using Microsoft.Extensions.Logging;
using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.ConsumerProfile.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.ConsumerProfile;

/// <summary>See <see cref="IConsumerProfileService"/> for the responsibility split.</summary>
public sealed class ConsumerProfileService : IConsumerProfileService
{
    private readonly IConsumerAccountRepository _accounts;
    private readonly IPasswordHasher _hasher;
    private readonly ILogger<ConsumerProfileService> _logger;

    public ConsumerProfileService(
        IConsumerAccountRepository accounts,
        IPasswordHasher hasher,
        ILogger<ConsumerProfileService> logger)
    {
        _accounts = accounts;
        _hasher = hasher;
        _logger = logger;
    }

    public async Task<(ConsumerProfileDto? Profile, string? Error, int? StatusCode)> GetProfileAsync(
        Guid consumerAccountId, CancellationToken ct = default)
    {
        var consumer = await _accounts.GetByIdAsync(consumerAccountId, ct);
        if (consumer is null || !consumer.IsActive)
            return (null, "Consumer account not found.", 404);

        return (ToDto(consumer), null, null);
    }

    public async Task<(ConsumerProfileDto? Profile, string? Error, int? StatusCode)> UpdateNameOrEmailAsync(
        Guid consumerAccountId, string? fullName, string? email, CancellationToken ct = default)
    {
        var consumer = await _accounts.GetByIdAsync(consumerAccountId, ct);
        if (consumer is null || !consumer.IsActive)
            return (null, "Consumer account not found.", 404);

        // Collected first, staged on the repository only once every validation has passed —
        // so a later 409 (duplicate email) never leaves a partial FullName change committed.
        var changes = new List<ConsumerAccountProfileChange>();

        if (fullName is not null)
        {
            var trimmed = fullName.Trim();
            if (trimmed.Length == 0)
                return (null, "Full name cannot be empty.", 400);

            if (!string.Equals(trimmed, consumer.FullName, StringComparison.Ordinal))
            {
                changes.Add(new ConsumerAccountProfileChange
                {
                    ConsumerAccountId = consumer.Id,
                    FieldName = ConsumerAccountProfileChangeField.FullName,
                    OldValue = consumer.FullName,
                    NewValue = trimmed,
                });
                consumer.FullName = trimmed;
            }
        }

        if (email is not null)
        {
            var trimmed = email.Trim();
            var normalized = trimmed.Length == 0 ? null : trimmed.ToLowerInvariant();

            if (!string.Equals(normalized, consumer.Email, StringComparison.Ordinal))
            {
                if (normalized is not null)
                {
                    var existing = await _accounts.GetByEmailAsync(normalized, ct);
                    if (existing is not null && existing.Id != consumer.Id)
                        return (null, "An account with this email already exists.", 409);
                }

                changes.Add(new ConsumerAccountProfileChange
                {
                    ConsumerAccountId = consumer.Id,
                    FieldName = ConsumerAccountProfileChangeField.Email,
                    OldValue = consumer.Email,
                    NewValue = normalized,
                });
                consumer.Email = normalized;
            }
        }

        if (changes.Count == 0)
            return (ToDto(consumer), null, null); // nothing actually changed — no audit rows, no write

        foreach (var change in changes)
            await _accounts.AddProfileChangeAsync(change, ct);

        _accounts.Update(consumer);
        await _accounts.SaveChangesAsync(ct); // single commit: ConsumerAccount + every audit row together

        _logger.LogInformation(
            "Consumer {ConsumerId} updated profile fields: {Fields}.",
            consumer.Id, string.Join(",", changes.Select(c => c.FieldName)));

        return (ToDto(consumer), null, null);
    }

    public async Task<(ConsumerProfileDto? Profile, string? Error, int? StatusCode)> ChangePhoneAsync(
        Guid consumerAccountId, string newPhone, string currentPassword, CancellationToken ct = default)
    {
        var consumer = await _accounts.GetByIdAsync(consumerAccountId, ct);
        if (consumer is null || !consumer.IsActive)
            return (null, "Consumer account not found.", 404);

        if (!_hasher.Verify(currentPassword, consumer.PasswordHash))
            return (null, "Current password is incorrect.", 400);

        var normalized = PhoneNormalizer.Normalize(newPhone);
        if (normalized is null)
            return (null, "Invalid phone number. Expected a Ukrainian mobile number.", 400);

        if (string.Equals(normalized, consumer.Phone, StringComparison.Ordinal))
            return (ToDto(consumer), null, null); // no-op — nothing changed, nothing to audit

        var existing = await _accounts.GetByPhoneAsync(normalized, ct);
        if (existing is not null && existing.Id != consumer.Id)
            return (null, "An account with this phone number already exists.", 409);

        await _accounts.AddProfileChangeAsync(new ConsumerAccountProfileChange
        {
            ConsumerAccountId = consumer.Id,
            FieldName = ConsumerAccountProfileChangeField.Phone,
            OldValue = consumer.Phone,
            NewValue = normalized,
        }, ct);

        consumer.Phone = normalized;
        _accounts.Update(consumer);
        await _accounts.SaveChangesAsync(ct); // single commit: ConsumerAccount.Phone + the audit row

        _logger.LogInformation("Consumer {ConsumerId} changed their phone number.", consumer.Id);

        return (ToDto(consumer), null, null);
    }

    public async Task<(PagedResult<ConsumerProfileChangeDto>? History, string? Error, int? StatusCode)> GetProfileChangeHistoryAsync(
        Guid consumerAccountId, int page, int pageSize, CancellationToken ct = default)
    {
        var consumer = await _accounts.GetByIdAsync(consumerAccountId, ct);
        if (consumer is null || !consumer.IsActive)
            return (null, "Consumer account not found.", 404);

        var clampedPage = Math.Max(1, page);
        var clampedPageSize = Math.Clamp(pageSize, 1, 200);

        var (items, total) = await _accounts.GetProfileChangesPagedAsync(
            consumerAccountId, clampedPage, clampedPageSize, ct);

        var result = new PagedResult<ConsumerProfileChangeDto>
        {
            Items = items.Select(ToChangeDto).ToList(),
            TotalCount = total,
            Page = clampedPage,
            PageSize = clampedPageSize,
        };
        return (result, null, null);
    }

    private static ConsumerProfileDto ToDto(ConsumerAccount c) =>
        new(c.Id, c.FullName, c.Email, c.Phone, c.CreatedAt);

    private static ConsumerProfileChangeDto ToChangeDto(ConsumerAccountProfileChange c) =>
        new(c.FieldName, c.OldValue, c.NewValue, c.ChangedAt);
}
