namespace ShelfGuard.Application.Features.Auth.Dtos;

public record LoginRequest(string Email, string Password);

public record LoginResponse(
    string AccessToken,
    string RefreshToken,
    AuthUserDto User
);

public record AuthUserDto(
    Guid Id,
    string Email,
    string FullName,
    string Role,
    Guid? TenantId,
    Guid? StoreId,
    Dictionary<string, bool>? Permissions
);
