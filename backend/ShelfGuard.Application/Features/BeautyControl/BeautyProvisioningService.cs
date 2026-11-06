using System.Net.Mail;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.BeautyControl;

public sealed record CreateBeautyTenantRequest(
    Guid? ShelfGuardTenantId, string? Name, string? Slug, string? OwnerEmail, string? OwnerName,
    IReadOnlyList<string>? Modules, string? LocationTimezone);

public sealed record SuspendBeautyTenantRequest(string? Reason);

public sealed record SetBeautyModulesRequest(IReadOnlyList<string>? Modules);

/// <summary>
/// State of a Beauty business as seen from ShelfGuard (flat). <c>Exists=false</c> means no link yet
/// (all other fields null). Never contains invite URLs or tokens.
/// </summary>
public sealed record BeautyTenantDto(
    bool Exists, Guid ShelfGuardTenantId, Guid? BeautyTenantId, string? Slug, string? Name, string? Status,
    IReadOnlyList<string>? Modules, string? OwnerState, string? OwnerEmail, DateTimeOffset? InviteExpiresAt,
    DateTimeOffset? SuspendedAt, DateTimeOffset? CreatedAt, DateTime? LastSyncedAt);

/// <summary>Create response. <c>InviteUrl</c> is returned once (null on idempotent replay) and is never stored.</summary>
public sealed record BeautyTenantCreatedDto(
    Guid ShelfGuardTenantId, Guid? BeautyTenantId, string Slug, string Status, IReadOnlyList<string> Modules,
    string? OwnerState, string? OwnerEmail, string? InviteUrl, DateTimeOffset? InviteExpiresAt, bool Replayed);

public sealed record BeautyInviteDto(string? InviteUrl, DateTimeOffset? ExpiresAt);

public sealed record BeautyStatusDto(string Status, DateTimeOffset? SuspendedAt);

public sealed record BeautyModulesDto(IReadOnlyList<string> Modules);

public interface IBeautyProvisioningService
{
    Task<BeautyResult<BeautyTenantCreatedDto>> CreateAsync(CreateBeautyTenantRequest request, BeautyActor actor, Guid? actorUserId, CancellationToken ct);
    Task<BeautyResult<BeautyTenantDto>> GetAsync(Guid shelfGuardTenantId, CancellationToken ct);
    Task<BeautyResult<BeautyStatusDto>> SuspendAsync(Guid shelfGuardTenantId, SuspendBeautyTenantRequest request, BeautyActor actor, Guid? actorUserId, CancellationToken ct);
    Task<BeautyResult<BeautyStatusDto>> ResumeAsync(Guid shelfGuardTenantId, BeautyActor actor, Guid? actorUserId, CancellationToken ct);
    Task<BeautyResult<BeautyInviteDto>> IssueOwnerInviteAsync(Guid shelfGuardTenantId, BeautyActor actor, Guid? actorUserId, CancellationToken ct);
    Task<BeautyResult<BeautyModulesDto>> SetModulesAsync(Guid shelfGuardTenantId, SetBeautyModulesRequest request, BeautyActor actor, Guid? actorUserId, CancellationToken ct);
}

public sealed partial class BeautyProvisioningService(
    IBeautyControlClient beauty,
    IBeautyTenantLinkRepository links,
    ITenantRepository tenants,
    IActivityLogRepository logs,
    TimeProvider clock,
    ILogger<BeautyProvisioningService> logger) : IBeautyProvisioningService
{
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,62}$")]
    private static partial Regex SlugRegex();

    public async Task<BeautyResult<BeautyTenantCreatedDto>> CreateAsync(
        CreateBeautyTenantRequest r, BeautyActor actor, Guid? actorUserId, CancellationToken ct)
    {
        if (r.ShelfGuardTenantId is not { } tenantId || tenantId == Guid.Empty)
            return Invalid<BeautyTenantCreatedDto>("invalid_shelfguard_tenant_id");

        var name = r.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 150) return Invalid<BeautyTenantCreatedDto>("invalid_name");

        var slug = r.Slug?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(slug) || !SlugRegex().IsMatch(slug)) return Invalid<BeautyTenantCreatedDto>("invalid_slug");

        var email = r.OwnerEmail?.Trim();
        if (!IsEmail(email)) return Invalid<BeautyTenantCreatedDto>("invalid_email");

        var ownerName = r.OwnerName?.Trim();
        if (string.IsNullOrEmpty(ownerName)) ownerName = email![..email.IndexOf('@')];
        if (ownerName.Length > 120) ownerName = ownerName[..120];

        List<string>? modules = null;
        if (r.Modules is not null)
        {
            modules = r.Modules.Select(m => m?.Trim() ?? string.Empty).Where(m => m.Length > 0).Distinct().ToList();
            if (modules.Count == 0 || modules.Any(m => m.Length > 64)) return Invalid<BeautyTenantCreatedDto>("unknown_module");
        }

        var sgTenant = await tenants.GetByIdAsync(tenantId, ct);
        if (sgTenant is null)
            return BeautyResult<BeautyTenantCreatedDto>.Fail(BeautyErrorKind.NotFound, "shelfguard_tenant_not_found");
        // Only an active ShelfGuard tenant of business type beauty may be provisioned in Beauty.
        if (!sgTenant.IsActive || !sgTenant.IsBeauty)
            return Invalid<BeautyTenantCreatedDto>("invalid_shelfguard_tenant");

        var result = await beauty.CreateTenantAsync(
            new BeautyCreateTenantCommand(ExternalRef(tenantId), name, slug, modules, email!, ownerName, actor), ct);
        if (!result.Ok) return BeautyResult<BeautyTenantCreatedDto>.Fail(result.Error, result.Code ?? "beauty_error", result.HttpStatus);

        var data = result.Data!;
        var now = clock.GetUtcNow().UtcDateTime;
        var status = data.Status ?? "active";
        var link = await Upsert(tenantId, data.TenantId, data.Slug ?? slug, status, now, ct);

        await Audit(tenantId, actorUserId, result.Replayed ? "beauty.tenant.create_replayed" : "beauty.tenant.created",
            $"slug={link.BeautySlug}", ct);

        return new BeautyResult<BeautyTenantCreatedDto>(
            new BeautyTenantCreatedDto(
                tenantId, link.BeautyTenantId, link.BeautySlug, link.BeautyStatus, data.Modules ?? [],
                data.Owner?.State, data.Owner?.Email ?? email, data.Invite?.Url, data.Invite?.ExpiresAt, result.Replayed),
            HttpStatus: result.Replayed ? 200 : 201) { Replayed = result.Replayed };
    }

    public async Task<BeautyResult<BeautyTenantDto>> GetAsync(Guid tenantId, CancellationToken ct)
    {
        var link = await links.GetByTenantIdAsync(tenantId, ct);
        if (link is null) return new BeautyResult<BeautyTenantDto>(NotLinked(tenantId));

        var result = await beauty.GetTenantAsync(ExternalRef(tenantId), ct);
        if (result.Error == BeautyErrorKind.NotFound) return new BeautyResult<BeautyTenantDto>(NotLinked(tenantId));
        if (!result.Ok) return BeautyResult<BeautyTenantDto>.Fail(result.Error, result.Code ?? "beauty_error", result.HttpStatus);

        var d = result.Data!;
        link.Sync(d.TenantId, d.Slug, d.Status ?? link.BeautyStatus, clock.GetUtcNow().UtcDateTime);
        await links.SaveChangesAsync(ct);

        return new BeautyResult<BeautyTenantDto>(new BeautyTenantDto(
            true, tenantId, link.BeautyTenantId, link.BeautySlug, d.Name, link.BeautyStatus, d.Modules ?? [],
            d.Owner?.State, d.Owner?.Email, d.Owner?.InviteExpiresAt, d.SuspendedAt, d.CreatedAt, link.LastSyncedAt));
    }

    public async Task<BeautyResult<BeautyStatusDto>> SuspendAsync(
        Guid tenantId, SuspendBeautyTenantRequest request, BeautyActor actor, Guid? actorUserId, CancellationToken ct)
    {
        // Reason is optional for the ShelfGuard admin UI; Beauty requires one, so a neutral default is sent.
        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason)) reason = "suspended by provider operator";
        if (reason.Length > 500) return Invalid<BeautyStatusDto>("reason_too_long");

        var link = await links.GetByTenantIdAsync(tenantId, ct);
        if (link is null) return BeautyResult<BeautyStatusDto>.Fail(BeautyErrorKind.NotFound, "beauty_link_not_found");

        var result = await beauty.SuspendAsync(ExternalRef(tenantId), reason, actor, ct);
        if (!result.Ok) return BeautyResult<BeautyStatusDto>.Fail(result.Error, result.Code ?? "beauty_error", result.HttpStatus);

        link.Sync(null, null, "suspended", clock.GetUtcNow().UtcDateTime);
        await links.SaveChangesAsync(ct);
        await Audit(tenantId, actorUserId, "beauty.tenant.suspended", $"slug={link.BeautySlug}; reason={reason}", ct);
        return new BeautyResult<BeautyStatusDto>(new BeautyStatusDto("suspended", result.Data?.SuspendedAt));
    }

    public async Task<BeautyResult<BeautyStatusDto>> ResumeAsync(Guid tenantId, BeautyActor actor, Guid? actorUserId, CancellationToken ct)
    {
        var link = await links.GetByTenantIdAsync(tenantId, ct);
        if (link is null) return BeautyResult<BeautyStatusDto>.Fail(BeautyErrorKind.NotFound, "beauty_link_not_found");

        var result = await beauty.ResumeAsync(ExternalRef(tenantId), actor, ct);
        if (!result.Ok) return BeautyResult<BeautyStatusDto>.Fail(result.Error, result.Code ?? "beauty_error", result.HttpStatus);

        link.Sync(null, null, "active", clock.GetUtcNow().UtcDateTime);
        await links.SaveChangesAsync(ct);
        await Audit(tenantId, actorUserId, "beauty.tenant.resumed", $"slug={link.BeautySlug}", ct);
        return new BeautyResult<BeautyStatusDto>(new BeautyStatusDto("active", null));
    }

    public async Task<BeautyResult<BeautyInviteDto>> IssueOwnerInviteAsync(Guid tenantId, BeautyActor actor, Guid? actorUserId, CancellationToken ct)
    {
        var link = await links.GetByTenantIdAsync(tenantId, ct);
        if (link is null) return BeautyResult<BeautyInviteDto>.Fail(BeautyErrorKind.NotFound, "beauty_link_not_found");

        var result = await beauty.IssueOwnerInviteAsync(ExternalRef(tenantId), actor, ct);
        if (!result.Ok) return BeautyResult<BeautyInviteDto>.Fail(result.Error, result.Code ?? "beauty_error", result.HttpStatus);

        // The URL (it embeds the one-time token) goes to the caller only: not stored, not logged, not audited.
        await Audit(tenantId, actorUserId, "beauty.owner_invite.issued", $"slug={link.BeautySlug}", ct);
        return new BeautyResult<BeautyInviteDto>(new BeautyInviteDto(result.Data?.Url, result.Data?.ExpiresAt), HttpStatus: 201);
    }

    public async Task<BeautyResult<BeautyModulesDto>> SetModulesAsync(
        Guid tenantId, SetBeautyModulesRequest request, BeautyActor actor, Guid? actorUserId, CancellationToken ct)
    {
        var modules = (request.Modules ?? []).Select(m => m?.Trim() ?? string.Empty).Where(m => m.Length > 0).Distinct().ToList();
        if (modules.Count == 0) return Invalid<BeautyModulesDto>("modules_required");
        if (modules.Any(m => m.Length > 64)) return Invalid<BeautyModulesDto>("unknown_module");

        var link = await links.GetByTenantIdAsync(tenantId, ct);
        if (link is null) return BeautyResult<BeautyModulesDto>.Fail(BeautyErrorKind.NotFound, "beauty_link_not_found");

        var result = await beauty.SetModulesAsync(ExternalRef(tenantId), modules, actor, ct);
        if (!result.Ok) return BeautyResult<BeautyModulesDto>.Fail(result.Error, result.Code ?? "beauty_error", result.HttpStatus);

        var applied = result.Data?.Modules ?? modules;
        link.Sync(null, null, link.BeautyStatus, clock.GetUtcNow().UtcDateTime);
        await links.SaveChangesAsync(ct);
        await Audit(tenantId, actorUserId, "beauty.tenant.modules_changed", $"slug={link.BeautySlug}; modules={string.Join(',', applied)}", ct);
        return new BeautyResult<BeautyModulesDto>(new BeautyModulesDto(applied));
    }

    private async Task<BeautyTenantLink> Upsert(Guid tenantId, Guid? beautyTenantId, string slug, string status, DateTime now, CancellationToken ct)
    {
        var link = await links.GetByTenantIdAsync(tenantId, ct);
        if (link is null)
        {
            link = BeautyTenantLink.Create(tenantId, beautyTenantId, slug, status, now);
            // A parallel create may win the unique index: re-read the winner's row and sync it instead of a 500.
            if (await links.TryAddAsync(link, ct)) return link;
            link = await links.GetByTenantIdAsync(tenantId, ct)
                   ?? throw new InvalidOperationException("Beauty link insert failed and no link exists.");
        }
        link.Sync(beautyTenantId, slug, status, now);
        await links.SaveChangesAsync(ct);
        return link;
    }

    /// <summary>Audit entry: who (provider user), what, which tenant. Never invite URLs, tokens or secrets.</summary>
    private async Task Audit(Guid tenantId, Guid? userId, string action, string meta, CancellationToken ct)
    {
        try
        {
            await logs.LogAsync(new ActivityLog
            {
                TenantId = tenantId,
                UserId = userId,
                Action = action,
                EntityType = "beauty_tenant",
                EntityId = tenantId,
                Meta = meta.Length > 1000 ? meta[..1000] : meta,
            }, ct);
            await logs.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The remote mutation already happened; do not turn it into a 5xx for the admin.
            logger.LogError("Beauty audit write failed for {Action}: {ErrorType}", action, ex.GetType().Name);
        }
    }

    private static BeautyTenantDto NotLinked(Guid tenantId) =>
        new(false, tenantId, null, null, null, null, null, null, null, null, null, null, null);

    public static string ExternalRef(Guid tenantId) => tenantId.ToString("D");

    private static BeautyResult<T> Invalid<T>(string code) => BeautyResult<T>.Fail(BeautyErrorKind.Validation, code);

    private static bool IsEmail(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 254) return false;
        return MailAddress.TryCreate(value, out var a) && a.Address == value && value.Contains('@');
    }
}
