using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ShelfGuard.Application.Features.Registers;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Registers;

// ── Fakes ──────────────────────────────────────────────────────────────────

internal sealed class FakeRegisterRepo : ICashRegisterRepository
{
    public List<CashRegister> Registers { get; } = [];
    public List<RegisterSession> Sessions { get; } = [];
    public List<User> Users { get; } = [];

    public Task<CashRegister?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Registers.FirstOrDefault(r => r.Id == id));

    public Task<CashRegister?> GetByActivationCodeHashAsync(string codeHash, CancellationToken ct = default) =>
        Task.FromResult(Registers.FirstOrDefault(r => r.ActivationCodeHash == codeHash));

    public Task<IReadOnlyList<CashRegister>> ListAsync(Guid? storeId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CashRegister>>(
            Registers.Where(r => !storeId.HasValue || r.StoreId == storeId).ToList());

    public Task<int> GetNextNumberAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(Registers.Count == 0 ? 1 : Registers.Max(r => r.Number) + 1);

    public Task<bool> NumberExistsAsync(Guid tenantId, int number, CancellationToken ct = default) =>
        Task.FromResult(Registers.Any(r => r.Number == number));

    public Task AddAsync(CashRegister register, CancellationToken ct = default)
    {
        Registers.Add(register);
        return Task.CompletedTask;
    }

    // Mirrors the SQL guard in CashRegisterRepository.TryActivateAsync (single-use + expiry).
    public Task<bool> TryActivateAsync(
        Guid registerId, string codeHash, string deviceTokenHash, DateTime nowUtc, CancellationToken ct = default)
    {
        var r = Registers.FirstOrDefault(x => x.Id == registerId);
        if (r is null || r.Status == CashRegister.StatusRevoked || r.ActivationCodeHash != codeHash
            || r.ActivationCodeExpiresAt is null || r.ActivationCodeExpiresAt <= nowUtc)
            return Task.FromResult(false);

        // Status/DeviceTokenHash have private setters: emulate the UPDATE via the domain surface.
        typeof(CashRegister).GetProperty(nameof(CashRegister.Status))!.SetValue(r, CashRegister.StatusActive);
        typeof(CashRegister).GetProperty(nameof(CashRegister.DeviceTokenHash))!.SetValue(r, deviceTokenHash);
        typeof(CashRegister).GetProperty(nameof(CashRegister.ActivationCodeHash))!.SetValue(r, null);
        typeof(CashRegister).GetProperty(nameof(CashRegister.ActivationCodeExpiresAt))!.SetValue(r, null);
        return Task.FromResult(true);
    }

    public Task<PinFailureOutcome> RegisterPinFailureAsync(
        Guid registerId, DateTime nowUtc, TimeSpan window, int maxDaily, CancellationToken ct = default)
    {
        var r = Registers.Single(x => x.Id == registerId);
        var (w, d) = r.RegisterPinFailure(nowUtc, window, maxDaily);
        return Task.FromResult(new PinFailureOutcome(w, d, r.Status));
    }

    public Task<bool> HadSessionAtAsync(
        Guid registerId, Guid cashierId, DateTime at, TimeSpan endTolerance, CancellationToken ct = default) =>
        Task.FromResult(Sessions.Any(s => s.RegisterId == registerId && s.CashierId == cashierId
            && s.StartedAt <= at && (s.EndedAt == null || s.EndedAt >= at - endTolerance)));

    public Task AddSessionAsync(RegisterSession session, CancellationToken ct = default)
    {
        Sessions.Add(session);
        return Task.CompletedTask;
    }

    public Task<RegisterSession?> GetSessionAsync(Guid sessionId, CancellationToken ct = default) =>
        Task.FromResult(Sessions.FirstOrDefault(s => s.Id == sessionId));

    public Task<IReadOnlyList<RegisterSession>> GetOpenSessionsAsync(Guid registerId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RegisterSession>>(
            Sessions.Where(s => s.RegisterId == registerId && s.EndedAt is null).ToList());

    public Task<IReadOnlyList<User>> GetPinUsersByStoreAsync(Guid storeId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<User>>(
            Users.Where(u => u.StoreId == storeId && u.IsActive && u.PosPinHash != null).ToList());

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class FakeUserRepo : IUserRepository
{
    private readonly FakeRegisterRepo _shared;
    public FakeUserRepo(FakeRegisterRepo shared) => _shared = shared;

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_shared.Users.FirstOrDefault(u => u.Id == id));

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) => Task.FromResult<User?>(null);
    public Task<User?> GetByPhoneAsync(string phone, CancellationToken ct = default) => Task.FromResult<User?>(null);
    public Task<IReadOnlyList<User>> GetAllByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<User>>(_shared.Users);
    public Task<IReadOnlyList<User>> GetByRolesAsync(IEnumerable<string> roles, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<User>>([]);
    public Task AddAsync(User user, CancellationToken ct = default) { _shared.Users.Add(user); return Task.CompletedTask; }
    public void Update(User user) { }
    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Runs the action directly (the real one only adds a tenant-scoped DB transaction).</summary>
internal sealed class QueuePinGenerator : IPinGenerator
{
    private readonly Queue<string> _pins;
    public QueuePinGenerator(params string[] pins) => _pins = new Queue<string>(pins);
    public string Next() => _pins.Count > 0 ? _pins.Dequeue() : "999999";
}

internal sealed class PassThroughScope : ITenantSessionOverride
{
    public Task<T> ExecuteAsync<T>(Guid tenantId, Func<Task<T>> action, CancellationToken ct = default) => action();
}

/// <summary>Fast deterministic stand-in for bcrypt.</summary>
internal sealed class PlainHasher : IPasswordHasher
{
    public string Hash(string password) => "h:" + password;
    public bool Verify(string password, string hash) => hash == "h:" + password;
}

// ── Tests ──────────────────────────────────────────────────────────────────

public sealed class RegisterServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid StoreId = Guid.NewGuid();
    private static readonly Guid OtherStoreId = Guid.NewGuid();

    private static RegisterCaller Admin => new(TenantId, Guid.NewGuid(), AppRoles.EnterpriseAdmin, null);
    private static RegisterCaller StoreManager(Guid? store = null) =>
        new(TenantId, Guid.NewGuid(), AppRoles.StoreManager, store ?? StoreId);

    private sealed class Harness
    {
        public FakeRegisterRepo Repo { get; } = new();
        public RegisterService Service { get; }
        public IJwtService Jwt { get; } = Substitute.For<IJwtService>();
        public IUserLocationRepository UserLocations { get; } = Substitute.For<IUserLocationRepository>();
        public IOperatorSessionCache SessionCache { get; } = Substitute.For<IOperatorSessionCache>();

        public Harness(bool posEnabled = true, IPinGenerator? pins = null)
        {
            var tenants = Substitute.For<ITenantRepository>();
            var tenant = Tenant.Create("Reg Test", "reg-test-" + Guid.NewGuid().ToString("N"));
            tenant.UpdateModules(posEnabled ? ["inventory", "pos"] : ["inventory"]);
            tenants.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(tenant);

            var locations = Substitute.For<ILocationRepository>();
            locations.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(ci => new Location { Id = ci.Arg<Guid>(), TenantId = TenantId, Name = "Store" });

            Jwt.GenerateOperatorToken(default, default!, default, default, default, default, default)
                .ReturnsForAnyArgs(("operator-jwt", DateTime.UtcNow.AddHours(8)));

            Service = new RegisterService(
                Repo, new FakeUserRepo(Repo), UserLocations, locations, tenants, new PlainHasher(), Jwt,
                new PassThroughScope(), pins ?? new RandomPinGenerator(), SessionCache,
                NullLogger<RegisterService>.Instance);
        }

        public User AddCashier(string pin, string email = "c@x.test", Guid? storeId = null, string role = AppRoles.Cashier)
        {
            var u = User.Create(TenantId, email, "Cashier " + email, "pw", role, storeId ?? StoreId);
            u.SetPosPin("h:" + pin);
            Repo.Users.Add(u);
            return u;
        }

        public User AddUserWithoutPin(string email, string role = AppRoles.Cashier, Guid? storeId = null)
        {
            var u = User.Create(TenantId, email, "U " + email, "pw", role, storeId ?? StoreId);
            Repo.Users.Add(u);
            return u;
        }

        /// <summary>Creates + activates a register; returns its device token.</summary>
        public async Task<(CashRegister Register, string DeviceToken)> ActiveRegisterAsync()
        {
            var (created, _, _) = await Service.CreateAsync(Admin, new CreateRegisterRequest(StoreId, "Каса 1"));
            var (act, error, _) = await Service.ActivateAsync(new ActivateRegisterRequest(created!.ActivationCode));
            Assert.Null(error);
            return (Repo.Registers.Single(r => r.Id == created.Register.Id), act!.DeviceToken);
        }
    }

    // ── Activation code ────────────────────────────────────────────────────

    [Fact]
    public async Task Activation_code_is_single_use()
    {
        var h = new Harness();
        var (created, err, _) = await h.Service.CreateAsync(Admin, new CreateRegisterRequest(StoreId, "Каса 1"));
        Assert.Null(err);

        var (first, e1, _) = await h.Service.ActivateAsync(new ActivateRegisterRequest(created!.ActivationCode));
        var (second, e2, s2) = await h.Service.ActivateAsync(new ActivateRegisterRequest(created.ActivationCode));

        Assert.Null(e1);
        Assert.NotNull(first);
        Assert.StartsWith("sgr1.", first.DeviceToken);
        Assert.Null(second);
        Assert.Equal(400, s2);
        Assert.NotNull(e2);

        var reg = h.Repo.Registers.Single();
        Assert.Equal(CashRegister.StatusActive, reg.Status);
        Assert.Null(reg.ActivationCodeHash);
        Assert.DoesNotContain(first.DeviceToken, reg.DeviceTokenHash!);
    }

    [Fact]
    public async Task Expired_activation_code_is_rejected()
    {
        var h = new Harness();
        var (created, _, _) = await h.Service.CreateAsync(Admin, new CreateRegisterRequest(StoreId, "Каса 1"));
        var reg = h.Repo.Registers.Single();
        reg.IssueActivationCode(reg.ActivationCodeHash!, DateTime.UtcNow.AddMinutes(-1));

        var (result, error, status) = await h.Service.ActivateAsync(new ActivateRegisterRequest(created!.ActivationCode));

        Assert.Null(result);
        Assert.Equal(400, status);
        Assert.NotNull(error);
        Assert.Equal(CashRegister.StatusPending, reg.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("00000000000000000000000000000000.ABCD-EFGH")]
    public async Task Malformed_or_unknown_activation_code_is_rejected(string code)
    {
        var h = new Harness();
        var (result, _, status) = await h.Service.ActivateAsync(new ActivateRegisterRequest(code));
        Assert.Null(result);
        Assert.Equal(400, status);
    }

    [Fact]
    public async Task Reissued_code_replaces_previous_one()
    {
        var h = new Harness();
        var (created, _, _) = await h.Service.CreateAsync(Admin, new CreateRegisterRequest(StoreId, "Каса 1"));
        var (reissued, _, _) = await h.Service.IssueActivationCodeAsync(Admin, created!.Register.Id);

        var (old, _, oldStatus) = await h.Service.ActivateAsync(new ActivateRegisterRequest(created.ActivationCode));
        var (fresh, _, _) = await h.Service.ActivateAsync(new ActivateRegisterRequest(reissued!.ActivationCode));

        Assert.Null(old);
        Assert.Equal(400, oldStatus);
        Assert.NotNull(fresh);
    }

    [Fact]
    public async Task Activation_failure_is_identical_whether_code_is_bad_or_pos_module_is_off()
    {
        var off = new Harness(posEnabled: false);
        var (created, _, _) = await off.Service.CreateAsync(Admin, new CreateRegisterRequest(StoreId, "Каса 1"));

        var moduleOff = await off.Service.ActivateAsync(new ActivateRegisterRequest(created!.ActivationCode));
        var badCode = await off.Service.ActivateAsync(
            new ActivateRegisterRequest(RegisterCredentials.FormatActivationCode(TenantId, "ABCD-EFGH")));

        Assert.Null(moduleOff.Result);
        Assert.Equal(400, moduleOff.StatusCode);
        Assert.Equal(badCode.StatusCode, moduleOff.StatusCode);
        Assert.Equal(badCode.Error, moduleOff.Error); // no module-state oracle for anonymous callers
    }

    // ── PIN session ────────────────────────────────────────────────────────

    [Fact]
    public async Task Correct_pin_only_signin_issues_operator_token_and_opens_session()
    {
        var h = new Harness();
        var cashier = h.AddCashier("1234");
        var (reg, token) = await h.ActiveRegisterAsync();

        var (res, error, _) = await h.Service.StartSessionAsync(token, new StartSessionRequest("1234"));

        Assert.Null(error);
        Assert.Equal("operator-jwt", res!.AccessToken);
        Assert.Equal(cashier.Id, res.CashierId);
        Assert.Equal(reg.Id, res.RegisterId);
        var session = Assert.Single(h.Repo.Sessions);
        Assert.Equal(cashier.Id, session.CashierId);
        Assert.Null(session.EndedAt);
        h.Jwt.Received(1).GenerateOperatorToken(
            cashier.Id, cashier.Email, cashier.FullName, TenantId, StoreId, reg.Id, session.Id);
    }

    [Fact]
    public async Task New_signin_replaces_the_previous_operator_session_and_invalidates_its_cache()
    {
        var h = new Harness();
        h.AddCashier("1111", "a@x.test");
        h.AddCashier("2222", "b@x.test");
        var (_, token) = await h.ActiveRegisterAsync();

        await h.Service.StartSessionAsync(token, new StartSessionRequest("1111"));
        var firstId = h.Repo.Sessions[0].Id;
        await h.Service.StartSessionAsync(token, new StartSessionRequest("2222"));

        Assert.Equal(2, h.Repo.Sessions.Count);
        Assert.Equal(RegisterSession.EndReasonReplaced, h.Repo.Sessions[0].EndReason);
        Assert.Null(h.Repo.Sessions[1].EndedAt);
        h.SessionCache.Received(1).Invalidate(firstId);
    }

    [Fact]
    public async Task Pin_failures_lock_the_user_with_PIN_fields_and_never_touch_password_lockout()
    {
        var h = new Harness();
        var cashier = h.AddCashier("1234");
        cashier.RegisterFailedLogin(3, TimeSpan.FromMinutes(15)); // one prior password failure
        var (_, token) = await h.ActiveRegisterAsync();

        for (var i = 0; i < RegisterService.MaxUserAttempts; i++)
        {
            var (r, _, s) = await h.Service.StartSessionAsync(token, new StartSessionRequest("9999", cashier.Id));
            Assert.Null(r);
            Assert.True(s is 401 or 423);
        }

        Assert.True(cashier.IsPosPinLockedOut);
        Assert.Equal(1, cashier.FailedLoginAttempts); // password counter untouched
        Assert.False(cashier.IsLockedOut);

        var (res, _, _) = await h.Service.StartSessionAsync(token, new StartSessionRequest("1234", cashier.Id));
        Assert.Null(res); // PIN-locked even for the right PIN
        Assert.Empty(h.Repo.Sessions);
    }

    [Fact]
    public async Task Pin_success_and_SetPin_never_clear_a_password_lockout()
    {
        var h = new Harness(pins: new QueuePinGenerator("424242"));
        var cashier = h.AddCashier("1234");
        cashier.RegisterFailedLogin(1, TimeSpan.FromMinutes(15)); // password-locked
        Assert.True(cashier.IsLockedOut);
        var (_, token) = await h.ActiveRegisterAsync();

        var signin = await h.Service.StartSessionAsync(token, new StartSessionRequest("1234", cashier.Id));
        Assert.NotNull(signin.Result);
        Assert.True(cashier.IsLockedOut);

        var set = await h.Service.SetPinAsync(Admin, cashier.Id);
        Assert.NotNull(set.Result);
        Assert.True(cashier.IsLockedOut);
    }

    [Fact]
    public async Task Successful_signin_clears_the_users_PIN_failure_counter()
    {
        var h = new Harness();
        var cashier = h.AddCashier("1234");
        var (_, token) = await h.ActiveRegisterAsync();

        await h.Service.StartSessionAsync(token, new StartSessionRequest("0000", cashier.Id));
        await h.Service.StartSessionAsync(token, new StartSessionRequest("0000", cashier.Id));
        Assert.Equal(2, cashier.PosPinFailedAttempts);

        var (res, _, _) = await h.Service.StartSessionAsync(token, new StartSessionRequest("1234", cashier.Id));

        Assert.NotNull(res);
        Assert.Equal(0, cashier.PosPinFailedAttempts);
    }

    [Fact]
    public async Task Register_locks_after_max_failures_in_window()
    {
        var h = new Harness();
        h.AddCashier("1234");
        var (reg, token) = await h.ActiveRegisterAsync();

        for (var i = 1; i < RegisterService.MaxRegisterAttempts; i++)
        {
            var (r, _, s) = await h.Service.StartSessionAsync(token, new StartSessionRequest("0000"));
            Assert.Null(r);
            Assert.Equal(401, s);
        }

        var tenth = await h.Service.StartSessionAsync(token, new StartSessionRequest("0000"));
        Assert.Equal(423, tenth.StatusCode);
        Assert.True(reg.IsPinLockedOut(RegisterService.MaxRegisterAttempts, RegisterService.RegisterWindow, DateTime.UtcNow));

        var (res, _, status) = await h.Service.StartSessionAsync(token, new StartSessionRequest("1234"));
        Assert.Null(res);
        Assert.Equal(423, status);
    }

    [Fact]
    public async Task A_successful_signin_does_not_reset_the_register_failure_counter()
    {
        var h = new Harness();
        h.AddCashier("1234");
        var (reg, token) = await h.ActiveRegisterAsync();

        // 9 failures, then the attacker's own valid PIN works...
        for (var i = 1; i < RegisterService.MaxRegisterAttempts; i++)
            await h.Service.StartSessionAsync(token, new StartSessionRequest("0000"));
        var ok = await h.Service.StartSessionAsync(token, new StartSessionRequest("1234"));
        Assert.NotNull(ok.Result);
        Assert.Equal(RegisterService.MaxRegisterAttempts - 1, reg.PinFailCount); // not reset

        // ...so the very next miss trips the lock instead of getting 9 fresh guesses.
        var miss = await h.Service.StartSessionAsync(token, new StartSessionRequest("0000"));
        Assert.Equal(423, miss.StatusCode);
    }

    [Fact]
    public void Failure_window_decays_but_the_daily_cap_locks_the_register_until_unlocked()
    {
        var reg = new CashRegister { TenantId = TenantId, StoreId = StoreId, Number = 1, Name = "r" };
        typeof(CashRegister).GetProperty(nameof(CashRegister.Status))!.SetValue(reg, CashRegister.StatusActive);
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // 9 failures, then a miss after the window elapsed: the window restarts at 1.
        for (var i = 0; i < 9; i++) reg.RegisterPinFailure(start.AddSeconds(i), RegisterService.RegisterWindow, 50);
        var (window, _) = reg.RegisterPinFailure(start.AddMinutes(20), RegisterService.RegisterWindow, 50);
        Assert.Equal(1, window);
        Assert.False(reg.IsPinLockedOut(10, RegisterService.RegisterWindow, start.AddMinutes(20)));

        // Spread failures so no single window ever fills: the per-day cap still trips at 50.
        var t = start.AddMinutes(20);
        while (reg.Status == CashRegister.StatusActive)
        {
            t = t.AddMinutes(16);
            reg.RegisterPinFailure(t, RegisterService.RegisterWindow, 50);
            Assert.True(t.Date == start.Date, "test must stay inside one UTC day");
        }

        Assert.Equal(CashRegister.StatusLocked, reg.Status);
        Assert.Equal(50, reg.PinFailDailyCount);

        reg.Unlock();
        Assert.Equal(CashRegister.StatusPending, reg.Status); // no device token in this synthetic register
        Assert.Equal(0, reg.PinFailDailyCount);
    }

    [Fact]
    public async Task Locked_register_refuses_signin_until_a_manager_unlocks_it()
    {
        var h = new Harness();
        h.AddCashier("1234");
        var (reg, token) = await h.ActiveRegisterAsync();
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 50; i++) reg.RegisterPinFailure(start.AddMinutes(16 * i), RegisterService.RegisterWindow, 50);
        Assert.Equal(CashRegister.StatusLocked, reg.Status);

        var blocked = await h.Service.StartSessionAsync(token, new StartSessionRequest("1234"));
        Assert.Equal(423, blocked.StatusCode);

        var unlock = await h.Service.UnlockAsync(Admin, reg.Id);
        Assert.True(unlock.Ok);
        Assert.NotNull((await h.Service.StartSessionAsync(token, new StartSessionRequest("1234"))).Result);
    }

    [Theory]
    [InlineData("12")]
    [InlineData("1234567")]
    [InlineData("12a4")]
    [InlineData("")]
    public async Task Pin_must_be_4_to_6_digits(string pin)
    {
        var h = new Harness();
        var (_, token) = await h.ActiveRegisterAsync();

        var (res, _, status) = await h.Service.StartSessionAsync(token, new StartSessionRequest(pin));

        Assert.Null(res);
        Assert.Equal(400, status);
    }

    [Fact]
    public async Task Cashier_of_another_store_cannot_sign_in()
    {
        var h = new Harness();
        var other = h.AddCashier("1234", storeId: Guid.NewGuid());
        var (_, token) = await h.ActiveRegisterAsync();

        var byId = await h.Service.StartSessionAsync(token, new StartSessionRequest("1234", other.Id));
        var byPin = await h.Service.StartSessionAsync(token, new StartSessionRequest("1234"));

        Assert.Equal(401, byId.StatusCode);
        Assert.Equal(401, byPin.StatusCode);
    }

    [Theory]
    [InlineData(AppRoles.Merchandiser)]
    [InlineData(AppRoles.Staff)]
    [InlineData(AppRoles.SupplierAdmin)]
    public async Task Users_without_a_pos_role_cannot_sign_in_even_with_a_pin(string role)
    {
        var h = new Harness();
        var u = h.AddCashier("1234", role: role);
        var (_, token) = await h.ActiveRegisterAsync();

        Assert.Equal(401, (await h.Service.StartSessionAsync(token, new StartSessionRequest("1234", u.Id))).StatusCode);
        Assert.Equal(401, (await h.Service.StartSessionAsync(token, new StartSessionRequest("1234"))).StatusCode);
    }

    [Fact]
    public async Task Deactivated_user_cannot_sign_in()
    {
        var h = new Harness();
        var u = h.AddCashier("1234");
        u.Deactivate();
        var (_, token) = await h.ActiveRegisterAsync();

        Assert.Equal(401, (await h.Service.StartSessionAsync(token, new StartSessionRequest("1234", u.Id))).StatusCode);
        Assert.Equal(401, (await h.Service.StartSessionAsync(token, new StartSessionRequest("1234"))).StatusCode);
    }

    [Fact]
    public async Task Pin_only_signin_is_refused_when_the_store_has_too_many_pin_holders()
    {
        var h = new Harness();
        for (var i = 0; i <= RegisterService.MaxPinCandidates; i++)
            h.AddCashier("1234", $"u{i}@x.test");
        var (_, token) = await h.ActiveRegisterAsync();

        var pinOnly = await h.Service.StartSessionAsync(token, new StartSessionRequest("1234"));
        Assert.Equal(400, pinOnly.StatusCode);

        var byId = await h.Service.StartSessionAsync(token, new StartSessionRequest("1234", h.Repo.Users[0].Id));
        Assert.NotNull(byId.Result); // explicit cashier still bounded to one bcrypt
    }

    [Fact]
    public async Task Invalid_or_forged_device_token_is_rejected()
    {
        var h = new Harness();
        h.AddCashier("1234");
        var (reg, token) = await h.ActiveRegisterAsync();
        var forged = RegisterCredentials.FormatDeviceToken(TenantId, reg.Id, "x".PadRight(43, 'x'));

        foreach (var bad in new[] { null, "", "nonsense", forged })
        {
            var (res, _, status) = await h.Service.StartSessionAsync(bad, new StartSessionRequest("1234"));
            Assert.Null(res);
            Assert.Equal(401, status);
        }

        Assert.NotNull((await h.Service.StartSessionAsync(token, new StartSessionRequest("1234"))).Result);
    }

    [Fact]
    public async Task Revoked_register_cannot_sign_in_and_its_sessions_end_and_are_uncached()
    {
        var h = new Harness();
        h.AddCashier("1234");
        var (reg, token) = await h.ActiveRegisterAsync();
        await h.Service.StartSessionAsync(token, new StartSessionRequest("1234"));
        var sessionId = h.Repo.Sessions.Single().Id;

        var (ok, _, _) = await h.Service.RevokeAsync(Admin, reg.Id);
        var (res, _, status) = await h.Service.StartSessionAsync(token, new StartSessionRequest("1234"));

        Assert.True(ok);
        Assert.Null(res);
        Assert.Equal(401, status);
        Assert.Equal(RegisterSession.EndReasonRevoked, h.Repo.Sessions.Single().EndReason);
        h.SessionCache.Received().Invalidate(sessionId);
    }

    [Fact]
    public async Task EndSession_marks_session_ended_invalidates_cache_and_is_scoped_to_its_register()
    {
        var h = new Harness();
        h.AddCashier("1234");
        var (reg, token) = await h.ActiveRegisterAsync();
        var session = (await h.Service.StartSessionAsync(token, new StartSessionRequest("1234"))).Result!;

        var wrongRegister = await h.Service.EndSessionAsync(session.SessionId, Guid.NewGuid(), null);
        var ok = await h.Service.EndSessionAsync(session.SessionId, reg.Id, "idle");

        Assert.Equal(404, wrongRegister.StatusCode);
        Assert.True(ok.Ok);
        Assert.Equal(RegisterSession.EndReasonIdle, h.Repo.Sessions.Single().EndReason);
        h.SessionCache.Received().Invalidate(session.SessionId);
    }

    // ── PIN management ─────────────────────────────────────────────────────

    [Fact]
    public async Task SetPin_generates_a_pin_server_side_and_returns_it_once()
    {
        var h = new Harness(pins: new QueuePinGenerator("135790"));
        var b = h.AddUserWithoutPin("b@x.test");

        var res = await h.Service.SetPinAsync(StoreManager(), b.Id);

        Assert.Null(res.Error);
        Assert.Equal("135790", res.Result!.Pin);
        Assert.Equal("h:135790", b.PosPinHash);
        Assert.NotNull(b.PosPinSetAt);
    }

    [Fact]
    public async Task SetPin_retries_collisions_silently_and_never_reveals_taken_pins()
    {
        // 111111 is held by a colleague: the generator proposes it first, the service must move on.
        var h = new Harness(pins: new QueuePinGenerator("111111", "111111", "222222"));
        h.AddCashier("111111", "a@x.test");
        var b = h.AddUserWithoutPin("b@x.test");

        var res = await h.Service.SetPinAsync(StoreManager(), b.Id);

        Assert.Null(res.Error);
        Assert.Equal("222222", res.Result!.Pin);
        Assert.Equal("h:222222", b.PosPinHash);
    }

    [Fact]
    public async Task SetPin_exhausting_retries_gives_a_generic_error_without_mentioning_taken_pins()
    {
        var always = new QueuePinGenerator(Enumerable.Repeat("111111", 50).ToArray());
        var h = new Harness(pins: always);
        h.AddCashier("111111", "a@x.test");
        var b = h.AddUserWithoutPin("b@x.test");

        var res = await h.Service.SetPinAsync(StoreManager(), b.Id);

        Assert.Null(res.Result);
        Assert.Equal(503, res.StatusCode);
        Assert.DoesNotContain("already", res.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("used", res.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(b.PosPinHash);
    }

    [Fact]
    public async Task SetPin_target_must_hold_a_pos_role()
    {
        var h = new Harness();
        foreach (var role in new[] { AppRoles.Merchandiser, AppRoles.Staff, AppRoles.SupplierAdmin, AppRoles.ProviderAgent })
        {
            var u = h.AddUserWithoutPin($"{role}@x.test", role);
            Assert.Equal(400, (await h.Service.SetPinAsync(Admin, u.Id)).StatusCode);
        }
    }

    [Fact]
    public async Task SetPin_target_must_not_outrank_the_caller()
    {
        var h = new Harness();
        var networkMgr = h.AddUserWithoutPin("nm@x.test", AppRoles.NetworkManager);
        var storeMgr = h.AddUserWithoutPin("sm@x.test", AppRoles.StoreManager);
        var cashier = h.AddUserWithoutPin("c@x.test", AppRoles.Cashier);

        Assert.Equal(403, (await h.Service.SetPinAsync(StoreManager(), networkMgr.Id)).StatusCode);
        Assert.NotNull((await h.Service.SetPinAsync(StoreManager(), storeMgr.Id)).Result);   // peer allowed
        Assert.NotNull((await h.Service.SetPinAsync(StoreManager(), cashier.Id)).Result);
    }

    [Fact]
    public async Task SetPin_store_manager_is_limited_to_their_own_store()
    {
        var h = new Harness();
        var foreign = h.AddUserWithoutPin("f@x.test", storeId: OtherStoreId);

        Assert.Equal(403, (await h.Service.SetPinAsync(StoreManager(), foreign.Id)).StatusCode);
        Assert.Equal(403, (await h.Service.ClearPinAsync(StoreManager(), foreign.Id)).StatusCode);
    }

    [Fact]
    public async Task SetPin_network_manager_is_limited_to_assigned_stores_enterprise_admin_is_not()
    {
        var h = new Harness();
        var nm = new RegisterCaller(TenantId, Guid.NewGuid(), AppRoles.NetworkManager, null);
        h.UserLocations.GetLocationIdsForUserAsync(TenantId, nm.UserId, Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { StoreId });
        var inStore = h.AddUserWithoutPin("in@x.test", storeId: StoreId);
        var outStore = h.AddUserWithoutPin("out@x.test", storeId: OtherStoreId);

        Assert.NotNull((await h.Service.SetPinAsync(nm, inStore.Id)).Result);
        Assert.Equal(403, (await h.Service.SetPinAsync(nm, outStore.Id)).StatusCode);
        Assert.NotNull((await h.Service.SetPinAsync(Admin, outStore.Id)).Result);
    }

    [Fact]
    public async Task Create_assigns_incrementing_numbers_and_rejects_duplicates()
    {
        var h = new Harness();
        var (a, _, _) = await h.Service.CreateAsync(Admin, new CreateRegisterRequest(StoreId, "A"));
        var (b, _, _) = await h.Service.CreateAsync(Admin, new CreateRegisterRequest(StoreId, "B"));
        var dup = await h.Service.CreateAsync(Admin, new CreateRegisterRequest(StoreId, "C", Number: 1));

        Assert.Equal(1, a!.Register.Number);
        Assert.Equal(2, b!.Register.Number);
        Assert.Equal(409, dup.StatusCode);
    }

    // ── Register management store scoping ──────────────────────────────────

    [Fact]
    public async Task Store_manager_manages_only_registers_of_their_own_store()
    {
        var h = new Harness();
        var (mine, _, _) = await h.Service.CreateAsync(Admin, new CreateRegisterRequest(StoreId, "Mine"));
        var (theirs, _, _) = await h.Service.CreateAsync(Admin, new CreateRegisterRequest(OtherStoreId, "Theirs"));
        var sm = StoreManager();

        var list = await h.Service.ListAsync(sm, null);
        Assert.Equal([mine!.Register.Id], list.Select(r => r.Id).ToArray());
        // an explicit filter on a store the manager is not assigned to yields nothing
        Assert.Empty(await h.Service.ListAsync(sm, OtherStoreId));

        var foreignId = theirs!.Register.Id;
        Assert.Equal(404, (await h.Service.UpdateAsync(sm, foreignId, new UpdateRegisterRequest(Name: "x"))).StatusCode);
        Assert.Equal(404, (await h.Service.IssueActivationCodeAsync(sm, foreignId)).StatusCode);
        Assert.Equal(404, (await h.Service.RevokeAsync(sm, foreignId)).StatusCode);
        Assert.Equal(404, (await h.Service.UnlockAsync(sm, foreignId)).StatusCode);
        Assert.NotEqual(CashRegister.StatusRevoked, h.Repo.Registers.Single(r => r.Id == foreignId).Status);

        Assert.NotNull((await h.Service.UpdateAsync(sm, mine.Register.Id, new UpdateRegisterRequest(Name: "ok"))).Result);
    }

    [Fact]
    public async Task Store_manager_with_extra_user_locations_assignment_manages_both_stores()
    {
        var h = new Harness();
        var sm = StoreManager();
        h.UserLocations.GetLocationIdsForUserAsync(TenantId, sm.UserId, Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { OtherStoreId });

        var own = await h.Service.CreateAsync(sm, new CreateRegisterRequest(StoreId, "Own"));
        var extra = await h.Service.CreateAsync(sm, new CreateRegisterRequest(OtherStoreId, "Extra"));

        Assert.NotNull(own.Result);
        Assert.NotNull(extra.Result);
        Assert.Equal(2, (await h.Service.ListAsync(sm, null)).Count);
    }

    [Fact]
    public async Task Store_manager_cannot_create_a_register_in_or_move_one_to_another_store()
    {
        var h = new Harness();
        var sm = StoreManager();

        var create = await h.Service.CreateAsync(sm, new CreateRegisterRequest(OtherStoreId, "Elsewhere"));
        Assert.Equal(403, create.StatusCode);

        var (mine, _, _) = await h.Service.CreateAsync(sm, new CreateRegisterRequest(StoreId, "Mine"));
        var move = await h.Service.UpdateAsync(sm, mine!.Register.Id, new UpdateRegisterRequest(StoreId: OtherStoreId));
        Assert.Equal(403, move.StatusCode);
        Assert.Equal(StoreId, h.Repo.Registers.Single().StoreId);
    }

    [Fact]
    public async Task Network_manager_sees_only_registers_of_assigned_stores()
    {
        var h = new Harness();
        var nm = new RegisterCaller(TenantId, Guid.NewGuid(), AppRoles.NetworkManager, null);
        h.UserLocations.GetLocationIdsForUserAsync(TenantId, nm.UserId, Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { OtherStoreId });
        await h.Service.CreateAsync(Admin, new CreateRegisterRequest(StoreId, "A"));
        var (b, _, _) = await h.Service.CreateAsync(Admin, new CreateRegisterRequest(OtherStoreId, "B"));

        var list = await h.Service.ListAsync(nm, null);

        Assert.Equal([b!.Register.Id], list.Select(r => r.Id).ToArray());
    }
}
