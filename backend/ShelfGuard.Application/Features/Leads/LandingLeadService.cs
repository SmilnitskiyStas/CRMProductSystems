using Microsoft.Extensions.Logging;
using ShelfGuard.Application.Features.Leads.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Leads;

public sealed class LandingLeadService(
    ILandingLeadRepository leads,
    ILogger<LandingLeadService> logger) : ILandingLeadService
{
    /// <summary>Whitelist of form origins accepted from the client; unknown values fall back to "landing".</summary>
    public static readonly IReadOnlySet<string> AllowedSources = new HashSet<string>(StringComparer.Ordinal)
    {
        "landing", "retail", "features", "roadmap", "how-it-works", "for-whom", "join",
    };

    private static readonly IReadOnlySet<string> AllowedLocales = new HashSet<string>(StringComparer.Ordinal) { "uk", "en" };

    public async Task<string?> CaptureAsync(CaptureLeadRequest request, CancellationToken ct)
    {
        // Honeypot: the "website" field is hidden on the real form, humans never fill it.
        // Pretend success (204) so bots can't detect the trap; nothing is saved.
        if (!string.IsNullOrWhiteSpace(request.Website))
        {
            logger.LogInformation("Landing lead honeypot triggered, submission discarded.");
            return null;
        }

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length < 2 || name.Length > 100)
            return "Name is required (2-100 characters).";

        var phone = request.Phone?.Trim();
        if (string.IsNullOrEmpty(phone) || phone.Length < 5 || phone.Length > 30)
            return "Phone is required (5-30 characters).";

        var company = NormalizeOptional(request.Company);
        if (company is { Length: > 150 })
            return "Company must be at most 150 characters.";

        var message = NormalizeOptional(request.Message);
        if (message is { Length: > 1000 })
            return "Message must be at most 1000 characters.";

        var source = NormalizeOptional(request.Source)?.ToLowerInvariant();
        if (source is null || !AllowedSources.Contains(source)) source = "landing";

        var locale = NormalizeOptional(request.Locale)?.ToLowerInvariant();
        if (locale is null || !AllowedLocales.Contains(locale)) locale = null;

        var lead = LandingLead.Create(
            name, phone, company, message, source,
            pageUrl: Truncate(request.PageUrl, 300),
            locale: locale,
            referrer: Truncate(request.Referrer, 300),
            utmSource: Truncate(request.UtmSource, 100),
            utmMedium: Truncate(request.UtmMedium, 100),
            utmCampaign: Truncate(request.UtmCampaign, 150));

        try
        {
            await leads.AddAsync(lead, ct);
            await leads.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A lead must never be silently lost: log everything needed to recover it by hand,
            // then rethrow so the API answers 5xx and the client keeps/retries the payload.
            logger.LogError(ex,
                "LEAD NOT SAVED: {LeadId} source={Source} name={Name} phone={Phone} company={Company} message={Message}",
                lead.Id, source, name, phone, company ?? "-", message ?? "-");
            throw;
        }

        logger.LogInformation(
            "Landing lead saved: {LeadId}, source {Source}, company: {Company}",
            lead.Id, source, company ?? "-");

        // Notification channel: the unprocessed-leads badge in the provider admin menu
        // (GET api/provider/leads/count). No Telegram/email push for now.
        return null;
    }

    public async Task<LandingLeadListDto> ListAsync(
        string? status, string? search, int page, int pageSize, CancellationToken ct)
    {
        var normalizedStatus = status is "new" or "processed" ? status : "all";
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var term = NormalizeOptional(search);
        if (term is { Length: > 100 }) term = term[..100];

        var (items, total) = await leads.ListAsync(normalizedStatus, term, page, pageSize, ct);
        return new LandingLeadListDto(items.Select(ToDto).ToList(), total, page, pageSize);
    }

    public async Task<LeadCountDto> CountUnprocessedAsync(CancellationToken ct) =>
        new(await leads.CountUnprocessedAsync(ct));

    public async Task<(LandingLeadDto? Lead, string? Error)> UpdateAsync(
        Guid id, UpdateLeadRequest request, Guid? actingUserId, CancellationToken ct)
    {
        var lead = await leads.GetByIdAsync(id, ct);
        if (lead is null) return (null, "Lead not found.");

        if (request.AdminNote is not null)
        {
            var note = NormalizeOptional(request.AdminNote);
            if (note is { Length: > 1000 }) return (null, "Note must be at most 1000 characters.");
            lead.SetAdminNote(note);
        }

        if (request.IsProcessed is true && !lead.IsProcessed) lead.MarkProcessed(actingUserId);
        else if (request.IsProcessed is false && lead.IsProcessed) lead.MarkUnprocessed();

        await leads.SaveChangesAsync(ct);
        return (ToDto(lead), null);
    }

    private static LandingLeadDto ToDto(LandingLead l) => new(
        l.Id, l.Name, l.Phone, l.Company, l.Message, l.Source, l.PageUrl, l.Locale, l.Referrer,
        l.UtmSource, l.UtmMedium, l.UtmCampaign, l.IsProcessed, l.CreatedAt, l.ProcessedAt,
        l.ProcessedByUserId, l.AdminNote);

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string? Truncate(string? value, int max)
    {
        var v = NormalizeOptional(value);
        return v is not null && v.Length > max ? v[..max] : v;
    }
}
