using ShelfGuard.Application.Features.Leads.Dtos;

namespace ShelfGuard.Application.Features.Leads;

public interface ILandingLeadService
{
    /// <summary>Returns null on success (or honeypot hit), otherwise a validation error message.</summary>
    Task<string?> CaptureAsync(CaptureLeadRequest request, CancellationToken ct);
}
