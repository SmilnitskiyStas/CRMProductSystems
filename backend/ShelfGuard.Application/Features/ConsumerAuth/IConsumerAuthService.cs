using ShelfGuard.Application.Features.ConsumerAuth.Dtos;

namespace ShelfGuard.Application.Features.ConsumerAuth;

/// <summary>
/// Registration/login for the global ConsumerAccount identity (Loyalty Фаза 0, TASK-405) —
/// a wholly separate auth flow from staff IAuthService/User. See ConsumerAuthService for the
/// full rationale (generic errors, lockout, no refresh-token flow).
/// </summary>
public interface IConsumerAuthService
{
    Task<(ConsumerAuthResponse? Response, string? Error)> RegisterAsync(
        ConsumerRegisterRequest request, CancellationToken ct = default);

    Task<(ConsumerAuthResponse? Response, string? Error)> LoginAsync(
        ConsumerLoginRequest request, CancellationToken ct = default);
}
