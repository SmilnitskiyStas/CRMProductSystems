using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class LegalEntityRepository : ILegalEntityRepository
{
    private readonly AppDbContext _db;

    public LegalEntityRepository(AppDbContext db) => _db = db;

    public Task<List<LegalEntity>> GetAllByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        _db.LegalEntities
            .Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.LegalName)
            .ToListAsync(ct);

    public Task<LegalEntity?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.LegalEntities.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task AddAsync(LegalEntity entity, CancellationToken ct = default) =>
        await _db.LegalEntities.AddAsync(entity, ct);

    public void Update(LegalEntity entity) => _db.LegalEntities.Update(entity);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
