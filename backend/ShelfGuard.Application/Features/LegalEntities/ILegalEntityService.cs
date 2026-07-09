using ShelfGuard.Application.Features.LegalEntities.Dtos;

namespace ShelfGuard.Application.Features.LegalEntities;

public interface ILegalEntityService
{
    Task<List<LegalEntityDto>> GetAllAsync(Guid tenantId, CancellationToken ct = default);

    Task<(LegalEntityDto? LegalEntity, string? Error)> GetByIdAsync(
        Guid tenantId, Guid id, CancellationToken ct = default);

    Task<(LegalEntityDto? LegalEntity, string? Error)> CreateAsync(
        Guid tenantId, CreateLegalEntityRequest request, CancellationToken ct = default);

    Task<(LegalEntityDto? LegalEntity, string? Error)> UpdateAsync(
        Guid tenantId, Guid id, UpdateLegalEntityRequest request, CancellationToken ct = default);

    Task<string?> DeactivateAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>Checks whether a LegalEntity id belongs to the given tenant (used for cross-feature FK validation).</summary>
    Task<bool> BelongsToTenantAsync(Guid tenantId, Guid legalEntityId, CancellationToken ct = default);
}
