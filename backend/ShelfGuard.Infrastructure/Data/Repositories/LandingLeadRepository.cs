using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class LandingLeadRepository(AppDbContext db) : ILandingLeadRepository
{
    public async Task AddAsync(LandingLead lead, CancellationToken ct) =>
        await db.LandingLeads.AddAsync(lead, ct);

    public Task SaveChangesAsync(CancellationToken ct) =>
        db.SaveChangesAsync(ct);
}
