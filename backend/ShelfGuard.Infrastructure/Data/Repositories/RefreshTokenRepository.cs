using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly AppDbContext _db;

    public RefreshTokenRepository(AppDbContext db) => _db = db;

    public Task<RefreshToken?> GetActiveByHashAsync(string tokenHash, CancellationToken ct = default) =>
        _db.RefreshTokens
            .FirstOrDefaultAsync(t =>
                t.TokenHash == tokenHash &&
                t.RevokedAt == null &&
                t.ExpiresAt > DateTime.UtcNow, ct);

    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default) =>
        _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task AddAsync(RefreshToken token, CancellationToken ct = default) =>
        await _db.RefreshTokens.AddAsync(token, ct);

    public void Update(RefreshToken token) => _db.RefreshTokens.Update(token);

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var active = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > DateTime.UtcNow)
            .ToListAsync(ct);

        foreach (var token in active)
            token.Revoke();
    }

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
