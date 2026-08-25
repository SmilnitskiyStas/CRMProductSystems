using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.WriteOffs.Dtos;

namespace ShelfGuard.Application.Features.WriteOffs;

public interface IWriteOffService
{
    Task<List<WriteOffDto>> GetAllAsync(Guid? storeId, string? status, CancellationToken ct = default);
    Task<PagedResult<WriteOffDto>> GetPagedAsync(
        Guid? storeId, string? status, string? search, string? sortBy, bool? sortDescending,
        int page, int pageSize, CancellationToken ct = default);
    Task<WriteOffDto?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<(WriteOffDto? WriteOff, string? Error)> CreateAsync(
        Guid tenantId, Guid createdBy, CreateWriteOffRequest request, CancellationToken ct = default);

    Task<(WriteOffDto? WriteOff, string? Error)> ApproveAsync(
        Guid id, Guid approvedBy, CancellationToken ct = default);

    Task<(WriteOffDto? WriteOff, string? Error)> RejectAsync(
        Guid id, CancellationToken ct = default);
}
