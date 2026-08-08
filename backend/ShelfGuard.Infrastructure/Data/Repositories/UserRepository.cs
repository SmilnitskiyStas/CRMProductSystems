using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly AppDbContext _db;

    public UserRepository(AppDbContext db) => _db = db;

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.Users.Include(u => u.Tenant).FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        _db.Users.Include(u => u.Tenant).FirstOrDefaultAsync(u => u.Email == email, ct);

    public Task<User?> GetByPhoneAsync(string phone, CancellationToken ct = default) =>
        _db.Users.Include(u => u.Tenant).FirstOrDefaultAsync(u => u.Phone == phone, ct);

    public async Task<IReadOnlyList<User>> GetAllByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        await _db.Users
            .Where(u => u.TenantId == tenantId)
            .OrderBy(u => u.FullName)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<User>> GetByRolesAsync(IEnumerable<string> roles, CancellationToken ct = default)
    {
        var roleList = roles.ToList();
        return await _db.Users
            .Where(u => roleList.Contains(u.Role))
            .OrderBy(u => u.FullName)
            .ToListAsync(ct);
    }

    public async Task AddAsync(User user, CancellationToken ct = default) =>
        await _db.Users.AddAsync(user, ct);

    public void Update(User user) => _db.Users.Update(user);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
