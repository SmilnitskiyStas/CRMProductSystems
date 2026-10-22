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
        string? source = null,
        CancellationToken ct = default);
    Task<ReceiptDto?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<(ReceiptDto? Receipt, string? Error)> CreateAsync(
        Guid tenantId, Guid createdBy, CreateReceiptRequest request, CancellationToken ct = default);

    Task<(ReceiptDto? Receipt, string? Error)> UpdateItemsAsync(
        Guid id, UpdateItemsRequest request, CancellationToken ct = default, Guid? callerUserId = null);

    /// <summary>
    /// TASK-755: take a DRAFT order into work (sets TakenByUserId/TakenAt). Idempotent for the same
    /// user; <see cref="ReceiptErrors.TakenByOther"/> when another user holds it (-> 409).
    /// </summary>
    Task<(ReceiptDto? Receipt, string? Error)> TakeAsync(Guid id, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// TASK-755: release a taken draft. Only the taker, or a caller with canOverride
    /// (enterprise_admin / network_manager); otherwise <see cref="ReceiptErrors.ReleaseForbidden"/> (-> 403).
    /// </summary>
    Task<(ReceiptDto? Receipt, string? Error)> ReleaseAsync(
        Guid id, Guid userId, bool canOverride, CancellationToken ct = default);

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

public static class ReceiptErrors
{
    public const string NotFound = "Receipt not found.";
    public const string TakenByOther = "Receipt is taken by another user.";
    public const string ReleaseForbidden = "Only the user who took the receipt (or a network manager / admin) can release it.";
}
