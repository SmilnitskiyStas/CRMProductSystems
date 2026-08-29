using ShelfGuard.Application.Features.PromotionCampaigns.Dtos;

namespace ShelfGuard.Application.Features.PromotionCampaigns;

public interface IPromotionCampaignService
{
    Task<IReadOnlyList<PromotionCampaignDto>> GetAllAsync(Guid tenantId, CancellationToken ct = default);
    Task<(PromotionCampaignDto? Campaign, string? Error)> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<(PromotionCampaignDto? Campaign, string? Error)> CreateAsync(Guid tenantId, Guid userId, UpsertPromotionCampaignRequest request, CancellationToken ct = default);
    Task<(PromotionCampaignDto? Campaign, string? Error)> UpdateAsync(Guid tenantId, Guid id, Guid userId, UpsertPromotionCampaignRequest request, CancellationToken ct = default);
    Task<(PromotionCampaignDto? Campaign, string? Error)> PublishAsync(Guid tenantId, Guid id, Guid userId, CancellationToken ct = default);
    Task<(bool Success, string? Error)> CancelAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<(string? Url, string? Error)> UploadImageAsync(Guid tenantId, Guid id, Stream stream, string extension, CancellationToken ct = default);
}
