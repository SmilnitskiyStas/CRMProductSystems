using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.Notifications.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using System.Text.Json;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Application.Features.MarketingAnalytics;
using ShelfGuard.Application.Features.MarketingAnalytics.AudienceBuilder;
using ShelfGuard.Application.Features.MarketingAnalytics.AudienceBuilder.Dtos;

namespace ShelfGuard.Application.Features.Notifications;

public sealed class NotificationService : INotificationService
{
    private readonly INotificationRepository _repo;
    private readonly IMarketingAnalyticsService _marketingAnalytics;
    private readonly IAudienceBuilderService _audienceBuilder;

    public NotificationService(INotificationRepository repo, IMarketingAnalyticsService marketingAnalytics,
        IAudienceBuilderService audienceBuilder)
    {
        _repo = repo;
        _marketingAnalytics = marketingAnalytics;
        _audienceBuilder = audienceBuilder;
    }

    public async Task<IReadOnlyList<NotificationSettingDto>> GetSettingsAsync(
        Guid userId, CancellationToken ct = default)
    {
        var settings = await _repo.GetSettingsByUserAsync(userId, ct);
        return settings
            .Select(s => new NotificationSettingDto(s.Id, s.EventType, s.Channel, s.IsEnabled))
            .ToList();
    }

    public Task UpsertSettingAsync(
        Guid userId, UpsertNotificationSettingRequest request, CancellationToken ct = default)
    {
        ValidateEventType(request.EventType);
        ValidateChannel(request.Channel);
        return _repo.UpsertSettingAsync(userId, request.EventType, request.Channel, request.IsEnabled, ct);
    }

    public async Task<PagedResult<NotificationHistoryDto>> GetHistoryAsync(
        Guid tenantId, NotificationHistoryQuery query, CancellationToken ct = default)
    {
        var page = query.ClampedPage;
        var pageSize = query.ClampedPageSize;

        var (items, total) = await _repo.GetHistoryAsync(
            tenantId,
            query.Search,
            query.EventType,
            query.UserId,
            query.StoreId,
            query.DateFrom,
            query.DateTo,
            page,
            pageSize,
            ct);

        return new PagedResult<NotificationHistoryDto>
        {
            Items = items.Select(ToDto).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<NotificationHistoryDto?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default)
    {
        var item = await _repo.GetByIdAsync(id, tenantId, ct);
        return item is null ? null : ToDto(item);
    }

    public async Task<PagedResult<CustomerMessageCampaignDto>> GetCustomerCampaignsAsync(
        Guid tenantId, PagedQuery query, CancellationToken ct = default)
    {
        var page = query.ClampedPage;
        var pageSize = query.ClampedPageSize;
        var (items, total) = await _repo.GetCustomerCampaignsAsync(tenantId, page, pageSize, ct);
        return new PagedResult<CustomerMessageCampaignDto>
        {
            Items = items.Select(ToCampaignDto).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    public Task MarkAsReadAsync(Guid id, Guid tenantId, CancellationToken ct = default)
        => _repo.MarkAsReadAsync(id, tenantId, ct);

    public Task MarkAsUnreadAsync(Guid id, Guid tenantId, CancellationToken ct = default)
        => _repo.MarkAsUnreadAsync(id, tenantId, ct);

    public Task MarkAllAsReadAsync(Guid tenantId, CancellationToken ct = default)
        => _repo.MarkAllAsReadAsync(tenantId, ct);

    public Task<int> GetUnreadCountAsync(Guid tenantId, CancellationToken ct = default)
        => _repo.GetUnreadCountAsync(tenantId, ct);

    public Task SendTestAsync(
        Guid tenantId, Guid userId, TestNotificationRequest request, CancellationToken ct = default)
    {
        ValidateChannel(request.Channel);

        var item = new NotificationQueue
        {
            TenantId  = tenantId,
            UserId    = userId,
            Channel   = request.Channel,
            EventType = request.EventType,
            Payload   = $"{{\"message\":\"Тестове сповіщення\",\"eventType\":\"{request.EventType}\"}}",
            Status    = "pending",
        };

        return _repo.EnqueueAsync(item, ct);
    }

    public async Task<CreateCustomerMessageResult> CreateCustomerMessageAsync(
        Guid tenantId, Guid userId, CreateCustomerMessageRequest request, CancellationToken ct = default)
    {
        var title = request.Title?.Trim() ?? string.Empty;
        var message = request.Message?.Trim() ?? string.Empty;
        if (title.Length is < 1 or > 120) throw new ArgumentException("Title must contain 1-120 characters.");
        if (message.Length is < 1 or > 2000) throw new ArgumentException("Message must contain 1-2000 characters.");
        if (request.Audience is not ("all_customers" or "loyalty_members" or "rfm_segment" or "purchase_history"))
            throw new ArgumentException("Unknown audience.");
        RfmSegmentKey? resolvedRfmSegment = null;
        if (request.Audience == "rfm_segment")
        {
            var definition = request.RfmAudience ?? throw new ArgumentException("Select an RFM segment.");
            if (!Enum.TryParse<RfmSegmentKey>(definition.Segment, ignoreCase: false, out var parsedSegment))
                throw new ArgumentException("Unknown RFM segment.");
            resolvedRfmSegment = parsedSegment;
            if (definition.Period is not ("3m" or "6m" or "12m" or "all" or "custom"))
                throw new ArgumentException("Unknown RFM period.");
            if (definition.Period == "custom" && (!definition.From.HasValue || !definition.To.HasValue || definition.From > definition.To))
                throw new ArgumentException("Select a valid custom RFM period.");
            if (definition.EstimatedRecipients < 0)
                throw new ArgumentException("Estimated recipient count cannot be negative.");
        }
        if (request.Audience == "purchase_history")
        {
            var definition = request.PurchaseAudience ?? throw new ArgumentException("Select purchase criteria.");
            if (definition.From > definition.To) throw new ArgumentException("Select a valid purchase period.");
            if (definition.Terms is not { Count: > 0 }) throw new ArgumentException("Select at least one product or category.");
            if (definition.MinQuantity is < 0 || definition.MinAmount is < 0)
                throw new ArgumentException("Purchase thresholds cannot be negative.");
            if (definition.EstimatedRecipients < 0)
                throw new ArgumentException("Estimated recipient count cannot be negative.");
        }

        IReadOnlyList<Guid> recipientIds = [];
        if (request.Audience is "all_customers" or "loyalty_members")
        {
            recipientIds = await _repo.ResolveBasicCustomerAudienceAsync(
                tenantId, request.Audience == "loyalty_members", ct);
        }
        else if (resolvedRfmSegment.HasValue)
        {
            var definition = request.RfmAudience!;
            var (from, to) = ResolveRfmPeriod(definition.Period, definition.From, definition.To);
            recipientIds = (await _marketingAnalytics.ResolveSegmentCustomerIdsAsync(
                tenantId, definition.StoreIds, from, to, resolvedRfmSegment.Value, ct))
                .Distinct()
                .ToArray();
        }
        else if (request.Audience == "purchase_history")
        {
            var definition = request.PurchaseAudience!;
            recipientIds = await _audienceBuilder.ResolveCustomerIdsAsync(tenantId, new AudienceBuildRequest(
                definition.From, definition.To, definition.StoreIds, definition.Terms, definition.Mode,
                definition.MinQuantity, definition.MinAmount, [], PageSize: 200), ct);
        }

        var channels = request.Channels?.Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        if (channels.Length == 0) throw new ArgumentException("Select at least one channel.");
        var validChannels = new HashSet<string>(["push", "messenger", "sms"], StringComparer.OrdinalIgnoreCase);
        if (channels.Any(channel => !validChannels.Contains(channel)))
            throw new ArgumentException("Unknown customer-message channel.");
        if (channels.Contains("messenger", StringComparer.OrdinalIgnoreCase) &&
            request.MessengerProvider is not ("telegram" or "viber" or "whatsapp"))
            throw new ArgumentException("Select a messenger provider.");

        var deliveryMode = request.DeliveryMode?.Trim().ToLowerInvariant() ?? "draft";
        if (deliveryMode is not ("draft" or "send_now" or "scheduled"))
            throw new ArgumentException("Unknown delivery mode.");
        var scheduledAt = request.ScheduledAt?.ToUniversalTime();
        if (deliveryMode == "scheduled" && (!scheduledAt.HasValue || scheduledAt <= DateTime.UtcNow))
            throw new ArgumentException("Select a future delivery time.");
        if (deliveryMode != "scheduled" && scheduledAt.HasValue)
            throw new ArgumentException("Scheduled time is only allowed for scheduled delivery.");
        var campaignStatus = deliveryMode switch
        {
            "draft" => "draft",
            "scheduled" => "scheduled",
            _ => "integration_pending",
        };

        (string Title, string? ImageUrl)? content = null;
        if (request.Content is not null)
        {
            if (request.Content.Type is not ("promotion" or "banner" or "catalog"))
                throw new ArgumentException("Unknown linked content type.");
            content = await _repo.ResolveCustomerMessageContentAsync(
                tenantId, request.Content.Type, request.Content.Id, ct);
            if (content is null)
                throw new ArgumentException("Linked content was not found for this tenant.");
        }

        var campaignId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new
        {
            campaignId,
            title,
            message,
            audience = request.Audience,
            messengerProvider = request.MessengerProvider,
            rfmAudience = request.Audience == "rfm_segment" ? request.RfmAudience : null,
            purchaseAudience = request.Audience == "purchase_history" ? request.PurchaseAudience : null,
            content = request.Content is null ? null : new
            {
                type = request.Content.Type,
                id = request.Content.Id,
                title = content!.Value.Title,
                imageUrl = content.Value.ImageUrl,
            },
            resolvedRecipients = request.Audience is "rfm_segment" or "purchase_history" ? recipientIds.Count : (int?)null,
            createdBy = userId,
            delivery = "provider_not_connected",
        });
        var items = channels.Select(channel => new NotificationQueue
        {
            TenantId = tenantId,
            UserId = null,
            Title = title,
            Channel = channel.ToLowerInvariant(),
            EventType = "customer_message.created",
            Payload = payload,
            // Deliberately not "pending": the worker must not attempt delivery before a
            // provider adapter is configured. Future integrations can promote this status.
            Status = campaignStatus,
        }).ToArray();
        var audienceDefinition = request.Audience == "rfm_segment"
            ? JsonSerializer.Serialize(new
            {
                segment = request.RfmAudience!.Segment,
                period = request.RfmAudience.Period,
                from = request.RfmAudience.From,
                to = request.RfmAudience.To,
                storeIds = request.RfmAudience.StoreIds,
                estimatedRecipients = request.RfmAudience.EstimatedRecipients,
            })
            : request.Audience == "purchase_history"
                ? JsonSerializer.Serialize(request.PurchaseAudience)
                : JsonSerializer.Serialize(new { source = request.Audience });
        var campaign = new CustomerMessageCampaign
        {
            Id = campaignId,
            TenantId = tenantId,
            CreatedByUserId = userId,
            Title = title,
            Message = message,
            AudienceSource = request.Audience,
            AudienceDefinition = audienceDefinition,
            Channels = channels.Select(x => x.ToLowerInvariant()).ToList(),
            MessengerProvider = request.MessengerProvider,
            ContentType = request.Content?.Type,
            ContentId = request.Content?.Id,
            ContentTitle = content?.Title,
            ContentImageUrl = content?.ImageUrl,
            DeliveryMode = deliveryMode,
            ScheduledAt = scheduledAt,
            SubmittedAt = deliveryMode == "draft" ? null : DateTime.UtcNow,
            Status = campaignStatus,
            EstimatedRecipients = request.RfmAudience?.EstimatedRecipients ?? request.PurchaseAudience?.EstimatedRecipients ?? recipientIds.Count,
            ResolvedRecipients = recipientIds.Count,
        };
        var recipients = recipientIds.Select(customerId => new CustomerMessageRecipient
        {
            TenantId = tenantId,
            CampaignId = campaignId,
            CustomerId = customerId,
        }).ToArray();
        await _repo.CreateCustomerCampaignAsync(campaign, recipients, items, ct);
        return new CreateCustomerMessageResult(campaignId, items.Length, campaignStatus);
    }

    public async Task<CustomerMessageCampaignDto?> SubmitCustomerMessageAsync(
        Guid tenantId, Guid campaignId, SubmitCustomerMessageRequest request, CancellationToken ct = default)
    {
        var mode = request.DeliveryMode?.Trim().ToLowerInvariant() ?? string.Empty;
        if (mode is not ("send_now" or "scheduled"))
            throw new ArgumentException("A draft can only be sent now or scheduled.");
        var scheduledAt = request.ScheduledAt?.ToUniversalTime();
        if (mode == "scheduled" && (!scheduledAt.HasValue || scheduledAt <= DateTime.UtcNow))
            throw new ArgumentException("Select a future delivery time.");
        if (mode == "send_now" && scheduledAt.HasValue)
            throw new ArgumentException("Scheduled time is only allowed for scheduled delivery.");
        var campaign = await _repo.SubmitCustomerCampaignAsync(tenantId, campaignId, mode, scheduledAt, ct);
        return campaign is null ? null : ToCampaignDto(campaign);
    }

    public async Task<CustomerMessageCampaignDetailDto?> GetCustomerCampaignDetailAsync(
        Guid tenantId, Guid campaignId, CancellationToken ct = default)
    {
        var (campaign, queueItems) = await _repo.GetCustomerCampaignDetailAsync(tenantId, campaignId, ct);
        if (campaign is null) return null;
        var recipientCount = campaign.ResolvedRecipients;
        var channels = campaign.Channels.Select(channel =>
        {
            var status = queueItems.FirstOrDefault(x =>
                string.Equals(x.Channel, channel, StringComparison.OrdinalIgnoreCase))?.Status ?? campaign.Status;
            var sent = status is "sent" or "dispatched" ? recipientCount : 0;
            var failed = status == "failed" ? recipientCount : 0;
            var pending = Math.Max(0, recipientCount - sent - failed);
            return new CustomerMessageChannelSummaryDto(channel, status, recipientCount, sent, failed, pending);
        }).ToArray();
        return new CustomerMessageCampaignDetailDto(
            ToCampaignDto(campaign), channels,
            channels.Sum(x => x.RecipientCount), channels.Sum(x => x.SentCount),
            channels.Sum(x => x.FailedCount), channels.Sum(x => x.PendingCount),
            channels.Any(x => x.Status is "sent" or "dispatched" or "failed"));
    }

    private static (DateOnly From, DateOnly To) ResolveRfmPeriod(string period, DateOnly? from, DateOnly? to)
    {
        if (period == "custom") return (from!.Value, to!.Value);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return period switch
        {
            "3m" => (today.AddMonths(-3), today),
            "6m" => (today.AddMonths(-6), today),
            "12m" => (today.AddMonths(-12), today),
            "all" => (DateOnly.MinValue, today),
            _ => throw new ArgumentException("Unknown RFM period."),
        };
    }

    // ── validation ────────────────────────────────────────────────────────────

    private static readonly HashSet<string> ValidEventTypes =
    [
        "stock.expiry_warning",
        "stock.expiry_critical",
        "stock.expired",
        "stock.needs_verification",
        "weekly_report",
        "iot.temp_alert",
        "iot.offline",
        "receipt.created",
        "order.replenishment_suggested",
        "supplier.message",
        "supplier_agreement.signed",
        // Supplier-portal expansion (plan 1-partitioned-book.md, Phase 1): supplier gets
        // "new order" alerts; client gets shipped / delay-reason alerts. Registered so both
        // sides can toggle them in notification settings (the dispatch matrix lives in the
        // worker's notification-dispatch.job.ts).
        "marketplace_order.created",
        "marketplace_order.shipped",
        "marketplace_order.delay_reason_added",
        // Phase 4 (plan D5): supplier reschedules a shipped order's expected delivery date.
        "marketplace_order.delivery_rescheduled",
        "access.temporary_expiring_soon",
        "access.temporary_expired",
        "auth.password_reset_requested",
    ];

    private static readonly HashSet<string> ValidChannels =
        ["telegram", "push", "email", "webhook"];

    private static void ValidateEventType(string value)
    {
        if (!ValidEventTypes.Contains(value))
            throw new ArgumentException($"Unknown event type: {value}");
    }

    private static NotificationHistoryDto ToDto(NotificationQueue q) =>
        new(q.Id, q.EventType ?? string.Empty, q.Channel, q.Status, q.Payload, q.CreatedAt, q.IsRead, q.ReadAt,
            q.Title, q.StoreId, q.UserId);

    private static CustomerMessageCampaignDto ToCampaignDto(CustomerMessageCampaign x) => new(
        x.Id, x.Title, x.Message, x.AudienceSource, x.AudienceDefinition,
        x.Channels, x.MessengerProvider, x.ContentType, x.ContentId, x.ContentTitle,
        x.ContentImageUrl, x.DeliveryMode, x.ScheduledAt, x.SubmittedAt,
        x.EstimatedRecipients, x.ResolvedRecipients, x.Status, x.CreatedAt);

    private static void ValidateChannel(string value)
    {
        if (!ValidChannels.Contains(value))
            throw new ArgumentException($"Unknown channel: {value}");
    }
}
