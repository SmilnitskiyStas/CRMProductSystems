using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.Stock.Dtos;
using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Application.Features.Stock;

public interface IStockService
{
    Task<List<ProductStockDto>> GetAllAsync(
        Guid? storeId,
        string? status,
        Guid? zoneId,
        Guid? productId,
        CancellationToken ct = default);

    Task<PagedResult<ProductStockDto>> GetPagedAsync(
        Guid? storeId,
        string? status,
        Guid? zoneId,
        Guid? productId,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<ProductStockDto?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<List<ProductStockDto>> GetExpiringAsync(Guid? storeId, int days, CancellationToken ct = default);

    Task<List<ProductStockDto>> GetExpiredAsync(Guid? storeId, CancellationToken ct = default);

    Task<List<ProductStockDto>> GetNeedsCheckAsync(Guid? storeId, CancellationToken ct = default);

    Task<List<SuggestionDto>> GetSuggestionsAsync(Guid? storeId, CancellationToken ct = default);

    Task<(ProductStockDto? Stock, string? Error)> CreateAsync(
        Guid tenantId,
        Guid performedBy,
        CreateStockRequest request,
        CancellationToken ct = default);

    Task<(ProductStockDto? Stock, string? Error)> UpdateAsync(
        Guid id,
        UpdateStockRequest request,
        CancellationToken ct = default);

    Task<(ProductStockDto? Stock, string? Error)> VerifyAsync(
        Guid id,
        Guid performedBy,
        CancellationToken ct = default);

    Task<StockSummaryDto> GetSummaryAsync(Guid? storeId, CancellationToken ct = default);

    Task<FefoConsumeResult> FefoConsumeAsync(
        Guid tenantId,
        Guid performedBy,
        FefoConsumeRequest request,
        CancellationToken ct = default);
}
