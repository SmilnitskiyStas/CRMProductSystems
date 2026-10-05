namespace ShelfGuard.Application.Features.Leads.Dtos;

public sealed record LandingLeadDto(
    Guid Id,
    string Name,
    string Phone,
    string? Company,
    string? Message,
    string Source,
    string? PageUrl,
    string? Locale,
    string? Referrer,
    string? UtmSource,
    string? UtmMedium,
    string? UtmCampaign,
    bool IsProcessed,
    DateTime CreatedAt,
    DateTime? ProcessedAt,
    Guid? ProcessedByUserId,
    string? AdminNote);

public sealed record LandingLeadListDto(
    List<LandingLeadDto> Items,
    int Total,
    int Page,
    int PageSize);

public sealed record LeadCountDto(int Unprocessed);

/// <summary>PATCH body: null fields are left unchanged. AdminNote "" clears the note.</summary>
public sealed record UpdateLeadRequest(bool? IsProcessed, string? AdminNote);
