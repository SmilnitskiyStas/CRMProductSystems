using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class PasswordResetTokenRepository : IPasswordResetTokenRepository
{
    private readonly AppDbContext _db;

    public PasswordResetTokenRepository(AppDbContext db) => _db = db;

    public Task InvalidateActiveTokensAsync(Guid userId, CancellationToken ct = default) =>
        _db.PasswordResetTokens
            .Where(t => t.UserId == userId && t.UsedAt == null && t.ExpiresAt > DateTime.UtcNow)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, DateTime.UtcNow), ct);

    // TASK-460: deliberately ignores ExpiresAt — a just-requested, not-yet-expired-or-used token
    // is what proves "a request happened within the window", independent of how long it remains
    // usable afterward.
    public Task<bool> HasRecentActiveTokenAsync(Guid userId, TimeSpan window, CancellationToken ct = default) =>
        _db.PasswordResetTokens
            .AnyAsync(t => t.UserId == userId && t.UsedAt == null && t.CreatedAt > DateTime.UtcNow - window, ct);

    public async Task AddAsync(PasswordResetToken token, CancellationToken ct = default) =>
        await _db.PasswordResetTokens.AddAsync(token, ct);

    public Task<PasswordResetToken?> GetActiveByHashAsync(string tokenHash, CancellationToken ct = default) =>
        _db.PasswordResetTokens
            .FirstOrDefaultAsync(t =>
                t.TokenHash == tokenHash &&
                t.UsedAt == null &&
                t.ExpiresAt > DateTime.UtcNow, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
