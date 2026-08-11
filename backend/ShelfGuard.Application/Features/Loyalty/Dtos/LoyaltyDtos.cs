namespace ShelfGuard.Application.Features.Loyalty.Dtos;

// ── Consumer-facing (wallet) ─────────────────────────────────────────────────

/// <summary>
/// <paramref name="PreferredStoreId"/> (TASK-507) is which store within this already-joined
/// network the consumer primarily shops at — a separate, optional preference from membership
/// itself (membership stays one-per-tenant, never one-per-store; see
/// <see cref="LoyaltyMembership.PreferredStoreId"/>). <paramref name="PreferredStoreName"/>/
/// <paramref name="PreferredStoreAddress"/> are resolved for display convenience and are both
/// null whenever <paramref name="PreferredStoreId"/> is null, or points at a store that has
/// since gone inactive or been removed (never an error — see
/// <see cref="LoyaltyService.GetMembershipsForConsumerAsync"/>).
/// </summary>
public sealed record LoyaltyMembershipSummaryDto(
    Guid MembershipId,
    Guid TenantId,
    string TenantName,
    decimal Balance,
    string Status,
    DateTimeOffset JoinedAt,
    Guid? PreferredStoreId,
    string? PreferredStoreName,
    string? PreferredStoreAddress);

/// <summary>TASK-507: one store belonging to a loyalty network's tenant, as surfaced to consumers.</summary>
public sealed record LoyaltyNetworkStoreDto(Guid StoreId, string StoreName, string? Address);

/// <summary>
/// Public consumer catalogue item; exposes no tenant configuration or internal data.
/// <paramref name="Stores"/> (TASK-501, restructured TASK-507) lists the tenant's active,
/// shoppable store locations (see <see cref="LoyaltyService"/>'s location-type filter) so a
/// consumer can recognize the network by the physical stores they actually shop at, and — via
/// <see cref="LoyaltyNetworkStoreDto.StoreId"/> — reference one when setting a preferred store
/// (see <c>PUT /api/consumer/loyalty/preferred-store</c>). Informational only, membership stays
/// one-per-tenant, never one-per-store. Sorted alphabetically by <c>StoreName</c>; empty (not
/// omitted, and never null) when the tenant currently has zero qualifying stores.
/// </summary>
public sealed record LoyaltyNetworkSummaryDto(Guid TenantId, string TenantName, IReadOnlyList<LoyaltyNetworkStoreDto> Stores);

/// <summary>TASK-507: request body for PUT /api/consumer/loyalty/preferred-store.</summary>
public sealed record SetPreferredStoreRequest(Guid TenantId, Guid StoreId);

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
