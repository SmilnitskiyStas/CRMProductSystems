using ShelfGuard.Application.Features.Banners.Dtos;

namespace ShelfGuard.Application.Features.Banners;

public interface IBannerService
{
    Task<IReadOnlyList<BannerDto>> GetAllAsync(
        Guid tenantId, BannerListQuery query, CancellationToken ct = default);

    Task<(BannerDto? Banner, string? Error)> GetByIdAsync(
        Guid tenantId, Guid id, CancellationToken ct = default);

    Task<(BannerDto? Banner, string? Error)> CreateAsync(
        Guid tenantId, Guid? createdBy, CreateBannerRequest request, CancellationToken ct = default);

    Task<(BannerDto? Banner, string? Error)> UpdateAsync(
        Guid tenantId, Guid id, UpdateBannerRequest request, CancellationToken ct = default);

    /// <summary>Soft-hide — flips IsActive to false. Never a hard delete.</summary>
    Task<(bool Success, string? Error)> DeactivateAsync(
        Guid tenantId, Guid id, CancellationToken ct = default);

    Task<(string? Url, string? Error)> UploadImageAsync(
        Guid tenantId, Guid id, Stream imageStream, string fileName, CancellationToken ct = default);

    Task<(BannerAnalyticsDto? Analytics, string? Error)> GetAnalyticsAsync(
        Guid tenantId, Guid id, CancellationToken ct = default);
}
