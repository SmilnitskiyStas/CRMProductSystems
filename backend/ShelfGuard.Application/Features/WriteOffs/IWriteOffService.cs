using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.WriteOffs.Dtos;

namespace ShelfGuard.Application.Features.WriteOffs;

public interface IWriteOffService
{
    Task<List<WriteOffDto>> GetAllAsync(Guid? storeId, string? status, CancellationToken ct = default);
    // TASK-640: categoryId/minLossAmount/maxLossAmount — see IWriteOffRepository.GetPagedAsync.
    Task<PagedResult<WriteOffDto>> GetPagedAsync(
        Guid? storeId, string? status, string? search, string? sortBy, bool? sortDescending,
        int page, int pageSize,
        Guid? categoryId = null, decimal? minLossAmount = null, decimal? maxLossAmount = null,
        CancellationToken ct = default);
    Task<WriteOffDto?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<(WriteOffDto? WriteOff, string? Error)> CreateAsync(
        Guid tenantId, Guid createdBy, CreateWriteOffRequest request, CancellationToken ct = default);

    Task<(WriteOffDto? WriteOff, string? Error)> ApproveAsync(
        Guid id, Guid approvedBy, CancellationToken ct = default);

    /// <summary>
    /// Approve with structured shortfall info. When <paramref name="excludeProblemItems"/> is true,
    /// lines lacking stock are dropped from the document (totals recomputed) and the rest approved.
    /// </summary>
    Task<(WriteOffDto? WriteOff, string? Error, List<WriteOffApprovalProblemDto> Problems)> ApproveWithOptionsAsync(
        Guid id, Guid approvedBy, bool excludeProblemItems, CancellationToken ct = default);

    Task<(WriteOffDto? WriteOff, string? Error)> RejectAsync(
        Guid id, CancellationToken ct = default);
}
