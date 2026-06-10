using ShelfGuard.Application.Features.Transfers.Dtos;

namespace ShelfGuard.Application.Features.Transfers;

public interface ITransferService
{
    Task<List<TransferDto>> GetAllAsync(Guid? storeId, string? status, CancellationToken ct = default);
    Task<TransferDto?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<(TransferDto? Transfer, string? Error)> CreateAsync(
        Guid tenantId, Guid initiatedBy, CreateTransferRequest request, CancellationToken ct = default);

    Task<(TransferDto? Transfer, string? Error)> ConfirmAsync(
        Guid id, Guid confirmedBy, CancellationToken ct = default);

    Task<(TransferDto? Transfer, string? Error)> CancelAsync(
        Guid id, CancellationToken ct = default);
}
