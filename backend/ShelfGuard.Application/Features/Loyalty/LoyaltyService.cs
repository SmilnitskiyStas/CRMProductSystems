using Microsoft.Extensions.Logging;
using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.Loyalty.Dtos;
using ShelfGuard.Application.Features.MobileConfig;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Exceptions;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Loyalty;

/// <summary>
/// See <see cref="ILoyaltyService"/> for the responsibility split. QR/barcode payload format:
/// "SGLOY1.{membershipId}.{6-digit-code}" — version + GUID (O(1) lookup) + rotating TOTP code
/// (plan §"Живий QR"). The TOTP secret itself never leaves this class.
/// </summary>
public sealed class LoyaltyService : ILoyaltyService
{
    private const string ConsumerCodePrefix = "SGCUS1";

    // TASK-405: same shape as AuthService's TASK-329 constants, applied to resolve-code
    // instead of login (LoyaltyMembership has no FailedLoginAttempts/LockoutUntil columns —
    // see IResolveCodeAttemptTracker doc).
    private const int ResolveMaxFailedAttempts = 5;
    private static readonly TimeSpan ResolveLockoutDuration = TimeSpan.FromMinutes(15);
    private const string GenericResolveError = "Invalid or expired code.";

    private readonly ILoyaltyRepository _loyalty;
    private readonly ICustomerRepository _customers;
    private readonly ITenantRepository _tenants;
    private readonly IUserRepository _users;
    private readonly IConsumerAccountRepository _consumerAccounts;
    private readonly ILocationRepository _locations;
    private readonly IPasswordHasher _hasher;
    private readonly ITotpService _totp;
    private readonly IResolveCodeAttemptTracker _attempts;
    private readonly IActivityLogRepository _activityLogs;
    private readonly ITenantSessionOverride _tenantScope;
    private readonly IConsumerFeatureFlagService _featureFlags;
    private readonly ILogger<LoyaltyService> _logger;

    public LoyaltyService(
        ILoyaltyRepository loyalty,
        ICustomerRepository customers,
        ITenantRepository tenants,
        IUserRepository users,
        IConsumerAccountRepository consumerAccounts,
        ILocationRepository locations,
        IPasswordHasher hasher,
        ITotpService totp,
        IResolveCodeAttemptTracker attempts,
        IActivityLogRepository activityLogs,
        ITenantSessionOverride tenantScope,
        IConsumerFeatureFlagService featureFlags,
        ILogger<LoyaltyService> logger)
    {
        _loyalty = loyalty;
        _customers = customers;
        _tenants = tenants;
        _users = users;
        _consumerAccounts = consumerAccounts;
        _locations = locations;
        _hasher = hasher;
        _totp = totp;
        _attempts = attempts;
        _activityLogs = activityLogs;
        _tenantScope = tenantScope;
        _featureFlags = featureFlags;
        _logger = logger;
    }

    // ── Consumer-facing (wallet) ──────────────────────────────────────────────

    public async Task<(LoyaltyMembershipSummaryDto? Membership, string? Error, int? StatusCode)> JoinAsync(
        Guid consumerAccountId, Guid tenantId, CancellationToken ct = default)
    {
        var consumer = await _consumerAccounts.GetByIdAsync(consumerAccountId, ct);
        if (consumer is null || !consumer.IsActive)
            return (null, "Consumer account not found.", 404);

        var tenant = await _tenants.GetByIdAsync(tenantId, ct);
        if (tenant is null)
            return (null, "Tenant not found.", 404);
        if (!tenant.HasModule("loyalty"))
            return (null, "This business has not activated its loyalty program.", 403);

        // Idempotent: scanning the same invite QR/link twice must not error.
        var existing = await _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerAccountId, ct);
        if (existing is not null)
        {
            // TASK-548: rejoining a network the consumer previously left (LeaveAsync set
            // Status to "left") reactivates this same row rather than leaving it stranded or
            // creating a second membership — the unique (TenantId, ConsumerAccountId) index
            // means a second row is not even possible, and "join" should always result in an
            // active membership. Balance/JoinedAt/history are untouched by either leaving or
            // rejoining. Only touches loyalty_memberships, which the consumer_self_access RLS
            // policy already covers for this session shape — no ITenantSessionOverride needed,
            // same as this idempotency check itself.
            if (existing.Status == LoyaltyMembershipStatus.Left)
            {
                existing.Status = LoyaltyMembershipStatus.Active;
                _loyalty.UpdateMembership(existing);
                await _loyalty.SaveChangesAsync(ct);
            }
            return (ToSummaryDto(existing, tenant.Name), null, null);
        }

        // TASK-417: a consumer session never carries app.tenant_id (cross-tenant by design —
        // see TenantConnectionInterceptor), so "customers"' plain tenant_isolation RLS policy
        // hides every row and rejects every insert for this session shape. tenantId is already
        // a trusted value at this point (validated tenant/module checks above — see
        // ITenantSessionOverride's security contract), so it's safe to explicitly assume that
        // one tenant's RLS context for exactly this transaction. LoyaltyMembership's insert
        // would already succeed without this (consumer_self_access covers it independently),
        // but keeping both writes in the same overridden transaction is simpler to reason about
        // and makes the two rows atomic — either both are created or neither is.
        var membership = await _tenantScope.ExecuteAsync(
            tenantId,
            () => CreateMembershipCoreAsync(tenantId, consumerAccountId, consumer.Phone, consumer.FullName, ct),
            ct);

        _logger.LogInformation(
            "Consumer {ConsumerId} joined loyalty program for tenant {TenantId}.", consumerAccountId, tenantId);

        return (ToSummaryDto(membership, tenant.Name), null, null);
    }

    /// <summary>
    /// TASK-498: staff-facing counterpart to <see cref="JoinAsync"/> — resolves the
    /// ConsumerAccount for a phone number typed at the register and idempotently gets-or-creates
    /// its LoyaltyMembership at <paramref name="tenantId"/>, with no manual store selection by
    /// the consumer. Runs entirely inside the caller's existing (staff JWT) tenant RLS context —
    /// no <see cref="ITenantSessionOverride"/> is used or needed here, unlike JoinAsync's
    /// consumer-session call site, because a staff request already carries a real app.tenant_id
    /// set by TenantConnectionInterceptor for the whole request.
    ///
    /// Return-shape convention (deliberately NOT the same as this file's other tuples): Error
    /// non-null means a genuine client error (currently only an unparseable phone, 400). A null
    /// Result with a null Error means "not applicable" — module disabled, or the phone doesn't
    /// belong to any ConsumerAccount, or that account is inactive — which is a normal, expected
    /// outcome for POS (fall back to a plain CRM customer), not a failure to surface to staff.
    /// </summary>
    public async Task<(LoyaltyMembershipLookupResult? Result, string? Error, int? StatusCode)> ResolveOrCreateMembershipByPhoneAsync(
        Guid tenantId, string phone, CancellationToken ct = default)
    {
        var normalized = PhoneNormalizer.Normalize(phone);
        if (normalized is null)
            return (null, "Invalid phone number.", 400);

        var tenant = await _tenants.GetByIdAsync(tenantId, ct);
        if (tenant is null || !tenant.HasModule("loyalty"))
            return (null, null, null); // not applicable — POS falls back to a plain customer

        var consumer = await _consumerAccounts.GetByPhoneAsync(normalized, ct);
        if (consumer is null || !consumer.IsActive)
            return (null, null, null); // no (active) mobile-app account for this phone

        var existing = await _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumer.Id, ct);
        if (existing is not null)
        {
            return (
                new LoyaltyMembershipLookupResult(existing.Id, existing.Balance, false, consumer.FullName),
                null, null);
        }

        var membership = await CreateMembershipCoreAsync(tenantId, consumer.Id, consumer.Phone, consumer.FullName, ct);

        _logger.LogInformation(
            "Consumer {ConsumerId} auto-enrolled in loyalty program for tenant {TenantId} via staff phone lookup.",
            consumer.Id, tenantId);

        return (new LoyaltyMembershipLookupResult(membership.Id, membership.Balance, true, consumer.FullName), null, null);
    }

    public async Task<IReadOnlyList<LoyaltyMembershipSummaryDto>> GetMembershipsForConsumerAsync(
        Guid consumerAccountId, CancellationToken ct = default)
    {
        var memberships = await _loyalty.GetMembershipsForConsumerAsync(consumerAccountId, ct);
        var result = new List<LoyaltyMembershipSummaryDto>(memberships.Count);
        foreach (var m in memberships)
        {
            var preferredStore = await ResolvePreferredStoreAsync(m, ct);
            result.Add(ToSummaryDto(m, m.Tenant?.Name ?? "—", preferredStore));
        }
        return result;
    }

    /// <summary>
    /// TASK-507: resolves <paramref name="m"/>'s <see cref="LoyaltyMembership.PreferredStoreId"/>
    /// to its <see cref="Location"/>, or null when unset, inactive, or since removed — never
    /// throws on a stale reference (see <see cref="LoyaltyMembershipSummaryDto"/> doc). Runs
    /// through <see cref="ITenantSessionOverride"/> for the same reason as
    /// <see cref="ResolveCustomerCodeFormatAsync"/> above: "locations" carries only the
    /// canonical tenant_isolation RLS policy, no consumer_self_access exemption, so a consumer
    /// session's ambient (null) app.tenant_id would otherwise see nothing.
    /// </summary>
    private async Task<Location?> ResolvePreferredStoreAsync(LoyaltyMembership m, CancellationToken ct)
    {
        if (m.PreferredStoreId is null) return null;

        var location = await _tenantScope.ExecuteAsync(
            m.TenantId, () => _locations.GetByIdAsync(m.PreferredStoreId.Value, ct), ct);
        return location is { IsActive: true } && location.TenantId == m.TenantId ? location : null;
    }

    /// <summary>
    /// TASK-559 (Option A): alongside the existing B2B <c>HasModule("loyalty")</c>/
    /// <c>LoyaltyProgramSettings.IsEnabled</c> filters, also excludes any tenant that has
    /// published <c>features.loyalty: false</c> (TASK-543/558 consumer-app flag,
    /// <see cref="IConsumerFeatureFlagService"/>) — a discovery-only cut, never applied to an
    /// existing member's own data (see this file's other consumer-facing methods, none of which
    /// call <see cref="_featureFlags"/>). Checked first, before the tenant-scoped settings/store
    /// load below, so a disabled tenant skips that second per-tenant round trip entirely — this
    /// method already pays one <see cref="ITenantSessionOverride"/> round trip per candidate
    /// tenant (a pre-existing N+1-shaped pattern, not introduced by this change), and the flag
    /// check adds one more per candidate; not optimized away here per TASK-559 scope.
    /// </summary>
    public async Task<IReadOnlyList<LoyaltyNetworkSummaryDto>> GetAvailableNetworksAsync(
        CancellationToken ct = default)
    {
        var tenants = await _tenants.GetAllAsync(ct);
        var result = new List<LoyaltyNetworkSummaryDto>();
        foreach (var tenant in tenants.Where(t => t.IsActive && t.HasModule("loyalty")))
        {
            if (!await _featureFlags.IsEnabledAsync(tenant.Id, "loyalty", ct))
                continue;

            var (settings, stores) = await _tenantScope.ExecuteAsync(
                tenant.Id, () => LoadNetworkDetailsAsync(tenant.Id, ct), ct);
            if (settings?.IsEnabled == false) continue;
            result.Add(new LoyaltyNetworkSummaryDto(tenant.Id, tenant.Name, tenant.Slug, stores));
        }
        return result;
    }

    /// <summary>
    /// TASK-548: single-network lookup by slug for <c>GET /api/v1/retailers/{slug}</c> — applies
    /// the exact same eligibility rule <see cref="GetAvailableNetworksAsync"/> filters its list
    /// by, reusing the same <see cref="LoadNetworkDetailsAsync"/> helper (and thus the same
    /// <see cref="ITenantSessionOverride"/> pattern) so the two endpoints can never drift apart.
    /// </summary>
    public async Task<(LoyaltyNetworkSummaryDto? Network, string? Error, int? StatusCode)> GetNetworkBySlugAsync(
        string slug, CancellationToken ct = default)
    {
        const string notFound = "Retailer not found.";

        var tenant = await _tenants.GetBySlugAsync(slug, ct);
        if (tenant is null || !tenant.IsActive || !tenant.HasModule("loyalty"))
            return (null, notFound, 404);

        var (settings, stores) = await _tenantScope.ExecuteAsync(
            tenant.Id, () => LoadNetworkDetailsAsync(tenant.Id, ct), ct);
        if (settings?.IsEnabled == false)
            return (null, notFound, 404);

        return (new LoyaltyNetworkSummaryDto(tenant.Id, tenant.Name, tenant.Slug, stores), null, null);
    }

    /// <summary>
    /// TASK-548: resolves <paramref name="slug"/> to a tenant id and delegates every other rule
    /// to <see cref="JoinAsync"/> — kept as a thin wrapper so the join logic itself (module gate,
    /// idempotency, left-membership reactivation) has exactly one implementation shared by both
    /// the legacy tenantId-addressed route and this slug-addressed one.
    /// </summary>
    public async Task<(LoyaltyMembershipSummaryDto? Membership, string? Error, int? StatusCode)> JoinBySlugAsync(
        Guid consumerAccountId, string slug, CancellationToken ct = default)
    {
        var tenant = await _tenants.GetBySlugAsync(slug, ct);
        if (tenant is null)
            return (null, "Retailer not found.", 404);

        return await JoinAsync(consumerAccountId, tenant.Id, ct);
    }

    /// <summary>See <see cref="ILoyaltyService.LeaveAsync"/>.</summary>
    public async Task<(bool Success, string? Error, int? StatusCode)> LeaveAsync(
        Guid consumerAccountId, Guid tenantId, CancellationToken ct = default)
    {
        var membership = await _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerAccountId, ct);
        if (membership is null)
            return (false, "You are not a member of this network.", 404);

        // Idempotent: leaving twice is a success both times, not a 404/409 — matches this
        // file's other idempotent consumer actions (JoinAsync, ResolveOrCreateMembershipByPhoneAsync).
        if (membership.Status != LoyaltyMembershipStatus.Left)
        {
            membership.Status = LoyaltyMembershipStatus.Left;
            _loyalty.UpdateMembership(membership);
            await _loyalty.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "Consumer {ConsumerId} left loyalty program for tenant {TenantId}.", consumerAccountId, tenantId);

        return (true, null, null);
    }

    /// <summary>Slug-addressed counterpart to <see cref="LeaveAsync"/> — see its doc.</summary>
    public async Task<(bool Success, string? Error, int? StatusCode)> LeaveBySlugAsync(
        Guid consumerAccountId, string slug, CancellationToken ct = default)
    {
        var tenant = await _tenants.GetBySlugAsync(slug, ct);
        if (tenant is null)
            return (false, "Retailer not found.", 404);

        return await LeaveAsync(consumerAccountId, tenant.Id, ct);
    }

    /// <summary>See <see cref="ILoyaltyService.GetPublicRetailerInfoAsync"/> for the full design
    /// rationale. Deliberately does not call <see cref="LoadNetworkDetailsAsync"/> — the public
    /// DTO never needs the store list, so there is no reason to pay for loading/projecting it.</summary>
    public async Task<(RetailerPublicInfoDto? Info, string? Error, int? StatusCode)> GetPublicRetailerInfoAsync(
        string slug, CancellationToken ct = default)
    {
        const string notFound = "Retailer not found.";

        var tenant = await _tenants.GetBySlugAsync(slug, ct);
        if (tenant is null || !tenant.IsActive || !tenant.HasModule("loyalty"))
            return (null, notFound, 404);

        // loyalty_program_settings has no consumer_self_access RLS policy, same reason
        // GetNetworkBySlugAsync/LoadNetworkDetailsAsync route this read through the tenant
        // session override instead of reading it ambiently.
        var settings = await _tenantScope.ExecuteAsync(
            tenant.Id, () => _loyalty.GetSettingsAsync(tenant.Id, ct), ct);
        if (settings?.IsEnabled == false)
            return (null, notFound, 404);

        return (new RetailerPublicInfoDto(tenant.Name, tenant.Slug, tenant.LogoUrl, true), null, null);
    }

    /// <summary>
    /// TASK-501: reads this tenant's loyalty settings and its shoppable stores together,
    /// inside the single <see cref="ITenantSessionOverride"/> block <see
    /// cref="GetAvailableNetworksAsync"/> already opens per tenant — combining both reads keeps
    /// it to one override per tenant instead of two. <see cref="ILocationRepository.GetAllAsync"/>
    /// takes no tenant parameter (RLS-scoped to whatever app.tenant_id the override set), same
    /// contract as <see cref="ILoyaltyRepository.GetSettingsAsync"/> right above it.
    /// TASK-507: projects the full <see cref="LoyaltyNetworkStoreDto"/> (with <c>StoreId</c>)
    /// instead of just the name, so a consumer can reference a specific store when setting a
    /// preferred store — same filter/sort as before, sorted by <c>StoreName</c>.
    /// </summary>
    private async Task<(LoyaltyProgramSettings? Settings, IReadOnlyList<LoyaltyNetworkStoreDto> Stores)> LoadNetworkDetailsAsync(
        Guid tenantId, CancellationToken ct)
    {
        var settings = await _loyalty.GetSettingsAsync(tenantId, ct);

        var locations = await _locations.GetAllAsync(ct);
        var stores = locations
            .Where(l => l.IsActive && IsShoppableStoreType(l.Type))
            .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .Select(l => new LoyaltyNetworkStoreDto(l.Id, l.Name, l.Address))
            .ToList();

        return (settings, stores);
    }

    /// <summary>
    /// TASK-501: which <see cref="Location.Type"/> values count as an actual walk-in/shoppable
    /// store for the consumer-facing loyalty network picker (as opposed to a warehouse or
    /// back-office the consumer would never visit). NOTE despite the name: Location's separate
    /// <c>LocationType</c> column (default "retail_store") is dead — nothing in Application ever
    /// reads or writes it; every location's real, populated type lives in <c>Type</c>
    /// (LocationService's <c>CreateLocationRequest.LocationType</c>/<c>UpdateLocationRequest
    /// .LocationType</c> DTO fields are assigned onto entity <c>Type</c>, not entity
    /// <c>LocationType</c> — see LocationService.CreateAsync/UpdateAsync). This is deliberately
    /// an exclude-list against LocationService.IsValidLocationType's full type set rather than an
    /// include-list, so a new customer-facing type added there later shows up here automatically
    /// instead of silently vanishing from the picker.
    /// </summary>
    private static readonly IReadOnlySet<string> NonShoppableLocationTypes = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase)
    {
        "warehouse", "central_warehouse", "distribution", "office", "production",
    };

    private static bool IsShoppableStoreType(string type) => !NonShoppableLocationTypes.Contains(type);

    public async Task<(LoyaltyCodeDto? Code, string? Error, int? StatusCode)> GetConsumerCodeAsync(
        Guid consumerAccountId, Guid? tenantId = null, CancellationToken ct = default)
    {
        var consumer = await _consumerAccounts.GetByIdAsync(consumerAccountId, ct);
        if (consumer is null || !consumer.IsActive)
            return (null, "Consumer account not found.", 404);

        string displayFormat;
        if (tenantId is not null)
        {
            var membership = await _loyalty.GetMembershipByTenantConsumerAsync(tenantId.Value, consumerAccountId, ct);
            if (membership is null)
                return (null, "You are not a member of this network.", 403);

            displayFormat = await ResolveCustomerCodeFormatAsync(tenantId.Value, ct);
        }
        else
        {
            var memberships = await _loyalty.GetMembershipsForConsumerAsync(consumerAccountId, ct);
            if (memberships.Count >= 2)
                return (null, "network_selection_required", 409);

            displayFormat = memberships.Count == 1
                ? await ResolveCustomerCodeFormatAsync(memberships[0].TenantId, ct)
                : "barcode"; // 0 memberships — system default, no network context exists yet
        }

        if (string.IsNullOrWhiteSpace(consumer.LoyaltyTotpSecret))
        {
            consumer.LoyaltyTotpSecret = _totp.GenerateSecret();
            _consumerAccounts.Update(consumer);
            await _consumerAccounts.SaveChangesAsync(ct);
        }

        var code = _totp.GenerateCode(consumer.LoyaltyTotpSecret);
        var payload = $"{ConsumerCodePrefix}.{consumer.Id}.{code}";
        return (new LoyaltyCodeDto(payload, displayFormat, 0m, 30), null, null);
    }

    /// <summary>
    /// TASK-507: sets which store within an already-joined network the consumer primarily
    /// shops at. Deliberately does NOT create a membership — no membership at
    /// <paramref name="tenantId"/> is a 403, full stop, no implicit join (join stays a
    /// separate, explicit step via <see cref="JoinAsync"/>). Runs entirely inside a single
    /// <see cref="ITenantSessionOverride"/> block — same pattern as <see cref="JoinAsync"/>'s
    /// consumer-session call site — since this is a staff-equivalent consumer context with no
    /// real tenant claim, and the store-validity check needs to read "locations", which (like
    /// "customers") has no consumer_self_access RLS policy.
    /// </summary>
    public async Task<(LoyaltyMembershipSummaryDto? Membership, string? Error, int? StatusCode)> SetPreferredStoreAsync(
        Guid consumerAccountId, Guid tenantId, Guid storeId, CancellationToken ct = default)
    {
        var (membership, location, error, statusCode) = await _tenantScope.ExecuteAsync(
            tenantId, () => SetPreferredStoreCoreAsync(consumerAccountId, tenantId, storeId, ct), ct);

        if (error is not null || membership is null)
            return (null, error, statusCode);

        var tenant = await _tenants.GetByIdAsync(tenantId, ct);
        return (ToSummaryDto(membership, tenant?.Name ?? "—", location), null, null);
    }

    private async Task<(LoyaltyMembership? Membership, Location? Location, string? Error, int? StatusCode)>
        SetPreferredStoreCoreAsync(Guid consumerAccountId, Guid tenantId, Guid storeId, CancellationToken ct)
    {
        var membership = await _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerAccountId, ct);
        if (membership is null)
            return (null, null, "You are not a member of this network.", 403);

        var location = await _locations.GetByIdAsync(storeId, ct);
        if (location is null || location.TenantId != tenantId || !location.IsActive || !IsShoppableStoreType(location.Type))
            return (null, null, "Invalid store for this network.", 400);

        membership.PreferredStoreId = storeId;
        _loyalty.UpdateMembership(membership);
        await _loyalty.SaveChangesAsync(ct);

        return (membership, location, null, null);
    }

    /// <summary>
    /// TASK-499: reads <see cref="LoyaltyProgramSettings.CustomerCodeFormat"/> for
    /// <paramref name="tenantId"/>, or "barcode" when the tenant has never saved a settings
    /// row. Runs through <see cref="ITenantSessionOverride"/> because
    /// <c>loyalty_program_settings</c> carries only the canonical tenant_isolation RLS
    /// policy — no consumer_self_access policy exists for it (unlike loyalty_memberships), so
    /// a consumer session's ambient (null) app.tenant_id would otherwise see no row at all.
    /// <paramref name="tenantId"/> must already be a value the caller trusts for this
    /// operation — both call sites above pass in a tenant the consumer is proven (by an
    /// already-checked LoyaltyMembership row) to belong to, satisfying
    /// ITenantSessionOverride's security contract.
    /// </summary>
    private async Task<string> ResolveCustomerCodeFormatAsync(Guid tenantId, CancellationToken ct)
    {
        var settings = await _tenantScope.ExecuteAsync(
            tenantId, () => _loyalty.GetSettingsAsync(tenantId, ct), ct);
        return settings?.CustomerCodeFormat ?? "barcode";
    }

    [Obsolete("Use GetConsumerCodeAsync; checkout codes are no longer tenant-specific.")]
    public Task<(LoyaltyCodeDto? Code, string? Error, int? StatusCode)> GetCurrentCodeAsync(
        Guid consumerAccountId, Guid tenantId, CancellationToken ct = default) =>
        GetConsumerCodeAsync(consumerAccountId, tenantId, ct);

    public async Task<(PagedResult<LoyaltyLedgerEntryDto>? History, string? Error, int? StatusCode)> GetHistoryAsync(
        Guid consumerAccountId, Guid tenantId, int page, int pageSize, CancellationToken ct = default)
    {
        var membership = await _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerAccountId, ct);
        if (membership is null)
            return (null, "You are not a member of this loyalty program.", 404);

        var clampedPage = Math.Max(1, page);
        var clampedPageSize = Math.Clamp(pageSize, 1, 200);

        var (items, total) = await _loyalty.GetLedgerPagedAsync(
            tenantId, membership.Id, clampedPage, clampedPageSize, ct);

        var result = new PagedResult<LoyaltyLedgerEntryDto>
        {
            Items = items.Select(ToLedgerDto).ToList(),
            TotalCount = total,
            Page = clampedPage,
            PageSize = clampedPageSize,
        };
        return (result, null, null);
    }

    // ── Tier ladder — consumer-facing (TASK-615) ──────────────────────────────

    /// <summary>See <see cref="ILoyaltyService.GetTierProgressAsync"/>.</summary>
    public async Task<(LoyaltyTierProgressDto? Progress, string? Error, int? StatusCode)> GetTierProgressAsync(
        Guid consumerAccountId, Guid tenantId, CancellationToken ct = default)
    {
        var membership = await _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerAccountId, ct);
        if (membership is null)
            return (null, "You are not a member of this loyalty program.", 404);

        // loyalty_tier_definitions is staff-only config (no consumer_self_access RLS policy —
        // see the TASK-613 handoff), so a consumer session's ambient (null) app.tenant_id needs
        // the same override ResolveCustomerCodeFormatAsync uses for loyalty_program_settings.
        var ladder = await _tenantScope.ExecuteAsync(
            tenantId, () => _loyalty.GetTierLadderAsync(tenantId, ct), ct);

        var currentTier = membership.CurrentTierId is Guid currentId
            ? ladder.FirstOrDefault(t => t.Id == currentId)
            : null;

        // Next rung up: the first ladder entry (in ascending SortOrder) above the current tier
        // — or the lowest rung of all when the membership doesn't hold any tier yet.
        var currentIndex = currentTier is null ? -1 : ladder.IndexOf(currentTier);
        var nextTier = ladder.Count > currentIndex + 1 ? ladder[currentIndex + 1] : null;

        var progress = new LoyaltyTierProgressDto(
            currentTier?.Id,
            currentTier?.Name,
            currentTier?.AccrualMultiplier ?? 1.0m,
            currentTier?.DiscountPercent ?? 0m,
            membership.CompositeScore,
            nextTier?.Id,
            nextTier?.Name,
            nextTier is null ? null : Math.Max(0, nextTier.MinCompositeScore - membership.CompositeScore));

        return (progress, null, null);
    }

    /// <summary>See <see cref="ILoyaltyService.GetTierHistoryAsync"/>.</summary>
    public async Task<(PagedResult<LoyaltyTierChangeHistoryDto>? History, string? Error, int? StatusCode)> GetTierHistoryAsync(
        Guid consumerAccountId, Guid tenantId, int page, int pageSize, CancellationToken ct = default)
    {
        var membership = await _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerAccountId, ct);
        if (membership is null)
            return (null, "You are not a member of this loyalty program.", 404);

        var clampedPage = Math.Max(1, page);
        var clampedPageSize = Math.Clamp(pageSize, 1, 200);

        var (items, total) = await _loyalty.GetTierHistoryPagedAsync(
            tenantId, membership.Id, clampedPage, clampedPageSize, ct);

        var result = new PagedResult<LoyaltyTierChangeHistoryDto>
        {
            Items = items.Select(ToTierHistoryDto).ToList(),
            TotalCount = total,
            Page = clampedPage,
            PageSize = clampedPageSize,
        };
        return (result, null, null);
    }

    // ── Staff-facing (POS / cabinet) ──────────────────────────────────────────

    public async Task<(ResolveLoyaltyCodeResult? Result, string? Error, int? StatusCode)> ResolveCodeAsync(
        Guid tenantId, Guid staffUserId, string scannedValue, CancellationToken ct = default)
    {
        // Transitional support for already-issued membership codes while clients roll out.
        if (scannedValue?.StartsWith("SGLOY1.", StringComparison.Ordinal) == true)
            return await ResolveLegacyMembershipCodeAsync(tenantId, staffUserId, scannedValue, ct);

        if (!TryParsePayload(scannedValue, out var consumerId, out var code))
            return (null, "Malformed loyalty code.", 400);

        if (_attempts.IsLockedOut(consumerId))
            return (null,
                "Too many failed attempts for this code. Ask the customer to refresh their QR and try again shortly.",
                429);

        var consumer = await _consumerAccounts.GetByIdAsync(consumerId, ct);
        if (consumer is null || !consumer.IsActive || string.IsNullOrWhiteSpace(consumer.LoyaltyTotpSecret))
        {
            var justLockedOut = _attempts.RegisterFailure(consumerId, ResolveMaxFailedAttempts, ResolveLockoutDuration);
            await LogResolveFailureAsync(tenantId, staffUserId, consumerId, justLockedOut, ct);
            return (null, GenericResolveError, 400);
        }

        var timestep = _totp.VerifyCode(consumer.LoyaltyTotpSecret, code);
        if (timestep is null)
        {
            var justLockedOut = _attempts.RegisterFailure(consumerId, ResolveMaxFailedAttempts, ResolveLockoutDuration);
            await LogResolveFailureAsync(tenantId, staffUserId, consumerId, justLockedOut, ct);
            return (null, GenericResolveError, 400);
        }

        // The register supplies the store context. First scan automatically creates the
        // tenant membership and CRM customer, so the consumer never chooses a store.
        var membership = await _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, ct);
        if (membership is null)
        {
            var (joined, joinError, joinStatus) = await JoinAsync(consumerId, tenantId, ct);
            if (joinError is not null || joined is null) return (null, joinError, joinStatus);
            membership = await _loyalty.GetMembershipByIdAsync(joined.MembershipId, tenantId, ct);
            if (membership is null) return (null, "Could not create loyalty membership.", 500);
        }

        if (membership.Status != LoyaltyMembershipStatus.Active)
            return (null, "This loyalty membership is blocked.", 400);

        var claimed = await _loyalty.TryClaimTimestepAsync(membership.Id, tenantId, timestep.Value, ct);
        if (!claimed)
            return (null, "This code was already used. Ask the customer to refresh their QR code and scan again.", 409);

        _attempts.Reset(consumerId);

        string? customerName = null;
        if (membership.CustomerId.HasValue)
        {
            var customer = await _customers.GetByIdAsync(membership.CustomerId.Value, tenantId, ct);
            customerName = customer?.Name;
        }

        var maskedPhone = MaskPhone(consumer.Phone);

        return (new ResolveLoyaltyCodeResult(
            membership.Id, membership.CustomerId, customerName, maskedPhone, membership.Balance), null, null);
    }

    private async Task<(ResolveLoyaltyCodeResult? Result, string? Error, int? StatusCode)>
        ResolveLegacyMembershipCodeAsync(Guid tenantId, Guid staffUserId, string scannedValue, CancellationToken ct)
    {
        var parts = scannedValue.Trim().Split('.');
        if (parts.Length != 3 || !Guid.TryParse(parts[1], out var membershipId))
            return (null, "Malformed loyalty code.", 400);

        if (_attempts.IsLockedOut(membershipId))
            return (null, "Too many failed attempts for this code. Ask the customer to refresh their QR and try again shortly.", 429);

        var membership = await _loyalty.GetMembershipByIdAsync(membershipId, tenantId, ct);
        if (membership is null)
        {
            var locked = _attempts.RegisterFailure(membershipId, ResolveMaxFailedAttempts, ResolveLockoutDuration);
            await LogResolveFailureAsync(tenantId, staffUserId, membershipId, locked, ct);
            return (null, GenericResolveError, 400);
        }

        if (membership.Status != LoyaltyMembershipStatus.Active)
            return (null, "This loyalty membership is blocked.", 400);

        var timestep = _totp.VerifyCode(membership.TotpSecret, parts[2].Trim());
        if (timestep is null)
        {
            var locked = _attempts.RegisterFailure(membershipId, ResolveMaxFailedAttempts, ResolveLockoutDuration);
            await LogResolveFailureAsync(tenantId, staffUserId, membershipId, locked, ct);
            return (null, GenericResolveError, 400);
        }

        if (!await _loyalty.TryClaimTimestepAsync(membership.Id, tenantId, timestep.Value, ct))
            return (null, "This code was already used. Ask the customer to refresh their QR code and scan again.", 409);

        _attempts.Reset(membershipId);
        var customer = membership.CustomerId.HasValue
            ? await _customers.GetByIdAsync(membership.CustomerId.Value, tenantId, ct)
            : null;
        var consumer = await _consumerAccounts.GetByIdAsync(membership.ConsumerAccountId, ct);
        return (new ResolveLoyaltyCodeResult(membership.Id, membership.CustomerId, customer?.Name,
            consumer is null ? null : MaskPhone(consumer.Phone), membership.Balance), null, null);
    }

    public async Task<(LoyaltyMembershipSummaryDto? Membership, string? Error, int? StatusCode)> ManualAdjustAsync(
        Guid tenantId, Guid staffUserId, ManualLoyaltyAdjustRequest request, CancellationToken ct = default)
    {
        var membership = await _loyalty.GetMembershipByIdAsync(request.MembershipId, tenantId, ct);
        if (membership is null)
            return (null, "Loyalty membership not found.", 404);

        var newBalance = membership.Balance + request.Amount;
        if (newBalance < 0)
            return (null, "Adjustment would result in a negative balance.", 400);

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();

        var entry = new LoyaltyLedgerEntry
        {
            TenantId = tenantId,
            MembershipId = membership.Id,
            EntryType = LoyaltyEntryType.ManualAdjustment,
            Amount = request.Amount,
            BalanceAfter = newBalance,
            CreatedByUserId = staffUserId,
            Note = note,
        };

        membership.Balance = newBalance;
        _loyalty.UpdateMembership(membership);
        await _loyalty.AddLedgerEntryAsync(entry, ct);

        try
        {
            await _loyalty.SaveChangesAsync(ct);
        }
        catch (ConcurrencyConflictException ex)
        {
            // TASK-414: another writer (a concurrent POS redemption/accrual, or a second
            // ManualAdjustAsync call) committed a Balance change to this same membership
            // between our read above and this SaveChangesAsync — LoyaltyMembership carries an
            // xmin concurrency token (see AppDbContext) precisely to catch this instead of
            // silently overwriting the other writer's update. Nothing was persisted, so it's
            // safe to just ask the caller to retry against the fresh balance.
            _logger.LogWarning(ex,
                "Concurrent balance update conflict while manually adjusting membership {MembershipId} for tenant {TenantId}",
                request.MembershipId, tenantId);
            return (null, "Loyalty balance was updated concurrently by another operation. Please retry.", 409);
        }

        await _activityLogs.LogAsync(new ActivityLog
        {
            TenantId = tenantId,
            UserId = staffUserId,
            Action = "loyalty.manual_adjust",
            EntityType = "loyalty_membership",
            EntityId = membership.Id,
            Meta = $"amount={request.Amount:0.00}; note={note ?? "-"}",
        }, ct);
        await _activityLogs.SaveChangesAsync(ct);

        var tenant = await _tenants.GetByIdAsync(tenantId, ct);
        return (ToSummaryDto(membership, tenant?.Name ?? "—"), null, null);
    }

    public async Task<LoyaltyMembershipSummaryDto?> GetMyMembershipAsync(
        Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var membership = await _loyalty.GetMembershipByLinkedUserAsync(tenantId, userId, ct);
        if (membership is null) return null;

        var tenant = await _tenants.GetByIdAsync(tenantId, ct);
        return ToSummaryDto(membership, tenant?.Name ?? "—");
    }

    public async Task<(LoyaltyMembershipSummaryDto? Membership, string? Error, int? StatusCode)> JoinAsStaffAsync(
        Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null)
            return (null, "User not found.", 404);

        var phone = PhoneNormalizer.Normalize(user.Phone);
        if (phone is null)
            return (null, "Your profile has no phone number set. Add one in your profile first.", 400);

        var tenant = await _tenants.GetByIdAsync(tenantId, ct);
        if (tenant is null)
            return (null, "Tenant not found.", 404);
        if (!tenant.HasModule("loyalty"))
            return (null, "This business has not activated its loyalty program.", 403);

        var consumer = await _consumerAccounts.GetByPhoneAsync(phone, ct);
        if (consumer is null)
        {
            consumer = new ConsumerAccount
            {
                Phone = phone,
                FullName = user.FullName,
                // Staff never logs in through consumer-auth via this auto-created account
                // unless they later set a real password (no reset flow exists yet — out of
                // scope, see task log). A random, never-communicated hash is a safe
                // placeholder for the non-null PasswordHash column.
                PasswordHash = _hasher.Hash(Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")),
            };
            await _consumerAccounts.AddAsync(consumer, ct);
            await _consumerAccounts.SaveChangesAsync(ct);
        }

        // Unique index is (TenantId, ConsumerAccountId) — must find-or-backfill, never
        // blind-insert, or a consumer who already joined independently would trigger a
        // uq_loyalty_memberships_tenant_consumer violation here.
        var membership = await _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumer.Id, ct);
        if (membership is not null)
        {
            if (membership.LinkedUserId != userId)
            {
                membership.LinkedUserId = userId;
                _loyalty.UpdateMembership(membership);
                await _loyalty.SaveChangesAsync(ct);
            }
            return (ToSummaryDto(membership, tenant.Name), null, null);
        }

        var customer = await FindOrCreateCustomerAsync(tenantId, phone, user.FullName, ct);

        membership = new LoyaltyMembership
        {
            TenantId = tenantId,
            ConsumerAccountId = consumer.Id,
            CustomerId = customer.Id,
            LinkedUserId = userId,
            TotpSecret = _totp.GenerateSecret(),
            Balance = 0m,
            Status = LoyaltyMembershipStatus.Active,
        };
        await _loyalty.AddMembershipAsync(membership, ct);
        await _loyalty.SaveChangesAsync(ct);

        await _activityLogs.LogAsync(new ActivityLog
        {
            TenantId = tenantId,
            UserId = userId,
            Action = "loyalty.join_as_staff",
            EntityType = "loyalty_membership",
            EntityId = membership.Id,
        }, ct);
        await _activityLogs.SaveChangesAsync(ct);

        return (ToSummaryDto(membership, tenant.Name), null, null);
    }

    // ── Settings (enterprise_admin) ───────────────────────────────────────────

    public async Task<LoyaltyProgramSettingsDto> GetSettingsAsync(Guid tenantId, CancellationToken ct = default)
    {
        var settings = await _loyalty.GetSettingsAsync(tenantId, ct);
        var isNew = settings is null;
        settings ??= new LoyaltyProgramSettings { TenantId = tenantId };
        return ToSettingsDto(settings, isNew);
    }

    public async Task<(LoyaltyProgramSettingsDto? Settings, string? Error)> UpsertSettingsAsync(
        Guid tenantId, UpsertLoyaltyProgramSettingsRequest request, CancellationToken ct = default)
    {
        if (request.AccrualRatePercent < 0 || request.AccrualRatePercent > 100)
            return (null, "AccrualRatePercent must be between 0 and 100.");
        if (request.RedemptionCapPercent < 0 || request.RedemptionCapPercent > 100)
            return (null, "RedemptionCapPercent must be between 0 and 100.");
        if (request.MinRedemptionBalance < 0)
            return (null, "MinRedemptionBalance cannot be negative.");
        if (request.CodeTtlSeconds is < 5 or > 300)
            return (null, "CodeTtlSeconds must be between 5 and 300.");
        if (request.CustomerCodeFormat is not ("qr" or "barcode"))
            return (null, "CustomerCodeFormat must be 'qr' or 'barcode'.");

        var settings = await _loyalty.GetSettingsAsync(tenantId, ct);
        if (settings is null)
        {
            settings = new LoyaltyProgramSettings { TenantId = tenantId };
            ApplyRequest(settings, request);
            await _loyalty.AddSettingsAsync(settings, ct);
        }
        else
        {
            ApplyRequest(settings, request);
            _loyalty.UpdateSettings(settings);
        }

        await _loyalty.SaveChangesAsync(ct);
        return (ToSettingsDto(settings), null);
    }

    // ── Tier ladder — admin CRUD (TASK-615) ───────────────────────────────────

    public async Task<IReadOnlyList<LoyaltyTierDefinitionDto>> GetTierLadderAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        var ladder = await _loyalty.GetTierLadderAsync(tenantId, ct);
        return ladder.Select(ToTierDefinitionDto).ToList();
    }

    public async Task<(IReadOnlyList<LoyaltyTierDefinitionDto>? Tiers, string? Error)> UpsertTierLadderAsync(
        Guid tenantId, List<UpsertTierRequest> tiers, CancellationToken ct = default)
    {
        if (tiers is null)
            return (null, "Tiers is required.");

        foreach (var tier in tiers)
        {
            if (string.IsNullOrWhiteSpace(tier.Name))
                return (null, "Tier name is required.");
            if (tier.Name.Trim().Length > 100)
                return (null, "Tier name cannot exceed 100 characters.");
            if (tier.SortOrder < 0)
                return (null, "SortOrder cannot be negative.");
            if (tier.MinCompositeScore < 0)
                return (null, "MinCompositeScore cannot be negative.");
            if (tier.AccrualMultiplier is < 0 or > 999.99m)
                return (null, "AccrualMultiplier must be between 0 and 999.99.");
            if (tier.DiscountPercent is < 0 or > 100)
                return (null, "DiscountPercent must be between 0 and 100.");
        }

        if (tiers.Select(t => t.SortOrder).Distinct().Count() != tiers.Count)
            return (null, "SortOrder values must be unique.");

        // TASK-615: match submitted rows against existing ones by SortOrder — the ladder's
        // natural unique key — rather than blind delete-then-recreate, so a tier whose
        // SortOrder is unchanged keeps its database Id (see ILoyaltyService.UpsertTierLadderAsync
        // doc for the full rationale: preserves any LoyaltyMembership.CurrentTierId already
        // pointing at it until the next nightly recompute).
        var existing = await _loyalty.GetTierLadderAsync(tenantId, ct);
        var existingBySortOrder = existing.ToDictionary(t => t.SortOrder);
        var submittedSortOrders = tiers.Select(t => t.SortOrder).ToHashSet();

        foreach (var stale in existing.Where(t => !submittedSortOrders.Contains(t.SortOrder)))
            _loyalty.RemoveTier(stale);

        var result = new List<LoyaltyTierDefinition>(tiers.Count);
        foreach (var request in tiers)
        {
            if (existingBySortOrder.TryGetValue(request.SortOrder, out var tier))
            {
                tier.Name = request.Name.Trim();
                tier.MinCompositeScore = request.MinCompositeScore;
                tier.AccrualMultiplier = request.AccrualMultiplier;
                tier.DiscountPercent = request.DiscountPercent;
                tier.UpdatedAt = DateTimeOffset.UtcNow;
                _loyalty.UpdateTier(tier);
            }
            else
            {
                tier = new LoyaltyTierDefinition
                {
                    TenantId = tenantId,
                    Name = request.Name.Trim(),
                    SortOrder = request.SortOrder,
                    MinCompositeScore = request.MinCompositeScore,
                    AccrualMultiplier = request.AccrualMultiplier,
                    DiscountPercent = request.DiscountPercent,
                };
                await _loyalty.AddTierAsync(tier, ct);
            }
            result.Add(tier);
        }

        await _loyalty.SaveChangesAsync(ct);

        return (result.OrderBy(t => t.SortOrder).Select(ToTierDefinitionDto).ToList(), null);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// TASK-498 refactor: the actual LoyaltyMembership creation body shared by
    /// <see cref="JoinAsync"/> (consumer session, wrapped by the caller in
    /// <see cref="ITenantSessionOverride"/>) and <see cref="ResolveOrCreateMembershipByPhoneAsync"/>
    /// (staff session, runs directly in the caller's own tenant RLS context). Does NOT check for
    /// an existing membership — every call site is responsible for that idempotency check itself
    /// first, since the right way to read "does this pair already have a membership" differs by
    /// call site (ambient session vs. already-tenant-scoped session).
    /// </summary>
    private async Task<LoyaltyMembership> CreateMembershipCoreAsync(
        Guid tenantId, Guid consumerAccountId, string phone, string fullName, CancellationToken ct)
    {
        var customer = await FindOrCreateCustomerAsync(tenantId, phone, fullName, ct);

        var membership = new LoyaltyMembership
        {
            TenantId = tenantId,
            ConsumerAccountId = consumerAccountId,
            CustomerId = customer.Id,
            TotpSecret = _totp.GenerateSecret(),
            Balance = 0m,
            Status = LoyaltyMembershipStatus.Active,
        };

        await _loyalty.AddMembershipAsync(membership, ct);
        await _loyalty.SaveChangesAsync(ct);
        return membership;
    }

    private async Task<Customer> FindOrCreateCustomerAsync(
        Guid tenantId, string phone, string name, CancellationToken ct)
    {
        var customer = await _customers.FindByPhoneAsync(phone, tenantId, ct);
        if (customer is not null) return customer;

        customer = new Customer
        {
            TenantId = tenantId,
            Name = name,
            Phone = phone,
            Tags = ["loyalty"],
        };
        return await _customers.CreateAsync(customer, ct);
    }

    private async Task LogResolveFailureAsync(
        Guid tenantId, Guid staffUserId, Guid membershipId, bool justLockedOut, CancellationToken ct)
    {
        await _activityLogs.LogAsync(new ActivityLog
        {
            TenantId = tenantId,
            UserId = staffUserId,
            Action = justLockedOut ? "loyalty.resolve_code_locked_out" : "loyalty.resolve_code_failed",
            EntityType = "loyalty_membership",
            EntityId = membershipId,
        }, ct);
        await _activityLogs.SaveChangesAsync(ct);
    }

    /// <summary>Parses "SGLOY1.{membershipId}.{code}". False on any structural mismatch.</summary>
    private static bool TryParsePayload(string? scanned, out Guid membershipId, out string code)
    {
        membershipId = Guid.Empty;
        code = string.Empty;

        if (string.IsNullOrWhiteSpace(scanned)) return false;

        var parts = scanned.Trim().Split('.');
        if (parts.Length != 3) return false;
        if (!string.Equals(parts[0], ConsumerCodePrefix, StringComparison.Ordinal)) return false;
        if (!Guid.TryParse(parts[1], out membershipId)) return false;

        code = parts[2].Trim();
        return code.Length > 0;
    }

    /// <summary>Keeps only the last 4 digits visible, e.g. "+380••••••567".</summary>
    private static string MaskPhone(string phone) =>
        phone.Length <= 4 ? phone : new string('•', phone.Length - 4) + phone[^4..];

    private static void ApplyRequest(LoyaltyProgramSettings settings, UpsertLoyaltyProgramSettingsRequest request)
    {
        settings.IsEnabled = request.IsEnabled;
        settings.AccrualRatePercent = request.AccrualRatePercent;
        settings.RedemptionCapPercent = request.RedemptionCapPercent;
        settings.MinRedemptionBalance = request.MinRedemptionBalance;
        settings.CodeTtlSeconds = request.CodeTtlSeconds;
        settings.CustomerCodeFormat = request.CustomerCodeFormat;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// TASK-507: <paramref name="preferredStore"/> is the already-resolved
    /// <see cref="Location"/> for <c>m.PreferredStoreId</c>, or null when there isn't one to
    /// resolve (unset) or the caller didn't need/have one on hand (e.g. staff-facing call
    /// sites in this file that don't resolve it) — <c>PreferredStoreId</c> itself always comes
    /// straight from the entity either way; only the two display-convenience name/address
    /// fields depend on this parameter.
    /// </summary>
    private static LoyaltyMembershipSummaryDto ToSummaryDto(
        LoyaltyMembership m, string tenantName, Location? preferredStore = null) => new(
        m.Id, m.TenantId, tenantName, m.Balance, m.Status, m.JoinedAt,
        m.PreferredStoreId, preferredStore?.Name, preferredStore?.Address);

    private static LoyaltyLedgerEntryDto ToLedgerDto(LoyaltyLedgerEntry e) => new(
        e.Id, e.EntryType, e.Amount, e.BalanceAfter, e.Note, e.CreatedAt);

    private static LoyaltyProgramSettingsDto ToSettingsDto(LoyaltyProgramSettings s, bool isNew = false) => new(
        s.IsEnabled, s.AccrualRatePercent, s.RedemptionCapPercent, s.MinRedemptionBalance, s.CodeTtlSeconds,
        s.CustomerCodeFormat, isNew ? null : s.UpdatedAt);

    private static LoyaltyTierDefinitionDto ToTierDefinitionDto(LoyaltyTierDefinition t) => new(
        t.Id, t.Name, t.SortOrder, t.MinCompositeScore, t.AccrualMultiplier, t.DiscountPercent);

    private static LoyaltyTierChangeHistoryDto ToTierHistoryDto(LoyaltyTierChangeHistory h) => new(
        h.Id, h.FromTier?.Name, h.ToTier?.Name, h.FromScore, h.ToScore, h.ChangedAt);
}
