using ShelfGuard.Application.Features.Auth.Dtos;

namespace ShelfGuard.Application.Features.Auth;

public interface IPasswordRecoveryService
{
    Task RequestAsync(string email, string? ipAddress = null, CancellationToken ct = default);
    Task<string?> ConfirmAsync(ResetPasswordRequest request, string? ipAddress = null, CancellationToken ct = default);
}

public sealed record PasswordRecoveryOptions(string WebBaseUrl);
