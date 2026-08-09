using Microsoft.Extensions.Logging;
using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.Loyalty.Dtos;
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
    private readonly IPasswordHasher _hasher;
    private readonly ITotpService _totp;
    private readonly IResolveCodeAttemptTracker _attempts;
    private readonly IActivityLogRepository _activityLogs;
    private readonly ITenantSessionOverride _tenantScope;
    private readonly ILogger<LoyaltyService> _logger;

    public LoyaltyService(
        ILoyaltyRepository loyalty,
        ICustomerRepository customers,
        ITenantRepository tenants,
        IUserRepository users,
        IConsumerAccountRepository consumerAccounts,
        IPasswordHasher hasher,
        ITotpService totp,
        IResolveCodeAttemptTracker attempts,
        IActivityLogRepository activityLogs,
        ITenantSessionOverride tenantScope,
        ILogger<LoyaltyService> logger)
    {
        _loyalty = loyalty;
        _customers = customers;
        _tenants = tenants;
        _users = users;
        _consumerAccounts = consumerAccounts;
        _hasher = hasher;
        _totp = totp;
        _attempts = attempts;
        _activityLogs = activityLogs;
        _tenantScope = tenantScope;
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
            return (ToSummaryDto(existing, tenant.Name), null, null);

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
        return memberships.Select(m => ToSummaryDto(m, m.Tenant?.Name ?? "—")).ToList();
    }

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

    private static LoyaltyMembershipSummaryDto ToSummaryDto(LoyaltyMembership m, string tenantName) => new(
        m.Id, m.TenantId, tenantName, m.Balance, m.Status, m.JoinedAt);

    private static LoyaltyLedgerEntryDto ToLedgerDto(LoyaltyLedgerEntry e) => new(
        e.Id, e.EntryType, e.Amount, e.BalanceAfter, e.Note, e.CreatedAt);

    private static LoyaltyProgramSettingsDto ToSettingsDto(LoyaltyProgramSettings s, bool isNew = false) => new(
        s.IsEnabled, s.AccrualRatePercent, s.RedemptionCapPercent, s.MinRedemptionBalance, s.CodeTtlSeconds,
        s.CustomerCodeFormat, isNew ? null : s.UpdatedAt);
}
