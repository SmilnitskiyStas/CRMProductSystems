using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.MarketingAnalytics.AudienceBuilder.Dtos;

namespace ShelfGuard.Application.Features.Notifications.Dtos;

public sealed record NotificationSettingDto(
    Guid   Id,
    string EventType,
    string Channel,
    bool   IsEnabled
);

public sealed record UpsertNotificationSettingRequest(
    string EventType,
    string Channel,
    bool   IsEnabled
);

public sealed record NotificationHistoryDto(
    Guid      Id,
    string    EventType,
    string    Channel,
    string    Status,
    string?   Payload,
    DateTime  CreatedAt,
    bool      IsRead,
    DateTime? ReadAt,
    string?   Title,
    Guid?     StoreId,
    Guid?     UserId
);

/// <summary>
/// Filter + pagination parameters for GET /api/notifications/history (ADR-018 §3/§4).
/// <see cref="Search"/> runs against <c>Title</c> via <c>EF.Functions.ILike</c> to hit the
/// pg_trgm GIN index added in TASK-338.
/// </summary>
public sealed class NotificationHistoryQuery
{
    public string?   Search    { get; init; }
    public string?   EventType { get; init; }
    public Guid?     UserId    { get; init; }
    public Guid?     StoreId   { get; init; }
    public DateTime? DateFrom  { get; init; }
    public DateTime? DateTo    { get; init; }

    public int Page     { get; init; } = 1;
    public int PageSize { get; init; } = 50;

    /// <summary>Page clamped to [1, ∞). PageSize clamped to [1, 200] — same bounds as PagedQuery.</summary>
    public int ClampedPage => Math.Max(1, Page);
    public int ClampedPageSize => Math.Clamp(PageSize, 1, 200);
}

public sealed record TestNotificationRequest(
    string Channel,
    string EventType
);

public sealed record CreateCustomerMessageRequest(
    string Title,
    string Message,
    string Audience,
    string[] Channels,
    string? MessengerProvider = null,
    RfmAudienceDefinition? RfmAudience = null,
    CustomerMessageContentReference? Content = null,
    PurchaseAudienceDefinition? PurchaseAudience = null,
    string DeliveryMode = "draft",
    DateTime? ScheduledAt = null);

public sealed record SubmitCustomerMessageRequest(string DeliveryMode, DateTime? ScheduledAt = null);

public sealed record CustomerMessageContentReference(string Type, Guid Id);

public sealed record PurchaseAudienceDefinition(
    DateOnly From,
    DateOnly To,
    Guid[] StoreIds,
    IReadOnlyList<AudienceTermRequest> Terms,
    AudienceCombineMode Mode,
    decimal? MinQuantity,
    decimal? MinAmount,
    int EstimatedRecipients);

public sealed record RfmAudienceDefinition(
    string Segment,
    string Period,
    DateOnly? From,
    DateOnly? To,
    Guid[] StoreIds,
    int EstimatedRecipients);

public sealed record CreateCustomerMessageResult(Guid CampaignId, int QueuedChannels, string Status);

public sealed record CustomerMessageCampaignDto(
    Guid Id,
    string Title,
    string Message,
    string AudienceSource,
    string AudienceDefinition,
    IReadOnlyList<string> Channels,
    string? MessengerProvider,
    string? ContentType,
    Guid? ContentId,
    string? ContentTitle,
    string? ContentImageUrl,
    string DeliveryMode,
    DateTime? ScheduledAt,
    DateTime? SubmittedAt,
    int EstimatedRecipients,
    int ResolvedRecipients,
    string Status,
    DateTime CreatedAt);

public sealed record CustomerMessageChannelSummaryDto(
    string Channel,
    string Status,
    int RecipientCount,
    int SentCount,
    int FailedCount,
    int PendingCount);

public sealed record CustomerMessageCampaignDetailDto(
    CustomerMessageCampaignDto Campaign,
    IReadOnlyList<CustomerMessageChannelSummaryDto> Channels,
    int TotalDeliveries,
    int SentCount,
    int FailedCount,
    int PendingCount,
    bool ProvidersConnected);
