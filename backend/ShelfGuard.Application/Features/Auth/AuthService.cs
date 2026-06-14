using ShelfGuard.Application.Features.Auth.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Auth;

public sealed class AuthService : IAuthService
{
    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtService _jwt;
    private readonly IActivityLogRepository _activityLogs;

    public AuthService(
        IUserRepository users,
        IRefreshTokenRepository refreshTokens,
        IPasswordHasher passwordHasher,
        IJwtService jwt,
        IActivityLogRepository activityLogs)
    {
        _users = users;
        _refreshTokens = refreshTokens;
        _passwordHasher = passwordHasher;
        _jwt = jwt;
        _activityLogs = activityLogs;
    }

    public async Task<(LoginResponse? Response, string? Error)> LoginAsync(
        string email, string password, CancellationToken ct = default)
    {
        var user = await _users.GetByEmailAsync(email.ToLowerInvariant(), ct);

        if (user is null || !user.IsActive || !_passwordHasher.Verify(password, user.PasswordHash))
            return (null, "Invalid email or password.");

        var (rawToken, tokenHash) = _jwt.GenerateRefreshToken();
        var refreshToken = RefreshToken.Create(user.Id, tokenHash, DateTime.UtcNow.AddDays(7));

        await _refreshTokens.AddAsync(refreshToken, ct);
        await _refreshTokens.SaveChangesAsync(ct);

        user.UpdateLastActive();
        _users.Update(user);
        await _users.SaveChangesAsync(ct);

        await _activityLogs.LogAsync(new ActivityLog
        {
            TenantId   = user.TenantId,
            UserId     = user.Id,
            Action     = "user.login",
            EntityType = "user",
            EntityId   = user.Id,
            Meta       = $"{user.Email} ({user.Role})",
        }, ct);
        await _activityLogs.SaveChangesAsync(ct);

        var accessToken = _jwt.GenerateAccessToken(user.Id, user.Email, user.Role, user.TenantId, user.StoreId, user.FullName);

        return (new LoginResponse(accessToken, rawToken, ToDto(user)), null);
    }

    public async Task<(LoginResponse? Response, string? Error)> RefreshAsync(
        string rawRefreshToken, CancellationToken ct = default)
    {
        var tokenHash = _jwt.HashToken(rawRefreshToken);
        var existing = await _refreshTokens.GetActiveByHashAsync(tokenHash, ct);

        if (existing is null)
            return (null, "Invalid or expired refresh token.");

        var user = await _users.GetByIdAsync(existing.UserId, ct);
        if (user is null || !user.IsActive)
            return (null, "User not found or deactivated.");

        var (newRaw, newHash) = _jwt.GenerateRefreshToken();
        var newToken = RefreshToken.Create(user.Id, newHash, DateTime.UtcNow.AddDays(7));

        existing.Revoke(newHash);
        _refreshTokens.Update(existing);
        await _refreshTokens.AddAsync(newToken, ct);
        await _refreshTokens.SaveChangesAsync(ct);

        var accessToken = _jwt.GenerateAccessToken(user.Id, user.Email, user.Role, user.TenantId, user.StoreId, user.FullName);

        return (new LoginResponse(accessToken, newRaw, ToDto(user)), null);
    }

    public async Task RevokeAsync(string rawRefreshToken, CancellationToken ct = default)
    {
        var tokenHash = _jwt.HashToken(rawRefreshToken);
        var existing = await _refreshTokens.GetActiveByHashAsync(tokenHash, ct);
        if (existing is null) return;

        existing.Revoke();
        _refreshTokens.Update(existing);
        await _refreshTokens.SaveChangesAsync(ct);
    }

    public async Task<AuthUserDto?> GetCurrentUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        return user is null ? null : ToDto(user);
    }

    private static AuthUserDto ToDto(User u) =>
        new(u.Id, u.Email, u.FullName, u.Role, u.TenantId, u.StoreId);
}
