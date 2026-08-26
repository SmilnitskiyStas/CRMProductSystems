using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class CategoryRepository : ICategoryRepository
{
    private readonly AppDbContext _db;

    public CategoryRepository(AppDbContext db) => _db = db;

    // TASK-632: no explicit TenantId filter here, by design — mirrors ItemRepository exactly.
    // `categories` already has RLS enabled (tenant_isolation + provider_bypass policies, added in
    // the initial FullSchema migration) and TenantConnectionInterceptor sets the
    // `app.tenant_id` session var per request, so isolation happens at the DB level the same way
    // it does for Items/Locations — adding a redundant `.Where(c => c.TenantId == tenantId)` here
    // would just be a second, easy-to-drift copy of what RLS already guarantees.
    public Task<List<Category>> GetAllActiveAsync(CancellationToken ct = default) =>
        _db.Categories
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);
}
