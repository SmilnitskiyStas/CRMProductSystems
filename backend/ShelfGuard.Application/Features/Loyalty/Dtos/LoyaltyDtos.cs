namespace ShelfGuard.Application.Features.Loyalty.Dtos;

// ── Consumer-facing (wallet) ─────────────────────────────────────────────────

public sealed record LoyaltyMembershipSummaryDto(
    Guid MembershipId,
    Guid TenantId,
    string TenantName,
    decimal Balance,
    string Status,
    DateTimeOffset JoinedAt);

/// <summary>Public consumer catalogue item; exposes no tenant configuration or internal data.</summary>
public sealed record LoyaltyNetworkSummaryDto(Guid TenantId, string TenantName);

/// <summary>
/// The rotating QR/barcode payload. Never carries the TOTP secret itself.
/// <paramref name="DisplayFormat"/> (TASK-499) is exactly "qr" or "barcode" — which tenant's
/// setting it reflects depends on how many loyalty networks this consumer belongs to; see
/// <see cref="ILoyaltyService.GetConsumerCodeAsync"/>.
/// </summary>
public sealed record LoyaltyCodeDto(
    string Code,
    string DisplayFormat,
    decimal Balance,
    int ExpiresInSeconds);

public sealed record LoyaltyLedgerEntryDto(
    Guid Id,
    string EntryType,
    decimal Amount,
    decimal BalanceAfter,
    string? Note,
    DateTimeOffset CreatedAt);

// ── Staff-facing (POS / settings) ────────────────────────────────────────────

/// <summary>Full scanned/typed payload, e.g. "SGLOY1.{membershipId}.{6-digit-code}".</summary>
public sealed record ResolveLoyaltyCodeRequest(string Code);

public sealed record ResolveLoyaltyCodeResult(
    Guid MembershipId,
    Guid? CustomerId,
    string? CustomerName,
    string? MaskedPhone,
    decimal Balance);

public sealed record ManualLoyaltyAdjustRequest(
    Guid MembershipId,
    decimal Amount,
    string? Note);

/// <summary>TASK-498: request body for POST /api/loyalty/resolve-or-create-by-phone.</summary>
public sealed record ResolveOrCreateMembershipByPhoneRequest(string Phone);

/// <summary>
/// TASK-498: result of a staff-triggered phone lookup at the register. Deliberately omits
/// the consumer's phone/email — the caller already has the phone it searched with, no need to
/// echo more identity data back than the UI needs to display.
/// </summary>
public sealed record LoyaltyMembershipLookupResult(
    Guid MembershipId,
    decimal Balance,
    bool IsNewMembership,
    string ConsumerFullName);

public sealed record LoyaltyProgramSettingsDto(
    bool IsEnabled,
    decimal AccrualRatePercent,
    decimal RedemptionCapPercent,
    decimal MinRedemptionBalance,
    int CodeTtlSeconds,
    /// <summary>TASK-499: "qr" or "barcode" — how this tenant's consumers render their code.</summary>
    string CustomerCodeFormat,
    DateTimeOffset? UpdatedAt);

public sealed record UpsertLoyaltyProgramSettingsRequest(
    bool IsEnabled,
    decimal AccrualRatePercent,
    decimal RedemptionCapPercent,
    decimal MinRedemptionBalance,
    int CodeTtlSeconds,
    /// <summary>
    /// TASK-499: must be exactly "qr" or "barcode" — this request has no partial-update
    /// semantics (every field is always fully overwritten), so null/empty is rejected the same
    /// as any other unrecognized value, not treated as "leave unchanged".
    /// </summary>
    string CustomerCodeFormat);
