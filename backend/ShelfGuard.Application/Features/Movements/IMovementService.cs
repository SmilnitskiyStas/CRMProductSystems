using ShelfGuard.Application.Features.Movements.Dtos;

namespace ShelfGuard.Application.Features.Movements;

public interface IMovementService
{
    Task<MovementPageDto> GetAsync(
        Guid tenantId,
        Guid? productId,
        Guid? storeId,
        string? type,
        DateOnly? from,
        DateOnly? to,
        int page,
        int pageSize,
        CancellationToken ct = default);
}
