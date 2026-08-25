using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.Transfers.Dtos;

namespace ShelfGuard.Application.Features.Transfers;

public interface ITransferService
{
    Task<List<TransferDto>> GetAllAsync(Guid? storeId, string? status, CancellationToken ct = default);
    Task<PagedResult<TransferDto>> GetPagedAsync(
        Guid? storeId, string? status, string? search, string? sortBy, bool? sortDescending,
        int page, int pageSize, CancellationToken ct = default);
    Task<TransferDto?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<(TransferDto? Transfer, string? Error)> CreateAsync(
        Guid tenantId, Guid initiatedBy, CreateTransferRequest request, CancellationToken ct = default);

    Task<(TransferDto? Transfer, string? Error)> ConfirmAsync(
        Guid id, Guid confirmedBy, Guid tenantId, string? role, CancellationToken ct = default);

    Task<(TransferDto? Transfer, string? Error)> CancelAsync(
        Guid id, CancellationToken ct = default);
}
