using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.Loyalty.Dtos;

namespace ShelfGuard.Application.Features.Loyalty;

/// <summary>
/// Loyalty program business logic (Фаза 0, TASK-405): consumer wallet (join/code/history),
/// staff POS resolve + manual adjustment, staff's own "join my employer's program" (plan
/// §"Кейс 2"), and per-tenant program settings. POS accrual/redemption at sale time lives in
/// PosService (composed into the same commit as the sale) — NOT here.
/// </summary>
public interface ILoyaltyService
{
    // ── Consumer-facing (wallet) ─────────────────────────────────────────────

    /// <summary>
    /// Joins (or idempotently returns the existing membership for) a tenant's loyalty
    /// program. Auto-finds/creates the tenant's own Customer record by phone.
    /// Status codes: 404 tenant not found, 403 tenant doesn't have the "loyalty" module.
    /// </summary>
    Task<(LoyaltyMembershipSummaryDto? Membership, string? Error, int? StatusCode)> JoinAsync(
        Guid consumerAccountId, Guid tenantId, CancellationToken ct = default);

    Task<IReadOnlyList<LoyaltyMembershipSummaryDto>> GetMembershipsForConsumerAsync(
        Guid consumerAccountId, CancellationToken ct = default);

    Task<IReadOnlyList<LoyaltyNetworkSummaryDto>> GetAvailableNetworksAsync(CancellationToken ct = default);

    /// <summary>
    /// TASK-499: <paramref name="tenantId"/> is optional and resolves which tenant's
    /// <c>CustomerCodeFormat</c> becomes the response's <c>DisplayFormat</c>:
    /// <list type="bullet">
    /// <item>Provided — must be a tenant the consumer has a membership at, else 403.</item>
    /// <item>Omitted, 0 memberships — "barcode" (system default; no network context yet).</item>
    /// <item>Omitted, exactly 1 membership — that membership's tenant format.</item>
    /// <item>Omitted, 2+ memberships — ambiguous, 409 ("network_selection_required").</item>
    /// </list>
    /// Status codes: 404 consumer account not found, 403 not a member of the given tenant's
    /// network, 409 ambiguous network with no explicit <paramref name="tenantId"/>.
    /// </summary>
    Task<(LoyaltyCodeDto? Code, string? Error, int? StatusCode)> GetConsumerCodeAsync(
        Guid consumerAccountId, Guid? tenantId = null, CancellationToken ct = default);

    /// <summary>
    /// TASK-507: sets which store within an already-joined network the consumer primarily
    /// shops at. Deliberately does NOT create a membership — join stays a separate, explicit
    /// step. Status codes: 403 the consumer has no <see cref="LoyaltyMembership"/> at
    /// <paramref name="tenantId"/>, 400 <paramref name="storeId"/> doesn't resolve to an
    /// active, shoppable <see cref="ShelfGuard.Domain.Entities.Location"/> belonging to that
    /// same tenant.
    /// </summary>
    Task<(LoyaltyMembershipSummaryDto? Membership, string? Error, int? StatusCode)> SetPreferredStoreAsync(
        Guid consumerAccountId, Guid tenantId, Guid storeId, CancellationToken ct = default);

    /// <summary>404 when the consumer has no membership at this tenant.</summary>
    Task<(PagedResult<LoyaltyLedgerEntryDto>? History, string? Error, int? StatusCode)> GetHistoryAsync(
        Guid consumerAccountId, Guid tenantId, int page, int pageSize, CancellationToken ct = default);

    // ── Staff-facing (POS / cabinet) ──────────────────────────────────────────

    /// <summary>
    /// Resolves a scanned/typed loyalty code at the register. Anti-replay + per-membership
    /// rate-limit/lockout (see implementation). Status codes: 400 malformed/invalid code or
    /// blocked membership, 429 locked out after repeated failures, 409 code already redeemed
    /// (replay/race).
    /// </summary>
    Task<(ResolveLoyaltyCodeResult? Result, string? Error, int? StatusCode)> ResolveCodeAsync(
        Guid tenantId, Guid staffUserId, string scannedValue, CancellationToken ct = default);

    /// <summary>store_manager+. Status codes: 404 membership not found, 400 would go negative.</summary>
    Task<(LoyaltyMembershipSummaryDto? Membership, string? Error, int? StatusCode)> ManualAdjustAsync(
        Guid tenantId, Guid staffUserId, ManualLoyaltyAdjustRequest request, CancellationToken ct = default);

    /// <summary>Plan §"Кейс 2": null when the caller has no membership in their own tenant.</summary>
    Task<LoyaltyMembershipSummaryDto?> GetMyMembershipAsync(
        Guid tenantId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Idempotent: returns the existing membership (backfilling LinkedUserId if unset)
    /// rather than erroring when the staff member (or their auto-matched ConsumerAccount)
    /// already has one. 400 when the caller's User.Phone is unset.
    /// </summary>
    Task<(LoyaltyMembershipSummaryDto? Membership, string? Error, int? StatusCode)> JoinAsStaffAsync(
        Guid tenantId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// TASK-498: resolves the ConsumerAccount for a phone typed at the register and
    /// idempotently gets-or-creates its LoyaltyMembership at <paramref name="tenantId"/> — no
    /// manual store selection by the consumer. Error non-null is a genuine client error
    /// (currently only an unparseable phone, 400). A null Result with a null Error means "not
    /// applicable" (module disabled, no matching/active ConsumerAccount) — a normal outcome the
    /// caller should treat as "fall back to a plain customer", not a failure.
    /// </summary>
    Task<(LoyaltyMembershipLookupResult? Result, string? Error, int? StatusCode)> ResolveOrCreateMembershipByPhoneAsync(
        Guid tenantId, string phone, CancellationToken ct = default);

    // ── Settings (enterprise_admin) ───────────────────────────────────────────

    /// <summary>Returns proposed defaults (3%/50%/0/30s/barcode, enabled) when the tenant has never saved a row.</summary>
    Task<LoyaltyProgramSettingsDto> GetSettingsAsync(Guid tenantId, CancellationToken ct = default);

    Task<(LoyaltyProgramSettingsDto? Settings, string? Error)> UpsertSettingsAsync(
        Guid tenantId, UpsertLoyaltyProgramSettingsRequest request, CancellationToken ct = default);
}
