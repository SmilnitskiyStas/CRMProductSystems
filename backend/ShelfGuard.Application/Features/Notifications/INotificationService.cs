using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.Notifications.Dtos;

namespace ShelfGuard.Application.Features.Notifications;

public interface INotificationService
{
    Task<IReadOnlyList<NotificationSettingDto>> GetSettingsAsync(Guid userId, CancellationToken ct = default);
    Task UpsertSettingAsync(Guid userId, UpsertNotificationSettingRequest request, CancellationToken ct = default);

    Task<PagedResult<NotificationHistoryDto>> GetHistoryAsync(
        Guid tenantId, NotificationHistoryQuery query, CancellationToken ct = default);
    Task SendTestAsync(Guid tenantId, Guid userId, TestNotificationRequest request, CancellationToken ct = default);

    Task<NotificationHistoryDto?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default);
    Task<CreateCustomerMessageResult> CreateCustomerMessageAsync(
        Guid tenantId, Guid userId, CreateCustomerMessageRequest request, CancellationToken ct = default);
    Task<PagedResult<CustomerMessageCampaignDto>> GetCustomerCampaignsAsync(
        Guid tenantId, PagedQuery query, CancellationToken ct = default);
    Task<CustomerMessageCampaignDto?> SubmitCustomerMessageAsync(
        Guid tenantId, Guid campaignId, SubmitCustomerMessageRequest request, CancellationToken ct = default);
    Task<CustomerMessageCampaignDetailDto?> GetCustomerCampaignDetailAsync(
        Guid tenantId, Guid campaignId, CancellationToken ct = default);
    Task MarkAsReadAsync(Guid id, Guid tenantId, CancellationToken ct = default);
    Task MarkAsUnreadAsync(Guid id, Guid tenantId, CancellationToken ct = default);
    Task MarkAllAsReadAsync(Guid tenantId, CancellationToken ct = default);
    Task<int> GetUnreadCountAsync(Guid tenantId, CancellationToken ct = default);
}
