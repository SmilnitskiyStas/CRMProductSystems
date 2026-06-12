using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class TelegramLinkRepository : ITelegramLinkRepository
{
    private readonly AppDbContext _db;

    public TelegramLinkRepository(AppDbContext db) => _db = db;

    public Task InvalidateActiveCodesAsync(Guid userId, CancellationToken ct = default) =>
        _db.TelegramLinkCodes
            .Where(c => c.UserId == userId && c.UsedAt == null && c.ExpiresAt > DateTime.UtcNow)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedAt, DateTime.UtcNow), ct);

    public async Task AddAsync(TelegramLinkCode code, CancellationToken ct = default) =>
        await _db.TelegramLinkCodes.AddAsync(code, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
