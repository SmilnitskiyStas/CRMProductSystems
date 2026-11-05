using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ShelfGuard.Application.Features.Leads;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.BeautyControl;

/// <summary>
/// Request from Beauty CRM (HMAC-authenticated). Flat fields are the primary contract; the nested
/// <c>tenant</c>/<c>user</c> objects of Beauty contracts §27.9 are accepted as a fallback so the Beauty worker
/// can send either shape. Unknown properties are ignored.
/// </summary>
public sealed record BeautyFeedbackRequest(
    string? SourceTenantRef, string? TenantName, string? Type, string? Message, string? Contact,
    string? UserRole, string? FeedbackId, string? Severity = null, string? PageUrl = null,
    BeautyFeedbackTenant? Tenant = null, BeautyFeedbackUser? User = null);

public sealed record BeautyFeedbackTenant(string? ExternalRef, string? TenantId, string? Slug, string? Name);

public sealed record BeautyFeedbackUser(string? Role, string? Email);

public enum BeautyFeedbackStatus { Created, Replayed, Invalid }

public sealed record BeautyFeedbackResult(BeautyFeedbackStatus Status, Guid? LeadId = null, string? ErrorCode = null);

public interface IBeautyFeedbackService
{
    Task<BeautyFeedbackResult> IngestAsync(BeautyFeedbackRequest request, CancellationToken ct);
}

public sealed partial class BeautyFeedbackService(
    ILandingLeadRepository leads,
    IBeautyTenantLinkRepository links,
    ILeadNotifier notifier,
    ILogger<BeautyFeedbackService> logger) : IBeautyFeedbackService
{
    public const string Source = "beauty";
    private static readonly HashSet<string> Types = new(StringComparer.Ordinal) { "cooperation", "improvement", "bug" };
    private static readonly HashSet<string> Roles = new(StringComparer.Ordinal) { "owner", "admin", "specialist" };

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex IdRegex();

    public async Task<BeautyFeedbackResult> IngestAsync(BeautyFeedbackRequest r, CancellationToken ct)
    {
        var feedbackId = r.FeedbackId?.Trim();
        if (feedbackId is null || !IdRegex().IsMatch(feedbackId)) return Invalid("invalid_feedback_id");

        var type = r.Type?.Trim().ToLowerInvariant();
        if (type is null || !Types.Contains(type)) return Invalid("invalid_type");

        var message = r.Message?.Trim();
        if (string.IsNullOrEmpty(message) || message.Length > 2000) return Invalid("invalid_message");

        var tenantRef = (r.SourceTenantRef ?? r.Tenant?.ExternalRef ?? r.Tenant?.TenantId)?.Trim();
        if (tenantRef is null || !IdRegex().IsMatch(tenantRef)) return Invalid("invalid_tenant_ref");

        var tenantName = Truncate(r.TenantName ?? r.Tenant?.Name, 100);
        if (tenantName is null) return Invalid("invalid_tenant_name");

        var role = (r.UserRole ?? r.User?.Role)?.Trim().ToLowerInvariant();
        if (role is not null && role.Length > 0 && !Roles.Contains(role)) return Invalid("invalid_user_role");
        if (string.IsNullOrEmpty(role)) role = null;

        // Idempotency: a repeated feedbackId (worker retry) is answered with the original lead.
        var existing = await leads.GetByExternalIdAsync(feedbackId, ct);
        if (existing is not null) return new(BeautyFeedbackStatus.Replayed, existing.Id);

        var contact = Truncate(r.Contact ?? r.User?.Email, 200);
        var slug = Truncate(r.Tenant?.Slug, 63);
        if (Guid.TryParse(tenantRef, out var shelfGuardTenantId))
        {
            try { slug ??= (await links.GetByTenantIdAsync(shelfGuardTenantId, ct))?.BeautySlug; }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Beauty link lookup failed ({ErrorType}); continuing without slug.", ex.GetType().Name);
            }
        }

        var criticalReason = LeadCriticalityEvaluator.Evaluate(tenantName, message, isRepeat: false, requestType: type);
        var lead = LandingLead.Create(
            name: tenantName,
            phone: contact is { Length: <= 30 } ? contact : "-",
            company: tenantName,
            message: message,
            source: Source,
            pageUrl: Truncate(r.PageUrl, 300),
            isCritical: criticalReason is not null,
            criticalReason: criticalReason,
            requestType: type,
            beautyTenantRef: tenantRef,
            reporterRole: role,
            contact: contact,
            externalId: feedbackId,
            beautyTenantSlug: slug);

        try
        {
            await leads.AddAsync(lead, ct);
            await leads.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Concurrent duplicate delivery: the unique index on ExternalId made us lose the race.
            var winner = await leads.GetByExternalIdAsync(feedbackId, ct);
            if (winner is not null) return new(BeautyFeedbackStatus.Replayed, winner.Id);
            // No PII (message/contact) in logs: only ids and the exception type.
            logger.LogError("BEAUTY FEEDBACK NOT SAVED: feedbackId={FeedbackId} type={Type} error={ErrorType}",
                feedbackId, type, ex.GetType().Name);
            throw;
        }

        logger.LogInformation("Beauty feedback saved: lead {LeadId}, type {Type}, critical {Critical}",
            lead.Id, type, lead.IsCritical);

        var notification = new LeadNotification(
            Source, tenantName, contact ?? "-", tenantName, message, lead.IsCritical, lead.CriticalReason, type);
        _ = Task.Run(async () =>
        {
            try { await notifier.NotifyAsync(notification, CancellationToken.None); }
            catch (Exception ex) { logger.LogWarning("Lead notifier failed: {ErrorType}", ex.GetType().Name); }
        });

        return new(BeautyFeedbackStatus.Created, lead.Id);
    }

    private static BeautyFeedbackResult Invalid(string code) => new(BeautyFeedbackStatus.Invalid, null, code);

    private static string? Truncate(string? value, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        return v.Length > max ? v[..max] : v;
    }
}
