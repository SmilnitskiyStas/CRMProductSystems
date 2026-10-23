using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using ShelfGuard.Api.Infrastructure;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Infrastructure.Interceptors;
using ShelfGuard.Infrastructure.Migrations;
using ShelfGuard.Infrastructure.Services;
using ShelfGuard.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace ShelfGuard.Tests.Infrastructure;

[CollectionDefinition("Session database", DisableParallelization = true)]
public sealed class SessionDatabaseCollection { }

[Collection("Session database")]
public sealed class SessionPostgresTests : IAsyncLifetime
{
    private readonly string _connection = Environment.GetEnvironmentVariable("SHELFGUARD_RECOVERY_TEST_DB") ?? "";
    private const string Key = "SYNTHETIC-SESSION-TEST-KEY-000000000000000000000000";
    private static JwtService Jwt() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Jwt:Secret"] = Key, ["Jwt:Issuer"] = "session-test", ["Jwt:Audience"] = "session-test" }).Build());
    private AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(TestPostgres.DataSource(_connection)).Options);
    public async Task InitializeAsync() { await using var db = Context(); await db.Database.EnsureCreatedAsync(); }
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(User User, RefreshToken Token)> Staff()
    {
        var user = User.Create(null, Guid.NewGuid().ToString("N") + "@synthetic.example", "Synthetic", "old-hash", "provider");
        user.SetPendingTotpSecret("SYNTHETIC"); user.EnableTotp(["synthetic-hash"]);
        var token = RefreshToken.Create(user.Id, Guid.NewGuid().ToString("N"), DateTime.UtcNow.AddDays(7), authVersion: user.AuthVersion, mfaVerifiedAt: DateTime.UtcNow);
        await using var db = Context(); db.Add(user); db.Add(token); await db.SaveChangesAsync(); return (user, token);
    }
    private async Task<(ConsumerAccount Account, ConsumerRefreshToken Token)> Consumer()
    {
        var account = new ConsumerAccount { Phone = "+380" + Random.Shared.NextInt64(100000000, 999999999), PasswordHash = "old-hash", FullName = "Synthetic" };
        var token = new ConsumerRefreshToken { ConsumerAccountId = account.Id, TokenHash = Guid.NewGuid().ToString("N"), ExpiresAt = DateTime.UtcNow.AddDays(30) };
        await using var db = Context(); db.Add(account); await db.SaveChangesAsync();
        Assert.True(await new ConsumerSessionStore(db).IssueAsync(account, token)); return (account, token);
    }

    private async Task<WebApplication> Server()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<TenantConnectionInterceptor>();
        builder.Services.AddDbContext<AppDbContext>((sp, options) => options.UseNpgsql(TestPostgres.DataSource(_connection)).AddInterceptors(sp.GetRequiredService<TenantConnectionInterceptor>()));
        builder.Services.AddSingleton<IAccessSessionValidator, AccessSessionValidator>();
        builder.Services.AddScoped<ITenantSessionOverride, TenantSessionOverride>();
        builder.Services.AddScoped<IMfaPolicy, LiveMfaPolicy>();
        builder.Services.AddAuthentication("Bearer").AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters { ValidateIssuer = true, ValidIssuer = "session-test",
                ValidateAudience = true, ValidAudience = "session-test", ValidateLifetime = true, ClockSkew = TimeSpan.Zero,
                ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)) };
            options.Events = new JwtBearerEvents { OnTokenValidated = context => AccessSessionValidation.ValidateAsync(context, "session-test:register") };
        });
        builder.Services.AddScoped<IWorkAccessContext, WorkAccessContext>();
        builder.Services.AddScoped<IAuthorizationHandler, WorkPermissionHandler>();
        builder.Services.AddAuthorization(AppPolicies.Configure);
        var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization();
        app.MapGet("/protected", () => "synthetic").RequireAuthorization();
        app.MapGet("/consumer", () => "synthetic").RequireAuthorization(policy => policy.RequireRole("consumer"));
        app.MapGet("/staff", () => "synthetic").RequireAuthorization(policy => policy.RequireRole("provider"));
        app.MapGet("/production-read", () => "synthetic").RequireAuthorization(AppPolicies.ProductionView);
        app.MapPost("/production-complete", () => "synthetic").RequireAuthorization(AppPolicies.ProductionComplete);
        await app.StartAsync(); return app;
    }
    private static HttpClient Client(WebApplication app, string token)
    {
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var client = new HttpClient { BaseAddress = new Uri(address) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token); return client;
    }

    [RecoveryPostgresFact]
    public async Task Enrollment_proof_and_unverified_staff_bearers_cannot_open_the_workspace()
    {
        var (user, proof) = await Staff(); await using var app = await Server();
        using var enrollment = Client(app, Jwt().GenerateMfaEnrollmentToken(user.Id, user.AuthVersion));
        using var unverified = Client(app, Jwt().GenerateAccessToken(user.Id, user.Email, user.Role, null, null,
            sessionId: proof.SessionId, authVersion: user.AuthVersion));
        Assert.Equal(HttpStatusCode.Unauthorized, (await enrollment.GetAsync("/protected")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await unverified.GetAsync("/staff")).StatusCode);
        using var verified = Client(app, Jwt().GenerateAccessToken(user.Id, user.Email, user.Role, null, null,
            sessionId: proof.SessionId, authVersion: user.AuthVersion, mfaVerifiedAt: proof.MfaVerifiedAt));
        Assert.Equal(HttpStatusCode.OK, (await verified.GetAsync("/staff")).StatusCode);
        await using var db = Context();
        await db.RefreshTokens.Where(t => t.Id == proof.Id).ExecuteUpdateAsync(u => u.SetProperty(t => t.MfaVerifiedAt, (DateTime?)null));
        Assert.Equal(HttpStatusCode.Unauthorized, (await verified.GetAsync("/staff")).StatusCode);
    }

    [RecoveryPostgresFact]
    public async Task Live_permission_policy_rejects_stale_capabilities_and_keeps_view_separate_from_complete()
    {
        var tenant = Tenant.Create("Synthetic", Guid.NewGuid().ToString("N"));
        var user = User.Create(tenant.Id, Guid.NewGuid().ToString("N") + "@synthetic.example", "Synthetic", "hash", "staff");
        user.SetPendingTotpSecret("SYNTHETIC"); user.EnableTotp([]);
        var role = TenantRole.Create(tenant.Id, "Read only", ["production.view"], null); user.SetTenantRole(role.Id);
        var proof = RefreshToken.Create(user.Id, Guid.NewGuid().ToString("N"), DateTime.UtcNow.AddDays(1), authVersion: user.AuthVersion, mfaVerifiedAt: DateTime.UtcNow);
        await using (var db = Context()) { db.AddRange(tenant, user, role, proof); await db.SaveChangesAsync(); }
        await using var app = await Server();
        using var client = Client(app, Jwt().GenerateAccessToken(user.Id, user.Email, user.Role, tenant.Id, null,
            capabilities: ["production.view", "production.complete"], sessionId: proof.SessionId, authVersion: user.AuthVersion, mfaVerifiedAt: proof.MfaVerifiedAt));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/production-read")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/production-complete", null)).StatusCode);
        await using (var db = Context()) { var liveRole = await db.TenantRoles.SingleAsync(r => r.Id == role.Id); liveRole.Deactivate(); await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/production-read")).StatusCode);
    }

    [RecoveryPostgresFact]
    public async Task Adding_privileged_permission_to_unverified_session_requires_new_MFA_login()
    {
        var tenant = Tenant.Create("Synthetic", Guid.NewGuid().ToString("N"));
        var user = User.Create(tenant.Id, Guid.NewGuid().ToString("N") + "@synthetic.example", "Synthetic", "hash", "cashier");
        var proof = RefreshToken.Create(user.Id, Guid.NewGuid().ToString("N"), DateTime.UtcNow.AddDays(1));
        await using (var db = Context()) { db.AddRange(tenant, user, proof); await db.SaveChangesAsync(); }
        await using var app = await Server();
        using var client = Client(app, Jwt().GenerateAccessToken(user.Id, user.Email, user.Role, tenant.Id, null, sessionId: proof.SessionId));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/protected")).StatusCode);
        await using (var db = Context()) { var current = await db.Users.SingleAsync(u => u.Id == user.Id); current.SetPermissions(new() { ["production.manage"] = true }); await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/protected")).StatusCode);
    }

    [RecoveryPostgresFact]
    public async Task Concurrent_step_up_consumes_one_code_and_changes_the_verified_time_of_only_one_session()
    {
        var (user, proof) = await Staff();
        await using var one = Context(); await using var two = Context();
        var first = await one.Users.SingleAsync(u => u.Id == user.Id);
        var second = await two.Users.SingleAsync(u => u.Id == user.Id);
        first.MarkTotpTimestepUsed(42); second.MarkTotpTimestepUsed(42);
        async Task<bool> Confirm(AppDbContext db, User current) {
            try { return await new RefreshTokenRepository(db).ConfirmMfaAsync(current, proof.SessionId, DateTime.UtcNow); }
            catch (DbUpdateConcurrencyException) { return false; }
        }
        Assert.Single(await Task.WhenAll(Confirm(one, first), Confirm(two, second)), result => result);
        await using var verify = Context();
        Assert.Equal(42, (await verify.Users.SingleAsync(u => u.Id == user.Id)).TotpLastTimestep);
        Assert.Single(await verify.ActivityLogs.Where(l => l.UserId == user.Id && l.Action == "user.mfa_step_up").ToListAsync());
    }

    [RecoveryPostgresFact]
    public async Task Login_and_MFA_updates_do_not_restore_permissions_revoked_after_the_user_was_loaded()
    {
        var (user, proof) = await Staff(); await using var stale = Context();
        var repo = new UserRepository(stale); var loaded = (await repo.GetByIdAsync(user.Id))!;
        await using (var edit = Context()) {
            var current = await edit.Users.SingleAsync(u => u.Id == user.Id);
            current.SetPermissions(new() { ["production.complete"] = false }); await edit.SaveChangesAsync();
        }
        loaded.UpdateLastActive(); repo.Update(loaded); await repo.SaveChangesAsync();
        loaded.MarkTotpTimestepUsed(42);
        Assert.True(await new RefreshTokenRepository(stale).ConfirmMfaAsync(loaded, proof.SessionId, DateTime.UtcNow));
        await using var verify = Context();
        Assert.False((await verify.Users.SingleAsync(u => u.Id == user.Id)).Permissions!["production.complete"]);
    }

    [RecoveryPostgresFact]
    public async Task Copied_personal_JWT_stops_on_next_HTTP_request_after_logout_while_another_device_stays_valid()
    {
        var (account, proof) = await Consumer();
        var other = new ConsumerRefreshToken { ConsumerAccountId = account.Id, TokenHash = Guid.NewGuid().ToString("N"), ExpiresAt = proof.ExpiresAt };
        await using (var db = Context()) Assert.True(await new ConsumerSessionStore(db).IssueAsync(account, other));
        await using var app = await Server();
        using var client = Client(app, Jwt().GenerateConsumerAccessToken(account.Id, sessionId: proof.SessionId));
        using var second = Client(app, Jwt().GenerateConsumerAccessToken(account.Id, sessionId: other.SessionId));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/consumer")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/staff")).StatusCode);
        await using (var db = Context()) await new ConsumerSessionStore(db).RevokeAsync(account.Id, proof.SessionId);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/protected")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/consumer")).StatusCode);
        await using (var db = Context()) await new ConsumerSessionStore(db).RevokeAsync(account.Id, null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/consumer")).StatusCode);
    }

    [RecoveryPostgresFact]
    public async Task Staff_logout_password_change_and_deactivation_reject_copied_JWT_and_stale_writes()
    {
        var (user, proof) = await Staff(); await using var app = await Server();
        using var client = Client(app, Jwt().GenerateAccessToken(user.Id, user.Email, user.Role, null, null, sessionId: proof.SessionId, authVersion: user.AuthVersion, mfaVerifiedAt: proof.MfaVerifiedAt));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/staff")).StatusCode);
        await using (var db = Context()) await new RefreshTokenRepository(db).RevokeSessionAsync(user.Id, proof.SessionId);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/protected")).StatusCode);
        var (nextUser, nextProof) = await Staff();
        using var nextClient = Client(app, Jwt().GenerateAccessToken(nextUser.Id, nextUser.Email, nextUser.Role, null, null, sessionId: nextProof.SessionId, authVersion: nextUser.AuthVersion, mfaVerifiedAt: nextProof.MfaVerifiedAt));
        await using var stale = Context(); var staleUser = await stale.Users.SingleAsync(u => u.Id == nextUser.Id);
        await using (var db = Context()) { var current = await db.Users.SingleAsync(u => u.Id == nextUser.Id); current.ChangePassword("new-hash"); await db.SaveChangesAsync(); }
        staleUser.UpdateLastActive(); stale.Users.Update(staleUser);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await nextClient.GetAsync("/protected")).StatusCode);
        var (disabled, disabledProof) = await Staff();
        using var disabledClient = Client(app, Jwt().GenerateAccessToken(disabled.Id, disabled.Email, disabled.Role, null, null, sessionId: disabledProof.SessionId, authVersion: disabled.AuthVersion, mfaVerifiedAt: disabledProof.MfaVerifiedAt));
        await using (var db = Context()) { (await db.Users.SingleAsync(u => u.Id == disabled.Id)).Deactivate(); await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Unauthorized, (await disabledClient.GetAsync("/protected")).StatusCode);
    }

    [RecoveryPostgresFact]
    public async Task Concurrent_rotations_have_one_winner_for_each_identity_and_preserve_absolute_expiry()
    {
        var (account, proof) = await Consumer();
        async Task<bool> RotateConsumer() { await using var db = Context(); return await new ConsumerSessionStore(db).RotateAsync(proof,
            new ConsumerRefreshToken { ConsumerAccountId = account.Id, SessionId = proof.SessionId, TokenHash = Guid.NewGuid().ToString("N"), ExpiresAt = proof.ExpiresAt }); }
        Assert.Single(await Task.WhenAll(RotateConsumer(), RotateConsumer()), value => value);
        var (user, staffProof) = await Staff();
        async Task<bool> RotateStaff() { await using var db = Context(); return await new RefreshTokenRepository(db).TryRotateAsync(staffProof,
            RefreshToken.Create(user.Id, Guid.NewGuid().ToString("N"), staffProof.ExpiresAt, staffProof.SessionId, user.AuthVersion, staffProof.MfaVerifiedAt)); }
        Assert.Single(await Task.WhenAll(RotateStaff(), RotateStaff()), value => value);
        await using var verify = Context();
        Assert.Single(await verify.ConsumerRefreshTokens.Where(t => t.SessionId == proof.SessionId && t.RevokedAt == null).ToListAsync());
        Assert.Single(await verify.RefreshTokens.Where(t => t.SessionId == staffProof.SessionId && t.RevokedAt == null).ToListAsync());
        await new ConsumerSessionStore(verify).RevokeAsync(account.Id, proof.SessionId);
        Assert.False(await RotateConsumer());
    }

    [RecoveryPostgresFact]
    public async Task Personal_password_change_and_reactivation_do_not_restore_old_sessions()
    {
        var (account, proof) = await Consumer(); await using var app = await Server();
        using var client = Client(app, Jwt().GenerateConsumerAccessToken(account.Id, sessionId: proof.SessionId));
        await using (var db = Context()) { var current = await db.ConsumerAccounts.SingleAsync(a => a.Id == account.Id); current.PasswordHash = "new-hash"; await db.SaveChangesAsync(); Assert.Equal(1, current.AuthVersion); }
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/consumer")).StatusCode);
        var (other, otherProof) = await Consumer();
        using var otherClient = Client(app, Jwt().GenerateConsumerAccessToken(other.Id, sessionId: otherProof.SessionId));
        await using (var db = Context()) { var current = await db.ConsumerAccounts.SingleAsync(a => a.Id == other.Id); current.IsActive = false; await db.SaveChangesAsync(); current.IsActive = true; await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherClient.GetAsync("/consumer")).StatusCode);
    }

    [RecoveryPostgresFact]
    public async Task Missing_or_foreign_session_claims_cannot_authenticate_and_provider_revoke_also_blocks_impersonation()
    {
        var (user, proof) = await Staff(); var (other, otherProof) = await Staff(); await using var app = await Server();
        using var legacy = Client(app, Jwt().GenerateAccessToken(user.Id, user.Email, user.Role, null, null));
        using var foreign = Client(app, Jwt().GenerateAccessToken(user.Id, user.Email, user.Role, null, null, sessionId: otherProof.SessionId));
        Assert.Equal(HttpStatusCode.Unauthorized, (await legacy.GetAsync("/protected")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await foreign.GetAsync("/protected")).StatusCode);
        var tenant = Tenant.Create("Synthetic", Guid.NewGuid().ToString("N"));
        await using (var seed = Context()) { seed.Tenants.Add(tenant); await seed.SaveChangesAsync(); }
        using var impersonation = Client(app, Jwt().GenerateImpersonationToken(user.Id, user.Email, tenant.Id, proof.SessionId, user.AuthVersion, proof.MfaVerifiedAt));
        Assert.Equal(HttpStatusCode.OK, (await impersonation.GetAsync("/protected")).StatusCode);
        await using var db = Context(); await new RefreshTokenRepository(db).RevokeAllSessionsAsync(user.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, (await impersonation.GetAsync("/protected")).StatusCode);
        Assert.False(await new RefreshTokenRepository(db).TryRotateAsync(proof, RefreshToken.Create(user.Id, "after-revoke", proof.ExpiresAt, proof.SessionId, user.AuthVersion, proof.MfaVerifiedAt)));
    }

    [RecoveryPostgresFact]
    public async Task Migration_backfills_legacy_staff_refresh_session_without_changing_password()
    {
        var (user, proof) = await Staff();
        await using var db = Context(); var migration = new AddRevocableIdentitySessions(); var generator = db.GetService<IMigrationsSqlGenerator>();
        foreach (var command in generator.Generate(migration.DownOperations, db.Model)) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        // A synthetic pre-migration refresh row survives with an independent session id.
        foreach (var command in generator.Generate(migration.UpOperations, db.Model)) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        Assert.False(await db.RefreshTokens.AnyAsync(t => t.SessionId == Guid.Empty));
        Assert.True(await db.Users.AllAsync(u => u.AuthVersion == 0));
        Assert.Equal(proof.Id, (await db.RefreshTokens.AsNoTracking().SingleAsync(t => t.Id == proof.Id)).SessionId);
        Assert.Equal("old-hash", (await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id)).PasswordHash);
    }

    [RecoveryPostgresFact]
    public async Task Recovery_code_can_only_be_committed_once_across_concurrent_contexts()
    {
        var (user, _) = await Staff();
        await using (var db = Context()) { var current = await db.Users.SingleAsync(u => u.Id == user.Id); current.SetPendingTotpSecret("SYNTHETIC"); current.EnableTotp(["synthetic-code-hash"]); await db.SaveChangesAsync(); }
        await using var first = Context(); await using var second = Context();
        var one = await first.Users.SingleAsync(u => u.Id == user.Id); var two = await second.Users.SingleAsync(u => u.Id == user.Id);
        Assert.True(one.TryConsumeRecoveryCode("synthetic-code-hash")); Assert.True(two.TryConsumeRecoveryCode("synthetic-code-hash"));
        first.Users.Update(one); second.Users.Update(two); await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }
}
