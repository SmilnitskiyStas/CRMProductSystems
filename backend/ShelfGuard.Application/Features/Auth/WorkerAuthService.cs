using System.Security.Claims;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Auth;

public sealed record WorkerAuthOptions(string Email);
public sealed record WorkerGrant(string AccessToken, Guid TenantId, DateTime ExpiresAt);
public interface IWorkerTokenIssuer { WorkerGrant GenerateWorkerToken(User user); }
public interface IWorkerSessionValidator { Task<bool> IsValidAsync(ClaimsPrincipal principal, CancellationToken ct = default); }

// This is a separate machine credential grant, never a staff session or MFA exemption.
public sealed class WorkerAuthService(IUserRepository users, IPasswordHasher hasher, IWorkerTokenIssuer tokens,
    IActivityLogRepository audit, ITenantSessionOverride tenantScope, WorkerAuthOptions options)
{
    public static bool AllowedRole(string role) => role is "enterprise_admin" or "network_manager" or "store_manager";
    public async Task<WorkerGrant?> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.Email) || !string.Equals(email.Trim(), options.Email.Trim(), StringComparison.OrdinalIgnoreCase)) return null;
        var user = await users.GetByEmailAsync(options.Email.Trim().ToLowerInvariant(), ct);
        if (user is not { IsActive: true, TenantId: not null } || !AllowedRole(user.Role) || user.IsLockedOut
            || user.TempPasswordExpiresAt != null) return null;
        if (!hasher.Verify(password, user.PasswordHash))
        {
            user.RegisterFailedLogin(5, TimeSpan.FromMinutes(15)); users.Update(user);
            await tenantScope.ExecuteAsync(user.TenantId.Value, async () => {
                await audit.LogAsync(new ActivityLog { TenantId = user.TenantId, UserId = user.Id, Action = "worker.login_failed", EntityType = "user", EntityId = user.Id }, ct);
                await users.SaveChangesAsync(ct); return true;
            }, ct);
            return null;
        }
        // Do not reset the human MFA failure counter or mark MFA as verified.
        await tenantScope.ExecuteAsync(user.TenantId.Value, async () => {
            await audit.LogAsync(new ActivityLog { TenantId = user.TenantId, UserId = user.Id, Action = "worker.token_issued", EntityType = "user", EntityId = user.Id }, ct);
            await audit.SaveChangesAsync(ct); return true;
        }, ct);
        return tokens.GenerateWorkerToken(user);
    }
}
