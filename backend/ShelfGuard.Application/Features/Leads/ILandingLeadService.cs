using ShelfGuard.Application.Features.Leads.Dtos;

namespace ShelfGuard.Application.Features.Leads;

public interface ILandingLeadService
{
    /// <summary>
    /// Returns null on success (or honeypot hit), otherwise a validation error message.
    /// Persistence failures are NOT swallowed: they propagate so the caller returns 5xx.
    /// </summary>
    Task<string?> CaptureAsync(CaptureLeadRequest request, CancellationToken ct);

    Task<LandingLeadListDto> ListAsync(string? status, string? search, int page, int pageSize, CancellationToken ct);

    Task<LeadCountDto> CountUnprocessedAsync(CancellationToken ct);

    /// <summary>Returns (dto, null) on success or (null, error): "not found" / validation.</summary>
    Task<(LandingLeadDto? Lead, string? Error)> UpdateAsync(
        Guid id, UpdateLeadRequest request, Guid? actingUserId, CancellationToken ct);
}
