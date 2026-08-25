using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class ConsumerAccountRepository : IConsumerAccountRepository
{
    private readonly AppDbContext _db;

    public ConsumerAccountRepository(AppDbContext db) => _db = db;

    public Task<ConsumerAccount?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.ConsumerAccounts.FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<ConsumerAccount?> GetByPhoneAsync(string phone, CancellationToken ct = default) =>
        _db.ConsumerAccounts.FirstOrDefaultAsync(a => a.Phone == phone, ct);

    public Task<ConsumerAccount?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        _db.ConsumerAccounts.FirstOrDefaultAsync(a => a.Email != null && a.Email.ToLower() == email.ToLower(), ct);

    public Task AddAsync(ConsumerAccount account, CancellationToken ct = default) =>
        _db.ConsumerAccounts.AddAsync(account, ct).AsTask();

    public void Update(ConsumerAccount account) => _db.ConsumerAccounts.Update(account);

    // TASK-614: ConsumerAccountProfileChange — see IConsumerAccountRepository's combined-repository doc.

    public Task AddProfileChangeAsync(ConsumerAccountProfileChange change, CancellationToken ct = default) =>
        _db.ConsumerAccountProfileChanges.AddAsync(change, ct).AsTask();

    public async Task<(List<ConsumerAccountProfileChange> Items, int Total)> GetProfileChangesPagedAsync(
        Guid consumerAccountId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.ConsumerAccountProfileChanges
            .Where(c => c.ConsumerAccountId == consumerAccountId)
            .OrderByDescending(c => c.ChangedAt);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
