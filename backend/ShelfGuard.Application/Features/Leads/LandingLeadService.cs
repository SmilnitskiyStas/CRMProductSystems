using Microsoft.Extensions.Logging;
using ShelfGuard.Application.Features.Leads.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Leads;

public sealed class LandingLeadService(
    ILandingLeadRepository leads,
    ILogger<LandingLeadService> logger) : ILandingLeadService
{
    public async Task<string?> CaptureAsync(CaptureLeadRequest request, CancellationToken ct)
    {
        // Honeypot: the "website" field is hidden on the real form — humans never fill it.
        // Pretend success (204) so bots can't detect the trap; nothing is saved.
        if (!string.IsNullOrWhiteSpace(request.Website))
        {
            logger.LogInformation("Landing lead honeypot triggered — submission discarded.");
            return null;
        }

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length < 2 || name.Length > 100)
            return "Name is required (2–100 characters).";

        var phone = request.Phone?.Trim();
        if (string.IsNullOrEmpty(phone) || phone.Length < 5 || phone.Length > 30)
            return "Phone is required (5–30 characters).";

        var company = NormalizeOptional(request.Company);
        if (company is { Length: > 150 })
            return "Company must be at most 150 characters.";

        var message = NormalizeOptional(request.Message);
        if (message is { Length: > 1000 })
            return "Message must be at most 1000 characters.";

        var lead = LandingLead.Create(name, phone, company, message);

        await leads.AddAsync(lead, ct);
        await leads.SaveChangesAsync(ct);

        logger.LogInformation(
            "Landing lead saved: {LeadId} — {Name}, {Phone}, company: {Company}",
            lead.Id, name, phone, company ?? "—");

        // TODO(TASK-333): notify provider via Telegram. The worker's notification pipeline
        // is tenant-scoped (resolves recipients by TenantId + role), so a provider-level
        // message needs a recipient convention/schema change. The lead row is the source
        // of truth; wire up Telegram when a provider-level notification channel exists.

        return null;
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
