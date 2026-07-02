using ShelfGuard.Application.Features.Movements.Dtos;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Movements;

public sealed class MovementService : IMovementService
{
    private readonly IMovementRepository _repo;

    public MovementService(IMovementRepository repo) => _repo = repo;

    public async Task<MovementPageDto> GetAsync(
        Guid tenantId,
        Guid? productId,
        Guid? storeId,
        string? type,
        DateOnly? from,
        DateOnly? to,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page     = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        // DbContext is not thread-safe: run the two queries sequentially (BUG-007).
        var items = await _repo.GetAsync(tenantId, productId, storeId, type, from, to, page, pageSize, ct);
        var total = await _repo.CountAsync(tenantId, productId, storeId, type, from, to, ct);

        var dtos = items.Select(m => new MovementDto(
            m.Id,
            m.MovementType,
            m.ProductId,
            null,
            m.FromStoreId,
            null,
            m.ToStoreId,
            null,
            m.Quantity,
            m.QuantityBefore,
            m.QuantityAfter,
            m.UnitPrice,
            m.TotalAmount,
            m.ReferenceId,
            m.ReferenceType,
            m.Notes,
            m.CreatedAt
        )).ToList();

        return new MovementPageDto(dtos, total, page, pageSize);
    }
}
