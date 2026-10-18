using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Registers;

public sealed partial class RegisterService : IRegisterService
{
    public const string PosModuleKey = "pos";

    // Lockout policy.
    //  * per user (PIN fields on User, separate from password lockout): 5 -> 15 min;
    //  * per register (device): 10 failures per 15 min window, never reset by a success; 50 per
    //    UTC day flips the register to 'locked' until a manager unlocks it.
    public const int MaxUserAttempts = 5;
    public static readonly TimeSpan UserLockout = TimeSpan.FromMinutes(15);
    public const int MaxRegisterAttempts = 10;
    public static readonly TimeSpan RegisterWindow = TimeSpan.FromMinutes(15);
    public const int MaxRegisterDailyAttempts = 50;
    public static readonly TimeSpan ActivationCodeTtl = TimeSpan.FromHours(24);

    /// <summary>PIN-only sign-in verifies against every PIN holder of the store (bcrypt each); above this cap the client must send cashierId.</summary>
    public const int MaxPinCandidates = 30;
    public const int MaxPinGenerationAttempts = 10;

    private const string InvalidDevice = "Invalid device credentials.";
    private const string InvalidPin = "Invalid PIN.";
    private const string LockedOut = "Too many failed attempts. Try again later.";
    private const string InvalidCode = "Invalid or expired activation code.";

    // Roles that may operate a till (mirrors AppPolicies.CanAccessPosRoles minus provider) and their rank.
    internal static readonly Dictionary<string, int> PosRoleRank = new()
    {
        [AppRoles.EnterpriseAdmin] = 4,
        [AppRoles.NetworkManager] = 3,
        [AppRoles.StoreManager] = 2,
        [AppRoles.Storekeeper] = 1,
        [AppRoles.Cashier] = 1,
    };

    /// <summary>True for roles that may work a till (shared with POS offline-sale attribution).</summary>
    public static bool IsPosRole(string role) => PosRoleRank.ContainsKey(role);

    [GeneratedRegex(@"^\d{4,6}$")]
    private static partial Regex PinRegex();

    private readonly ICashRegisterRepository _registers;
    private readonly IUserRepository _users;
    private readonly IUserLocationRepository _userLocations;
    private readonly ILocationRepository _locations;
    private readonly ITenantRepository _tenants;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtService _jwt;
    private readonly ITenantSessionOverride _tenantScope;
    private readonly IPinGenerator _pins;
    private readonly IOperatorSessionCache _sessionCache;
    private readonly ILogger<RegisterService> _log;

    public RegisterService(
        ICashRegisterRepository registers,
        IUserRepository users,
        IUserLocationRepository userLocations,
        ILocationRepository locations,
        ITenantRepository tenants,
        IPasswordHasher hasher,
        IJwtService jwt,
        ITenantSessionOverride tenantScope,
        IPinGenerator pins,
        IOperatorSessionCache sessionCache,
        ILogger<RegisterService> log)
    {
        _registers = registers;
        _users = users;
        _userLocations = userLocations;
        _locations = locations;
        _tenants = tenants;
        _hasher = hasher;
        _jwt = jwt;
        _tenantScope = tenantScope;
        _pins = pins;
        _sessionCache = sessionCache;
        _log = log;
    }

    // ── Manager CRUD (store-scoped) ────────────────────────────────────────

    public async Task<IReadOnlyList<RegisterDto>> ListAsync(
        RegisterCaller caller, Guid? storeId, CancellationToken ct = default)
    {
        switch (caller.Role)
        {
            case AppRoles.StoreManager:
            case AppRoles.NetworkManager:
                var mine = await AssignedStoreIdsAsync(caller, ct);
                var all = await _registers.ListAsync(storeId, ct);
                return all.Where(r => mine.Contains(r.StoreId)).Select(RegisterDto.From).ToList();
            case AppRoles.EnterpriseAdmin:
            case AppRoles.Provider:
                break;
            default:
                return [];
        }

        return (await _registers.ListAsync(storeId, ct)).Select(RegisterDto.From).ToList();
    }

    public async Task<(RegisterCreatedDto? Result, string? Error, int? StatusCode)> CreateAsync(
        RegisterCaller caller, CreateRegisterRequest request, CancellationToken ct = default)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 100)
            return (null, "Name is required (max 100 characters).", 400);

        var type = request.Type ?? CashRegister.TypeLine;
        if (type != CashRegister.TypeLine && type != CashRegister.TypeSelfCheckout)
            return (null, "Type must be 'line' or 'self_checkout'.", 400);

        if (await _locations.GetByIdAsync(request.StoreId, ct) is null)
            return (null, "Store not found.", 400);

        if (!await CanManageStoreAsync(caller, request.StoreId, ct))
            return (null, "You can only manage registers of stores assigned to you.", 403);

        int number;
        if (request.Number.HasValue)
        {
            if (request.Number.Value <= 0)
                return (null, "Number must be positive.", 400);
            if (await _registers.NumberExistsAsync(caller.TenantId, request.Number.Value, ct))
                return (null, $"Register number {request.Number.Value} is already used.", 409);
            number = request.Number.Value;
        }
        else
        {
            number = await _registers.GetNextNumberAsync(caller.TenantId, ct);
        }

        var register = new CashRegister
        {
            TenantId = caller.TenantId,
            StoreId = request.StoreId,
            Number = number,
            Type = type,
            Name = name,
        };

        var (code, expires) = NewActivationFor(caller.TenantId, register);

        await _registers.AddAsync(register, ct);
        await _registers.SaveChangesAsync(ct);

        var created = await _registers.GetByIdAsync(register.Id, ct) ?? register;
        return (new RegisterCreatedDto(RegisterDto.From(created), code, expires), null, null);
    }

    public async Task<(RegisterDto? Result, string? Error, int? StatusCode)> UpdateAsync(
        RegisterCaller caller, Guid registerId, UpdateRegisterRequest request, CancellationToken ct = default)
    {
        var (register, error, status) = await LoadManagedRegisterAsync(caller, registerId, ct);
        if (register is null) return (null, error, status);
        if (register.Status == CashRegister.StatusRevoked)
            return (null, "Register is revoked.", 409);

        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (name.Length is 0 or > 100) return (null, "Name must be 1-100 characters.", 400);
            register.Name = name;
        }

        if (request.StoreId.HasValue && request.StoreId.Value != register.StoreId)
        {
            if (await _locations.GetByIdAsync(request.StoreId.Value, ct) is null)
                return (null, "Store not found.", 400);
            // The caller must manage the destination store too (a store manager cannot move a till elsewhere).
            if (!await CanManageStoreAsync(caller, request.StoreId.Value, ct))
                return (null, "You can only manage registers of stores assigned to you.", 403);
            register.StoreId = request.StoreId.Value;
        }

        await _registers.SaveChangesAsync(ct);
        var fresh = await _registers.GetByIdAsync(registerId, ct) ?? register;
        return (RegisterDto.From(fresh), null, null);
    }

    public async Task<(ActivationCodeDto? Result, string? Error, int? StatusCode)> IssueActivationCodeAsync(
        RegisterCaller caller, Guid registerId, CancellationToken ct = default)
    {
        var (register, error, status) = await LoadManagedRegisterAsync(caller, registerId, ct);
        if (register is null) return (null, error, status);
        if (register.Status == CashRegister.StatusRevoked)
            return (null, "Register is revoked.", 409);

        var (code, expires) = NewActivationFor(caller.TenantId, register);
        await _registers.SaveChangesAsync(ct);
        return (new ActivationCodeDto(code, expires), null, null);
    }

    public async Task<(bool Ok, string? Error, int? StatusCode)> RevokeAsync(
        RegisterCaller caller, Guid registerId, CancellationToken ct = default)
    {
        var (register, error, status) = await LoadManagedRegisterAsync(caller, registerId, ct);
        if (register is null) return (false, error, status);

        register.Revoke();
        foreach (var s in await _registers.GetOpenSessionsAsync(registerId, ct))
        {
            s.EndedAt = DateTime.UtcNow;
            s.EndReason = RegisterSession.EndReasonRevoked;
            _sessionCache.Invalidate(s.Id);
        }

        await _registers.SaveChangesAsync(ct);
        _log.LogInformation("Register {RegisterId} revoked", registerId);
        return (true, null, null);
    }

    public async Task<(bool Ok, string? Error, int? StatusCode)> UnlockAsync(
        RegisterCaller caller, Guid registerId, CancellationToken ct = default)
    {
        var (register, error, status) = await LoadManagedRegisterAsync(caller, registerId, ct);
        if (register is null) return (false, error, status);
        if (register.Status == CashRegister.StatusRevoked)
            return (false, "Register is revoked.", 409);

        register.Unlock();
        await _registers.SaveChangesAsync(ct);
        return (true, null, null);
    }

    // ── Activation (anonymous) ─────────────────────────────────────────────

    public async Task<(ActivateRegisterResponse? Result, string? Error, int? StatusCode)> ActivateAsync(
        ActivateRegisterRequest request, CancellationToken ct = default)
    {
        // One answer for every failure (bad format, unknown/used/expired code, module off): the
        // endpoint must not tell an anonymous caller anything about tenants or module state.
        if (!RegisterCredentials.TryParseActivationCode(request.ActivationCode, out var tenantId, out var code))
            return (null, InvalidCode, 400);

        return await _tenantScope.ExecuteAsync<(ActivateRegisterResponse? Result, string? Error, int? StatusCode)>(tenantId, async () =>
        {
            var codeHash = RegisterCredentials.Hash(code);
            var register = await _registers.GetByActivationCodeHashAsync(codeHash, ct);
            if (register is null
                || register.Status == CashRegister.StatusRevoked
                || register.ActivationCodeExpiresAt is null
                || register.ActivationCodeExpiresAt <= DateTime.UtcNow)
                return (null, InvalidCode, 400);

            if (!await IsPosEnabledAsync(tenantId, ct))
                return (null, InvalidCode, 400);

            var secret = RegisterCredentials.NewDeviceTokenSecret();
            var activated = await _registers.TryActivateAsync(
                register.Id, codeHash, RegisterCredentials.Hash(secret), DateTime.UtcNow, ct);
            if (!activated)
                return (null, InvalidCode, 400); // lost the single-use race

            if (!string.IsNullOrWhiteSpace(request.AppVersion))
            {
                // Reload: ExecuteUpdate bypassed the tracker.
                var fresh = await _registers.GetByIdAsync(register.Id, ct);
                if (fresh is not null)
                {
                    fresh.Touch(request.AppVersion);
                    await _registers.SaveChangesAsync(ct);
                }
            }

            _log.LogInformation("Register {RegisterId} activated (tenant {TenantId})", register.Id, tenantId);

            return (new ActivateRegisterResponse(
                DeviceToken: RegisterCredentials.FormatDeviceToken(tenantId, register.Id, secret),
                RegisterId: register.Id,
                Number: register.Number,
                Name: register.Name,
                Type: register.Type,
                StoreId: register.StoreId,
                StoreName: register.Store?.Name), null, null);
        }, ct);
    }

    // ── PIN session (anonymous, device-token authenticated) ────────────────

    public async Task<(RegisterSessionResponse? Result, string? Error, int? StatusCode)> StartSessionAsync(
        string? deviceToken, StartSessionRequest request, CancellationToken ct = default)
    {
        if (!RegisterCredentials.TryParseDeviceToken(deviceToken, out var tenantId, out var registerId, out var secret))
            return (null, InvalidDevice, 401);

        if (request.Pin is null || !PinRegex().IsMatch(request.Pin))
            return (null, "PIN must be 4-6 digits.", 400);

        return await _tenantScope.ExecuteAsync<(RegisterSessionResponse? Result, string? Error, int? StatusCode)>(tenantId, async () =>
        {
            var register = await _registers.GetByIdAsync(registerId, ct);
            if (register is null
                || (register.Status != CashRegister.StatusActive && register.Status != CashRegister.StatusLocked)
                || !RegisterCredentials.HashesEqual(register.DeviceTokenHash, RegisterCredentials.Hash(secret)))
                return (null, InvalidDevice, 401);

            if (!await IsPosEnabledAsync(tenantId, ct))
                return (null, "Module not activated", 403);

            var now = DateTime.UtcNow;
            if (register.Status == CashRegister.StatusLocked
                || register.IsPinLockedOut(MaxRegisterAttempts, RegisterWindow, now))
            {
                DummyVerify(request.Pin);
                return (null, LockedOut, 423);
            }

            User? operatorUser;
            if (request.CashierId.HasValue)
            {
                operatorUser = await ResolveByCashierIdAsync(register, request.CashierId.Value, request.Pin, ct);
            }
            else
            {
                var candidates = (await _registers.GetPinUsersByStoreAsync(register.StoreId, ct))
                    .Where(IsEligiblePosUser).ToList();
                if (candidates.Count > MaxPinCandidates)
                    return (null, "Select the cashier to sign in (PIN-only sign-in is unavailable for this store).", 400);

                operatorUser = ResolveByPin(candidates, request.Pin);
            }

            if (operatorUser is null)
            {
                // Persist any per-user counter, then record the device-level failure atomically.
                await _registers.SaveChangesAsync(ct);
                var outcome = await _registers.RegisterPinFailureAsync(
                    register.Id, now, RegisterWindow, MaxRegisterDailyAttempts, ct);
                if (outcome.Status == CashRegister.StatusLocked)
                    _log.LogWarning("Register {RegisterId} locked: daily PIN-failure cap reached", register.Id);

                return outcome.Status == CashRegister.StatusLocked || outcome.WindowCount >= MaxRegisterAttempts
                    ? (null, LockedOut, 423)
                    : (null, InvalidPin, 401);
            }

            // NB: a success deliberately does NOT reset the register's failure counters.
            operatorUser.ResetPosPinLockout(); // PIN lockout only; the password lockout is untouched
            register.Touch(request.AppVersion);

            // One active operator per register: a new sign-in replaces the previous session.
            foreach (var old in await _registers.GetOpenSessionsAsync(register.Id, ct))
            {
                old.EndedAt = DateTime.UtcNow;
                old.EndReason = RegisterSession.EndReasonReplaced;
                _sessionCache.Invalidate(old.Id);
            }

            var session = new RegisterSession
            {
                TenantId = tenantId,
                RegisterId = register.Id,
                CashierId = operatorUser.Id,
            };
            await _registers.AddSessionAsync(session, ct);
            await _registers.SaveChangesAsync(ct);

            var (token, expiresAt) = _jwt.GenerateOperatorToken(
                operatorUser.Id, operatorUser.Email, operatorUser.FullName,
                tenantId, register.StoreId, register.Id, session.Id);

            return (new RegisterSessionResponse(
                token, expiresAt, session.Id, register.Id, register.StoreId,
                operatorUser.Id, operatorUser.FullName), null, null);
        }, ct);
    }

    public async Task<(bool Ok, string? Error, int? StatusCode)> EndSessionAsync(
        Guid sessionId, Guid registerId, string? reason, CancellationToken ct = default)
    {
        var session = await _registers.GetSessionAsync(sessionId, ct);
        if (session is null || session.RegisterId != registerId)
            return (false, "Session not found.", 404);

        if (session.EndedAt is null)
        {
            session.EndedAt = DateTime.UtcNow;
            session.EndReason = reason == RegisterSession.EndReasonIdle
                ? RegisterSession.EndReasonIdle
                : RegisterSession.EndReasonLogout;
            await _registers.SaveChangesAsync(ct);
        }

        _sessionCache.Invalidate(sessionId);
        return (true, null, null);
    }

    // ── PIN management ─────────────────────────────────────────────────────

    public async Task<(SetPinResult? Result, string? Error, int? StatusCode)> SetPinAsync(
        RegisterCaller caller, Guid userId, CancellationToken ct = default)
    {
        var (user, error, status) = await LoadManagedUserAsync(caller, userId, ct);
        if (user is null) return (null, error, status);

        var storeId = user.StoreId!.Value; // guaranteed by LoadManagedUserAsync
        var peers = (await _registers.GetPinUsersByStoreAsync(storeId, ct))
            .Where(p => p.Id != user.Id && p.PosPinHash is not null).ToList();

        // The server picks the PIN and silently retries on a collision, so this endpoint can never
        // be used as an oracle for which PINs other employees already hold.
        for (var attempt = 0; attempt < MaxPinGenerationAttempts; attempt++)
        {
            var pin = _pins.Next();
            if (peers.Any(p => _hasher.Verify(pin, p.PosPinHash!))) continue;

            user.SetPosPin(_hasher.Hash(pin)); // clears PIN lockout only
            _users.Update(user);
            await _users.SaveChangesAsync(ct);
            return (new SetPinResult(pin), null, null);
        }

        return (null, "Could not generate a PIN, please try again.", 503);
    }

    public async Task<(bool Ok, string? Error, int? StatusCode)> ClearPinAsync(
        RegisterCaller caller, Guid userId, CancellationToken ct = default)
    {
        var (user, error, status) = await LoadManagedUserAsync(caller, userId, ct);
        if (user is null) return (false, error, status);

        user.ClearPosPin();
        _users.Update(user);
        await _users.SaveChangesAsync(ct);
        return (true, null, null);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// store_manager / network_manager: only stores assigned to them (user_locations + primary
    /// store, see <see cref="AssignedStoreIdsAsync"/>); enterprise_admin/provider: any store of
    /// the tenant (RLS bounds the tenant).
    /// </summary>
    private async Task<bool> CanManageStoreAsync(RegisterCaller caller, Guid? storeId, CancellationToken ct)
    {
        if (storeId is null) return false;
        switch (caller.Role)
        {
            case AppRoles.EnterpriseAdmin:
            case AppRoles.Provider:
                return true;
            case AppRoles.NetworkManager:
            case AppRoles.StoreManager:
                return (await AssignedStoreIdsAsync(caller, ct)).Contains(storeId.Value);
            default:
                return false;
        }
    }

    /// <summary>
    /// Stores a store-scoped manager is assigned to: their <c>user_locations</c> rows (ADR-022,
    /// a manager may run several stores) plus their primary <c>StoreId</c>.
    /// </summary>
    internal async Task<HashSet<Guid>> AssignedStoreIdsAsync(RegisterCaller caller, CancellationToken ct)
    {
        var ids = (await _userLocations.GetLocationIdsForUserAsync(caller.TenantId, caller.UserId, ct)).ToHashSet();
        if (caller.StoreId is { } own) ids.Add(own);
        return ids;
    }

    private async Task<(CashRegister? Register, string? Error, int? Status)> LoadManagedRegisterAsync(
        RegisterCaller caller, Guid registerId, CancellationToken ct)
    {
        var register = await _registers.GetByIdAsync(registerId, ct);
        // 404 (not 403) for a store the caller cannot manage: do not confirm the register exists.
        if (register is null || !await CanManageStoreAsync(caller, register.StoreId, ct))
            return (null, "Register not found.", 404);
        return (register, null, null);
    }

    private async Task<(User? User, string? Error, int? Status)> LoadManagedUserAsync(
        RegisterCaller caller, Guid userId, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null || user.TenantId is null || !user.IsActive)
            return (null, "Employee not found.", 404);

        // Only people who can actually work a till get a POS PIN (no merchandiser/staff/provider
        // team/supplier accounts), and never someone outranking the caller.
        if (!PosRoleRank.TryGetValue(user.Role, out var targetRank))
            return (null, "This employee's role cannot use a POS PIN.", 400);
        var callerRank = PosRoleRank.GetValueOrDefault(caller.Role, caller.Role == AppRoles.Provider ? int.MaxValue : 0);
        if (targetRank > callerRank)
            return (null, "You cannot manage the PIN of an employee with a higher role.", 403);

        if (user.StoreId is null)
            return (null, "Employee has no store assigned; a POS PIN is unique per store.", 400);

        if (!await CanManageStoreAsync(caller, user.StoreId, ct))
            return (null, "You can only manage PINs of employees in your own stores.", 403);

        return (user, null, null);
    }

    private (string Code, DateTime ExpiresAt) NewActivationFor(Guid tenantId, CashRegister register)
    {
        var (_, display) = RegisterCredentials.NewActivationCode();
        var normalized = display.Replace("-", string.Empty);
        var expires = DateTime.UtcNow.Add(ActivationCodeTtl);
        register.IssueActivationCode(RegisterCredentials.Hash(normalized), expires);
        return (RegisterCredentials.FormatActivationCode(tenantId, display), expires);
    }

    private async Task<bool> IsPosEnabledAsync(Guid tenantId, CancellationToken ct)
    {
        var tenant = await _tenants.GetByIdAsync(tenantId, ct);
        return tenant is not null && tenant.IsActive && tenant.HasModule(PosModuleKey);
    }

    private static bool IsEligiblePosUser(User u) =>
        u.IsActive && u.PosPinHash is not null && PosRoleRank.ContainsKey(u.Role);

    private async Task<User?> ResolveByCashierIdAsync(CashRegister register, Guid cashierId, string pin, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(cashierId, ct);
        var eligible = user is not null && IsEligiblePosUser(user) && user.StoreId == register.StoreId;

        if (!eligible || user!.IsPosPinLockedOut)
        {
            DummyVerify(pin);
            return null;
        }

        if (_hasher.Verify(pin, user.PosPinHash!))
            return user;

        user.RegisterFailedPosPin(MaxUserAttempts, UserLockout);
        return null;
    }

    private User? ResolveByPin(IReadOnlyList<User> candidates, string pin)
    {
        User? match = null;
        var verified = 0;

        foreach (var candidate in candidates)
        {
            if (candidate.IsPosPinLockedOut) continue;
            verified++;
            if (match is null && _hasher.Verify(pin, candidate.PosPinHash!))
                match = candidate;
        }

        if (verified == 0) DummyVerify(pin);
        return match;
    }

    // Keeps the no-candidate path roughly as slow as a real verification (no user-enumeration timing).
    private static string? _dummyHash;

    private void DummyVerify(string pin)
    {
        try
        {
            _dummyHash ??= _hasher.Hash(Guid.NewGuid().ToString("N"));
            _hasher.Verify(pin, _dummyHash);
        }
        catch { /* must never leak */ }
    }
}
