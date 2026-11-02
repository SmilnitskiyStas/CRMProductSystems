using ShelfGuard.Application.Features.Auth.Dtos;

namespace ShelfGuard.Application.Features.MobileAuth.Dtos;

public sealed record MobileLoginRequest(string Identifier, string Password);

public sealed record MobileAccessDto(
    bool CanAccessWorkspace,
    string Role,
    Dictionary<string, bool> Permissions,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> Tabs);

public sealed record MobileUserDto(
    Guid Id,
    string FullName,
    string? Email,
    string? Phone,
    Guid? TenantId,
    Guid? StoreId);

/// <summary>
/// Personal and workspace tokens represent independent identities. Consumer login and
/// registration return only a personal token; staff login returns only a workspace token.
/// Both fields remain in the response for compatibility with existing mobile clients.
/// Contact equality must never authorize a workspace token (SEC-001).
/// </summary>
public sealed record MobileLoginResponse(
    string? PersonalAccessToken,
    string? WorkspaceAccessToken,
    MobileUserDto User,
    MobileAccessDto Access,
    string? PersonalRefreshToken = null);

public static class MobileLoginResponseFactory
{
    /// <summary>Legacy staff-only fallback (no <c>ConsumerAccount</c> exists) — no personal token to expose.</summary>
    public static MobileLoginResponse ForStaff(string workspaceAccessToken, AuthUserDto user) => new(
        null,
        workspaceAccessToken,
        new MobileUserDto(user.Id, user.FullName, user.Email, null, user.TenantId, user.StoreId),
        new MobileAccessDto(
            true,
            user.Role,
            user.Permissions ?? new Dictionary<string, bool>(),
            user.Capabilities ?? Array.Empty<string>(),
            user.Tabs ?? Array.Empty<string>()));

    public static MobileLoginResponse ForConsumer(
        string personalAccessToken, Guid id, string fullName, string phone, string? personalRefreshToken = null) => new(
        personalAccessToken,
        null,
        new MobileUserDto(id, fullName, null, phone, null, null),
        new MobileAccessDto(
            false,
            "consumer",
            new Dictionary<string, bool>(),
            Array.Empty<string>(),
            Array.Empty<string>()), personalRefreshToken);
}
