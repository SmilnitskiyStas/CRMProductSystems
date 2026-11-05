namespace ShelfGuard.Application.Features.BeautyControl;

/// <summary>Config section "BeautyControl" (env BeautyControl__*). Secret only from env, never stored in DB.</summary>
public sealed class BeautyControlOptions
{
    public const string Section = "BeautyControl";
    public bool Enabled { get; set; }
    public string? BaseUrl { get; set; }
    public string? KeyId { get; set; }
    /// <summary>Base64 secret (>= 32 raw bytes). Env: BeautyControl__Secret.</summary>
    public string? Secret { get; set; }
    public int TimeoutSeconds { get; set; } = 10;
}

/// <summary>Config section "BeautyFeedbackInbound": keys of the Beauty -> ShelfGuard direction.</summary>
public sealed class BeautyFeedbackInboundOptions
{
    public const string Section = "BeautyFeedbackInbound";
    public Dictionary<string, BeautyInboundKey> Keys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class BeautyInboundKey
{
    /// <summary>Base64 secret (>= 32 raw bytes).</summary>
    public string? Secret { get; set; }
    /// <summary>active | retiring. Anything else (or missing) disables the key.</summary>
    public string? Status { get; set; }
}

public enum BeautyErrorKind
{
    None, Disabled, NotFound, Conflict, Validation, RateLimited, AuthFailed, Unavailable, Unknown,
}

/// <summary>Typed outcome of a Beauty call. Raw Beauty bodies are never exposed; only a safe error code.</summary>
public sealed record BeautyResult<T>(
    T? Data, BeautyErrorKind Error = BeautyErrorKind.None, string? Code = null, int HttpStatus = 200)
{
    public bool Ok => Error == BeautyErrorKind.None;
    /// <summary>True when Beauty answered an idempotent create with 200 (tenant already existed).</summary>
    public bool Replayed { get; init; }

    public static BeautyResult<T> Fail(BeautyErrorKind kind, string code, int httpStatus = 0) =>
        new(default, kind, code, httpStatus);
}

public sealed record BeautyActor(string Id, string DisplayName);

public sealed record BeautyCreateTenantCommand(
    string ExternalRef, string Name, string Slug, IReadOnlyList<string>? Modules,
    string OwnerEmail, string OwnerFullName, BeautyActor Actor);

public sealed record BeautyOwnerInfo(Guid? UserId, string? Email, string? State, DateTimeOffset? InviteExpiresAt = null);

public sealed record BeautyInviteInfo(string? Url, DateTimeOffset? ExpiresAt);

public sealed record BeautyCreatedTenant(
    Guid? TenantId, string? ExternalRef, string? Slug, string? Status, IReadOnlyList<string>? Modules,
    BeautyOwnerInfo? Owner, BeautyInviteInfo? Invite);

public sealed record BeautyTenantInfo(
    Guid? TenantId, string? ExternalRef, string? Slug, string? Name, string? Status,
    IReadOnlyList<string>? Modules, DateTimeOffset? SuspendedAt, BeautyOwnerInfo? Owner, DateTimeOffset? CreatedAt);

public sealed record BeautyStatusInfo(string? Status, DateTimeOffset? SuspendedAt = null);

public sealed record BeautyModulesInfo(IReadOnlyList<string>? Modules);

/// <summary>Outbound HMAC client for Beauty CRM <c>/api/control/v1/*</c>. No retries; a fresh nonce per request.</summary>
public interface IBeautyControlClient
{
    Task<BeautyResult<BeautyCreatedTenant>> CreateTenantAsync(BeautyCreateTenantCommand command, CancellationToken ct);
    Task<BeautyResult<BeautyTenantInfo>> GetTenantAsync(string externalRef, CancellationToken ct);
    Task<BeautyResult<BeautyStatusInfo>> SuspendAsync(string externalRef, string reason, BeautyActor actor, CancellationToken ct);
    Task<BeautyResult<BeautyStatusInfo>> ResumeAsync(string externalRef, BeautyActor actor, CancellationToken ct);
    Task<BeautyResult<BeautyModulesInfo>> SetModulesAsync(string externalRef, IReadOnlyList<string> modules, BeautyActor actor, CancellationToken ct);
    Task<BeautyResult<BeautyInviteInfo>> IssueOwnerInviteAsync(string externalRef, BeautyActor actor, CancellationToken ct);
}
