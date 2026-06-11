using ShelfGuard.Application.Features.Buffer.Dtos;

namespace ShelfGuard.Application.Features.Buffer;

public interface IBufferService
{
    Task<(BufferDto? Buffer, string? Error)> GetAsync(
        Guid storeId, Guid productId, CancellationToken ct = default);

    /// <summary>
    /// Recalculates CDA buffers for every product in the store that has an effective ADU
    /// and an active supply schedule. Called on order day (v2-spec §2 rule 1).
    /// </summary>
    Task<(RecalculateBuffersResult? Result, string? Error)> RecalculateAsync(
        Guid tenantId, Guid storeId, CancellationToken ct = default);
}
