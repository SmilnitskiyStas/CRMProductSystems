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
/// An employee is first a loyalty-program consumer (personal identity) who may additionally
/// hold workspace access when linked to an active staff <c>User</c> (TASK-496). The two
/// identities are independent sessions the mobile client can hold at once, so both tokens are
/// surfaced here rather than collapsing to a single <c>AccessToken</c>:
/// - PersonalAccessToken  → consumer JWT for loyalty/personal APIs; null only when the caller
///                          has no personal identity at all (legacy staff-only fallback).
/// - WorkspaceAccessToken → staff JWT for workspace APIs; null when the caller has no linked,
///                          active staff account (<see cref="MobileAccessDto.CanAccessWorkspace"/>
///                          is false in that case).
/// </summary>
public sealed record MobileLoginResponse(
    string? PersonalAccessToken,
    string? WorkspaceAccessToken,
    MobileUserDto User,
    MobileAccessDto Access);

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

    /// <summary>
    /// Consumer linked to an active staff <c>User</c>: both identities are already authenticated
    /// in the same request, so both JWTs are returned together — the personal one is reused
    /// as-is (already minted by ConsumerAuthService), not reissued.
    /// </summary>
    public static MobileLoginResponse ForLinkedStaff(
        string personalAccessToken, string workspaceAccessToken, AuthUserDto user) =>
        ForStaff(workspaceAccessToken, user) with { PersonalAccessToken = personalAccessToken };

    public static MobileLoginResponse ForConsumer(
        string personalAccessToken, Guid id, string fullName, string phone) => new(
        personalAccessToken,
        null,
        new MobileUserDto(id, fullName, null, phone, null, null),
        new MobileAccessDto(
            false,
            "consumer",
            new Dictionary<string, bool>(),
            Array.Empty<string>(),
            Array.Empty<string>()));
}
