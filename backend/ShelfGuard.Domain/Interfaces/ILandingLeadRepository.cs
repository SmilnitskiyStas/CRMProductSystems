using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface ILandingLeadRepository
{
    Task AddAsync(LandingLead lead, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
