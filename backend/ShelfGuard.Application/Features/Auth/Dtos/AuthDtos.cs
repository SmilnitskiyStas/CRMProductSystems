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
    string? TenantName,
    Guid? StoreId,
    Dictionary<string, bool>? Permissions,
    /// <summary>Optional legal entity this user is registered under (TASK-322).</summary>
    Guid? LegalEntityId = null
);
