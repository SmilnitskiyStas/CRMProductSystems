using ShelfGuard.Application.Features.Adu.Dtos;

namespace ShelfGuard.Application.Features.Adu;

public interface IAduService
{
    Task<(AduDto? Adu, string? Error)> GetAsync(Guid storeId, Guid productId, CancellationToken ct = default);

    /// <summary>Recalculates ADU for every eligible product in the store (v2-spec §1).</summary>
    Task<(RecalculateResult? Result, string? Error)> RecalculateAsync(
        Guid tenantId,
        Guid storeId,
        CancellationToken ct = default);
}
