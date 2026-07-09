using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface ILegalEntityRepository
{
    Task<List<LegalEntity>> GetAllByTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task<LegalEntity?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task AddAsync(LegalEntity entity, CancellationToken ct = default);
    void Update(LegalEntity entity);
    Task SaveChangesAsync(CancellationToken ct = default);
}
