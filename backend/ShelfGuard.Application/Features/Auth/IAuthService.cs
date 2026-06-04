using ShelfGuard.Application.Features.Auth.Dtos;

namespace ShelfGuard.Application.Features.Auth;

public interface IAuthService
{
    Task<(LoginResponse? Response, string? Error)> LoginAsync(string email, string password, CancellationToken ct = default);
    Task<(LoginResponse? Response, string? Error)> RefreshAsync(string rawRefreshToken, CancellationToken ct = default);
    Task RevokeAsync(string rawRefreshToken, CancellationToken ct = default);
    Task<AuthUserDto?> GetCurrentUserAsync(Guid userId, CancellationToken ct = default);
}
