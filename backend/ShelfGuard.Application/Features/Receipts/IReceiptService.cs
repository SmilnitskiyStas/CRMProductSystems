using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.Receipts.Dtos;

namespace ShelfGuard.Application.Features.Receipts;

public interface IReceiptService
{
    Task<List<ReceiptDto>> GetAllAsync(Guid? storeId, string? status, CancellationToken ct = default);
    // TASK-640: categoryId/minItems/maxItems — see IReceiptRepository.GetPagedAsync.
    Task<PagedResult<ReceiptDto>> GetPagedAsync(
        Guid? storeId, string? status, string? search, string? sortBy, bool? sortDescending,
        int page, int pageSize,
        Guid? categoryId = null, int? minItems = null, int? maxItems = null,
        CancellationToken ct = default);
    Task<ReceiptDto?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<(ReceiptDto? Receipt, string? Error)> CreateAsync(
        Guid tenantId, Guid createdBy, CreateReceiptRequest request, CancellationToken ct = default);

    Task<(ReceiptDto? Receipt, string? Error)> UpdateItemsAsync(
        Guid id, UpdateItemsRequest request, CancellationToken ct = default);

    Task<(ReceiptDto? Receipt, string? Error)> ReceiveAsync(
        Guid id, Guid receivedBy, CancellationToken ct = default);

    /// <summary>
    /// Postpones the expected delivery of a delayed receipt. The receipt stays open, so the order
    /// engine keeps counting it as in transit (no re-order) — it just knows the delivery is late.
    /// </summary>
    Task<(ReceiptDto? Receipt, string? Error)> PostponeAsync(
        Guid id, Guid userId, PostponeReceiptRequest request, CancellationToken ct = default);

    Task<(ReceiptDto? Receipt, string? Error)> CancelAsync(
        Guid id, CancellationToken ct = default);
}
