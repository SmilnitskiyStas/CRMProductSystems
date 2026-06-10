using ShelfGuard.Application.Features.Receipts.Dtos;

namespace ShelfGuard.Application.Features.Receipts;

public interface IReceiptService
{
    Task<List<ReceiptDto>> GetAllAsync(Guid? storeId, string? status, CancellationToken ct = default);
    Task<ReceiptDto?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<(ReceiptDto? Receipt, string? Error)> CreateAsync(
        Guid tenantId, Guid createdBy, CreateReceiptRequest request, CancellationToken ct = default);

    Task<(ReceiptDto? Receipt, string? Error)> UpdateItemsAsync(
        Guid id, UpdateItemsRequest request, CancellationToken ct = default);

    Task<(ReceiptDto? Receipt, string? Error)> ReceiveAsync(
        Guid id, Guid receivedBy, CancellationToken ct = default);

    Task<(ReceiptDto? Receipt, string? Error)> CancelAsync(
        Guid id, CancellationToken ct = default);
}
