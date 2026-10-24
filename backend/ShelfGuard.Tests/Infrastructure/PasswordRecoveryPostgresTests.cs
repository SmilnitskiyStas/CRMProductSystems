using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShelfGuard.Application.Features.Auth;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Infrastructure.Migrations;
using Xunit;

namespace ShelfGuard.Tests.Infrastructure;

public sealed class RecoveryPostgresFactAttribute : FactAttribute
{
    public RecoveryPostgresFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SHELFGUARD_RECOVERY_TEST_DB")))
            Skip = "Requires the disposable recovery-only PostgreSQL fixture; never uses a default/local application DB.";
    }
}

[Collection("Session database")]
public sealed class PasswordRecoveryPostgresTests : IAsyncLifetime
{
    private readonly string _connection = Environment.GetEnvironmentVariable("SHELFGUARD_RECOVERY_TEST_DB") ?? "";
    private AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connection).Options);

    public async Task InitializeAsync()
    {
        await using var db = Context();
        await db.Database.EnsureCreatedAsync();
    }
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<User> Seed(bool grant = false, bool expired = false)
    {
        var user = User.Create(null, Guid.NewGuid().ToString("N") + "@synthetic.example", "Synthetic recovery fixture", "old-hash", "provider");
        user.SetPendingTotpSecret("SYNTHETIC_TOTP_SECRET");
        user.EnableTotp([]);
        if (grant) user.SetPasswordResetGrant("grant-hash", DateTime.UtcNow.AddMinutes(-2),
            expired ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddMinutes(15));
        await using var db = Context();
        db.Users.Add(user);
        db.RefreshTokens.Add(RefreshToken.Create(user.Id, Guid.NewGuid().ToString("N"), DateTime.UtcNow.AddDays(1)));
        await db.SaveChangesAsync();
        return user;
    }

    [RecoveryPostgresFact]
    public async Task Concurrent_requests_reserve_one_grant_and_one_outbox_intent_without_changing_password_or_sessions()
    {
        var user = await Seed();
        var now = DateTime.UtcNow;
        async Task<bool> Issue(string marker)
        {
            await using var db = Context();
            return await new PasswordRecoveryStore(db).IssueAsync(user, marker, now, now.AddMinutes(15),
                new NotificationQueue { UserId = user.Id, EventType = PasswordRecoveryService.EventType,
                    Channel = "system", Status = "pending", Payload = "{}" });
        }
        var outcomes = await Task.WhenAll(Issue("synthetic-hash-A"), Issue("synthetic-hash-B"));
        Assert.Single(outcomes, value => value);
        await using var verify = Context();
        Assert.Equal("old-hash", (await verify.Users.SingleAsync(u => u.Id == user.Id)).PasswordHash);
        Assert.Equal(1, await verify.NotificationQueues.CountAsync(n => n.UserId == user.Id));
        Assert.Null((await verify.RefreshTokens.SingleAsync(t => t.UserId == user.Id)).RevokedAt);
    }

    [RecoveryPostgresFact]
    public async Task Concurrent_confirmation_has_one_winner_and_atomically_revokes_sessions_and_preserves_MFA()
    {
        var user = await Seed(grant: true);
        async Task<bool> Confirm(string newHash)
        {
            await using var db = Context();
            return await new PasswordRecoveryStore(db).ConfirmAsync(user.Id, "grant-hash", "old-hash", newHash, DateTime.UtcNow, null);
        }
        var outcomes = await Task.WhenAll(Confirm("new-hash-A"), Confirm("new-hash-B"));
        Assert.Single(outcomes, value => value);
        Assert.False(await Confirm("replay-hash"));
        await using var verify = Context();
        var account = await verify.Users.SingleAsync(u => u.Id == user.Id);
        Assert.StartsWith("new-hash-", account.PasswordHash);
        Assert.Null(account.PasswordResetTokenHash);
        Assert.Null(account.PasswordResetExpiresAt);
        Assert.True(account.TotpEnabled);
        Assert.NotNull((await verify.RefreshTokens.SingleAsync(t => t.UserId == user.Id)).RevokedAt);
        Assert.Equal(1, await verify.ActivityLogs.CountAsync(l => l.UserId == user.Id && l.Action == "user.password_reset_confirmed"));
    }

    [RecoveryPostgresFact]
    public async Task Expired_proof_and_failed_final_audit_write_leave_password_proof_and_refresh_consistent()
    {
        var expired = await Seed(grant: true, expired: true);
        await using (var db = Context())
            Assert.False(await new PasswordRecoveryStore(db).ConfirmAsync(expired.Id, "grant-hash", "old-hash", "new-hash", DateTime.UtcNow, null));
        var user = await Seed(grant: true);
        await using (var db = Context())
        {
            // A bounded synthetic DB constraint failure occurs after the password/refresh UPDATEs.
            await Assert.ThrowsAsync<DbUpdateException>(() => new PasswordRecoveryStore(db).ConfirmAsync(
                user.Id, "grant-hash", "old-hash", "new-hash", DateTime.UtcNow, new string('x', 51)));
        }
        await using var verify = Context();
        var account = await verify.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Equal("old-hash", account.PasswordHash);
        Assert.Equal("grant-hash", account.PasswordResetTokenHash);
        Assert.Null((await verify.RefreshTokens.SingleAsync(t => t.UserId == user.Id)).RevokedAt);
        Assert.Equal("old-hash", (await verify.Users.SingleAsync(u => u.Id == expired.Id)).PasswordHash);
    }

    [RecoveryPostgresFact]
    public async Task Recovery_migration_down_and_up_preserve_accounts_and_restore_nullable_grant_columns()
    {
        var user = await Seed();
        await using var db = Context();
        var migration = new AddProofBasedPasswordRecovery();
        var generator = db.GetService<IMigrationsSqlGenerator>();
        foreach (var command in generator.Generate(migration.DownOperations, db.Model))
            await db.Database.ExecuteSqlRawAsync(command.CommandText);
        foreach (var command in generator.Generate(migration.UpOperations, db.Model))
            await db.Database.ExecuteSqlRawAsync(command.CommandText);
        var account = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.Equal("old-hash", account.PasswordHash);
        Assert.Null(account.PasswordResetTokenHash);
        Assert.Null(account.PasswordResetRequestedAt);
    }
}
