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
    /// TASK-548: single-network lookup backing <c>GET /api/v1/retailers/{slug}</c>. Applies the
    /// exact same eligibility rule <see cref="GetAvailableNetworksAsync"/> filters its list by
    /// (decision 1's accepted consequence) — an unknown slug, an inactive tenant, a tenant
    /// without the "loyalty" module, or a tenant whose <see cref="Loyalty.LoyaltyProgramSettings"/>
    /// row exists but has <c>IsEnabled = false</c> are all indistinguishable 404s to the caller,
    /// same as they're simply omitted from the list endpoint. Status codes: 404 only.
    /// </summary>
    Task<(LoyaltyNetworkSummaryDto? Network, string? Error, int? StatusCode)> GetNetworkBySlugAsync(
        string slug, CancellationToken ct = default);

    /// <summary>
    /// TASK-548: slug-addressed counterpart to <see cref="JoinAsync"/> backing
    /// <c>POST /api/v1/retailers/{slug}/join</c> — resolves <paramref name="slug"/> to a tenant
    /// id and delegates to <see cref="JoinAsync"/> for every other rule (module gate, idempotency,
    /// reactivating a previously-left membership). Status codes: 404 unknown slug (added by this
    /// method itself), plus everything <see cref="JoinAsync"/> can return once the tenant is
    /// resolved (404 impossible from there on, 403 module not active).
    /// </summary>
    Task<(LoyaltyMembershipSummaryDto? Membership, string? Error, int? StatusCode)> JoinBySlugAsync(
        Guid consumerAccountId, string slug, CancellationToken ct = default);

    /// <summary>
    /// TASK-548: leaves a previously joined loyalty network — soft-deactivates the membership
    /// (<see cref="ShelfGuard.Domain.Entities.LoyaltyMembershipStatus.Left"/>) rather than
    /// deleting it, preserving Balance/JoinedAt/ledger history exactly as-is (never hard-delete
    /// financial/loyalty data — see <see cref="ShelfGuard.Domain.Entities.LoyaltyMembershipStatus"/>
    /// doc). Idempotent: leaving an already-left membership succeeds again without a redundant
    /// write. Not gated on the tenant's current "loyalty" module state or
    /// <see cref="ShelfGuard.Domain.Entities.Tenant.IsActive"/> — a consumer must always be able
    /// to leave a network they once joined, independent of whether the retailer later disabled
    /// the program. Status codes: 404 the consumer has no membership at this tenant to leave.
    /// </summary>
    Task<(bool Success, string? Error, int? StatusCode)> LeaveAsync(
        Guid consumerAccountId, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// TASK-548: slug-addressed counterpart to <see cref="LeaveAsync"/> backing
    /// <c>DELETE /api/v1/retailers/{slug}/membership</c>. Status codes: 404 unknown slug (added
    /// by this method itself) or no membership to leave (from <see cref="LeaveAsync"/>).
    /// </summary>
    Task<(bool Success, string? Error, int? StatusCode)> LeaveBySlugAsync(
        Guid consumerAccountId, string slug, CancellationToken ct = default);

    /// <summary>
    /// TASK-549: anonymous public lookup backing <c>GET /api/v1/retailers/{slug}/public</c> —
    /// the QR/deep-link onboarding web fallback page, reached by anyone who scans a retailer's
    /// QR code before installing the app or logging in. Takes no consumer identity, unlike every
    /// other consumer-facing method in this interface.
    ///
    /// Design decision (TASK-549): kept as a separate method/DTO/route rather than relaxing
    /// <see cref="GetNetworkBySlugAsync"/>'s <c>[Authorize]</c> requirement, even though that
    /// method itself never actually reads a consumer id (its auth gate lives entirely in
    /// <c>RetailersController</c>). The reason is its DTO, <see cref="LoyaltyNetworkSummaryDto"/>:
    /// it carries the tenant's full shoppable-store list (name+address per location) and internal
    /// <c>TenantId</c> guid, neither of which is safe to hand to an unauthenticated caller. This
    /// method instead reuses <see cref="GetNetworkBySlugAsync"/>'s exact eligibility filter
    /// (tenant active, "loyalty" module enabled, <see cref="Loyalty.LoyaltyProgramSettings.IsEnabled"/>
    /// not explicitly false) and its indistinguishable-404 policy: an unknown slug, an inactive
    /// tenant, a tenant without the "loyalty" module, and a tenant that has since paused its
    /// program are all identical 404s — the same enumeration posture TASK-548's decision 1
    /// already established for the list/single-network endpoints, applied consistently here so
    /// this new anonymous surface cannot be used to distinguish "never existed" from "existed
    /// once, deactivated/paused" any more precisely than the existing authenticated endpoint
    /// already can. Status codes: 404 only.
    /// </summary>
    Task<(RetailerPublicInfoDto? Info, string? Error, int? StatusCode)> GetPublicRetailerInfoAsync(
        string slug, CancellationToken ct = default);

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

    /// <summary>
    /// TASK-615: this consumer's current tier (name, multiplier, discount), composite score,
    /// and the next tier up (name + remaining score gap), for <paramref name="tenantId"/>'s
    /// ladder. Reads <c>loyalty_tier_definitions</c> — a staff-only table with no
    /// <c>consumer_self_access</c> RLS policy — through <see cref="Services.ITenantSessionOverride"/>,
    /// same pattern as <see cref="ResolveCustomerCodeFormatAsync"/> uses for
    /// <see cref="LoyaltyProgramSettings"/>. Status codes: 404 the consumer has no membership at
    /// this tenant.
    /// </summary>
    Task<(LoyaltyTierProgressDto? Progress, string? Error, int? StatusCode)> GetTierProgressAsync(
        Guid consumerAccountId, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// TASK-615: paged, newest-first <see cref="LoyaltyTierChangeHistory"/> for this consumer's
    /// membership at <paramref name="tenantId"/>. That table carries a <c>consumer_self_access</c>
    /// RLS policy (unlike the ladder itself), so this reads through the caller's ambient
    /// (consumer) session — no <see cref="Services.ITenantSessionOverride"/> needed, same as
    /// <see cref="GetHistoryAsync"/> above. Status codes: 404 the consumer has no membership at
    /// this tenant.
    /// </summary>
    Task<(PagedResult<LoyaltyTierChangeHistoryDto>? History, string? Error, int? StatusCode)> GetTierHistoryAsync(
        Guid consumerAccountId, Guid tenantId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// TASK-626: the full configured tier ladder for <paramref name="tenantId"/>, for the
    /// mobile rank-progress screen (the existing <see cref="GetTierProgressAsync"/> only
    /// returns current + next tier). Same membership check and <see
    /// cref="Services.ITenantSessionOverride"/> read as <see cref="GetTierProgressAsync"/> —
    /// <c>loyalty_tier_definitions</c> is staff-only config with no <c>consumer_self_access</c>
    /// RLS policy. Ordered ascending by <see cref="LoyaltyTierDefinition.SortOrder"/>; empty
    /// (never null) when the tenant has no ladder configured. Status codes: 404 the consumer
    /// has no membership at this tenant.
    /// </summary>
    Task<(IReadOnlyList<LoyaltyTierDefinitionDto>? Ladder, string? Error, int? StatusCode)> GetTierLadderForConsumerAsync(
        Guid consumerAccountId, Guid tenantId, CancellationToken ct = default);

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

    // ── Tier ladder (enterprise_admin, TASK-615) ──────────────────────────────

    /// <summary>Ordered ascending by <see cref="LoyaltyTierDefinition.SortOrder"/>. Empty (never null) when the tenant has no ladder yet.</summary>
    Task<IReadOnlyList<LoyaltyTierDefinitionDto>> GetTierLadderAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Bulk-replaces the tenant's whole tier ladder. Matches each submitted row against an
    /// existing one by <see cref="UpsertTierRequest.SortOrder"/> (the ladder's natural unique
    /// key) rather than blind delete-then-recreate: a row whose SortOrder is unchanged keeps its
    /// database Id, so any <see cref="LoyaltyMembership.CurrentTierId"/> already pointing at it
    /// stays valid (functional benefits — multiplier/discount — keep applying) until the next
    /// nightly recompute. A SortOrder present before but absent from <paramref name="tiers"/> is
    /// deleted; any membership whose <c>CurrentTierId</c> pointed at it is set null by the FK's
    /// SetNull behavior (safe — the nightly job re-evaluates every membership from scratch).
    /// Reordering (same conceptual tier, different SortOrder) is therefore treated as
    /// delete+insert, a documented limitation, not a hidden bug. Validates: non-empty
    /// <see cref="UpsertTierRequest.Name"/> (max 100 chars), unique
    /// <see cref="UpsertTierRequest.SortOrder"/> values within the request, non-negative
    /// <see cref="UpsertTierRequest.MinCompositeScore"/>, <see cref="UpsertTierRequest.AccrualMultiplier"/>
    /// in [0, 999.99], <see cref="UpsertTierRequest.DiscountPercent"/> in [0, 100].
    /// </summary>
    Task<(IReadOnlyList<LoyaltyTierDefinitionDto>? Tiers, string? Error)> UpsertTierLadderAsync(
        Guid tenantId, List<UpsertTierRequest> tiers, CancellationToken ct = default);

    Task<(string? Url, string? Error)> UploadTierImageAsync(
        Guid tenantId, Guid tierId, Stream imageStream, string fileName, CancellationToken ct = default);
}
