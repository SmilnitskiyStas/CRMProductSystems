using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <inheritdoc />
public sealed class ConsumerAiRequestRepository : IConsumerAiRequestRepository
{
    private readonly AppDbContext _db;

    public ConsumerAiRequestRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(ConsumerAiRequest entry, CancellationToken ct = default)
    {
        _db.ConsumerAiRequests.Add(entry);
        await _db.SaveChangesAsync(ct);
    }
}
