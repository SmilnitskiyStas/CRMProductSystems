using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using System.Text.Json;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class NotificationRepository : INotificationRepository
{
    private readonly AppDbContext _db;

    public NotificationRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<NotificationSetting>> GetSettingsByUserAsync(
        Guid userId, CancellationToken ct = default)
    {
        return await _db.NotificationSettings
            .Where(s => s.UserId == userId)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task UpsertSettingAsync(
        Guid userId, string eventType, string channel, bool isEnabled, CancellationToken ct = default)
    {
        var existing = await _db.NotificationSettings
            .FirstOrDefaultAsync(
                s => s.UserId == userId && s.EventType == eventType && s.Channel == channel,
                ct);

        if (existing is not null)
        {
            existing.IsEnabled = isEnabled;
        }
        else
        {
            _db.NotificationSettings.Add(new NotificationSetting
            {
                UserId    = userId,
                EventType = eventType,
                Channel   = channel,
                IsEnabled = isEnabled,
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<(IReadOnlyList<NotificationQueue> Items, int Total)> GetHistoryAsync(
        Guid tenantId,
        string? search,
        string? eventType,
        Guid? userId,
        Guid? storeId,
        DateTime? dateFrom,
        DateTime? dateTo,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        // Channel = 'system' rows are undispatched outbox intents (ADR-018 §1/§2) — never
        // real per-user notifications, so they must never leak into the UI history feed.
        var query = _db.NotificationQueues
            .Where(q => q.TenantId == tenantId && q.Channel != "system")
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            // ILike hits the pg_trgm GIN index on Title (TASK-338) — plain .Contains()/LIKE
            // translation is not guaranteed to.
            query = query.Where(q => q.Title != null && EF.Functions.ILike(q.Title, $"%{search}%"));

        if (!string.IsNullOrWhiteSpace(eventType))
            query = query.Where(q => q.EventType == eventType);
        else
            // Outbound customer campaigns have their own history screen and are not
            // incoming system notifications for the administrator.
            query = query.Where(q => q.EventType != "customer_message.created");

        if (userId.HasValue)
            query = query.Where(q => q.UserId == userId);

        if (storeId.HasValue)
            query = query.Where(q => q.StoreId == storeId);

        if (dateFrom.HasValue)
            query = query.Where(q => q.CreatedAt >= dateFrom.Value);

        if (dateTo.HasValue)
            query = query.Where(q => q.CreatedAt <= dateTo.Value);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(q => q.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task EnqueueAsync(NotificationQueue item, CancellationToken ct = default)
    {
        _db.NotificationQueues.Add(item);
        await _db.SaveChangesAsync(ct);
    }

    public async Task EnqueueManyAsync(IReadOnlyCollection<NotificationQueue> items, CancellationToken ct = default)
    {
        _db.NotificationQueues.AddRange(items);
        await _db.SaveChangesAsync(ct);
    }

    public async Task CreateCustomerCampaignAsync(
        CustomerMessageCampaign campaign,
        IReadOnlyCollection<CustomerMessageRecipient> recipients,
        IReadOnlyCollection<NotificationQueue> queueItems,
        CancellationToken ct = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        _db.CustomerMessageCampaigns.Add(campaign);
        _db.CustomerMessageRecipients.AddRange(recipients);
        _db.NotificationQueues.AddRange(queueItems);
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<(IReadOnlyList<CustomerMessageCampaign> Items, int Total)> GetCustomerCampaignsAsync(
        Guid tenantId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.CustomerMessageCampaigns
            .Where(x => x.TenantId == tenantId)
            .AsNoTracking();
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    public async Task<(string Title, string? ImageUrl)?> ResolveCustomerMessageContentAsync(
        Guid tenantId, string contentType, Guid contentId, CancellationToken ct = default)
    {
        if (contentType == "promotion")
        {
            var item = await _db.PromotionCampaigns.AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.Id == contentId)
                .Select(x => new { x.Title, x.ImageUrl })
                .FirstOrDefaultAsync(ct);
            return item is null ? null : (item.Title, item.ImageUrl);
        }
        if (contentType == "banner")
        {
            var item = await _db.Banners.AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.Id == contentId)
                .Select(x => new { x.Title, x.ImageUrl })
                .FirstOrDefaultAsync(ct);
            return item is null ? null : (item.Title, item.ImageUrl);
        }
        if (contentType == "catalog")
        {
            var item = await _db.MobileCatalogSettings.AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.Id == contentId)
                .Select(x => new { x.Title, ImageUrl = x.BannerUrl })
                .FirstOrDefaultAsync(ct);
            return item is null ? null : (item.Title, item.ImageUrl);
        }
        return null;
    }

    public async Task<CustomerMessageCampaign?> SubmitCustomerCampaignAsync(
        Guid tenantId, Guid campaignId, string deliveryMode, DateTime? scheduledAt,
        CancellationToken ct = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        var campaign = await _db.CustomerMessageCampaigns
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == campaignId, ct);
        if (campaign is null) return null;
        if (campaign.Status != "draft") throw new InvalidOperationException("Only a draft campaign can be submitted.");

        campaign.DeliveryMode = deliveryMode;
        campaign.ScheduledAt = scheduledAt;
        campaign.SubmittedAt = DateTime.UtcNow;
        campaign.Status = deliveryMode == "scheduled" ? "scheduled" : "integration_pending";
        var campaignToken = JsonSerializer.Serialize(new { campaignId });
        var queueItems = await _db.NotificationQueues
            .Where(x => x.TenantId == tenantId && x.EventType == "customer_message.created" &&
                x.Payload != null && EF.Functions.JsonContains(x.Payload, campaignToken))
            .ToListAsync(ct);
        foreach (var item in queueItems) item.Status = campaign.Status;
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return campaign;
    }

    public async Task<IReadOnlyList<Guid>> ResolveBasicCustomerAudienceAsync(
        Guid tenantId, bool loyaltyMembersOnly, CancellationToken ct = default)
    {
        if (!loyaltyMembersOnly)
            return await _db.Customers.AsNoTracking().Where(x => x.TenantId == tenantId)
                .Select(x => x.Id).ToListAsync(ct);

        return await _db.LoyaltyMemberships.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Status == LoyaltyMembershipStatus.Active && x.CustomerId != null)
            .Select(x => x.CustomerId!.Value).Distinct().ToListAsync(ct);
    }

    public async Task<(CustomerMessageCampaign? Campaign, IReadOnlyList<NotificationQueue> QueueItems)> GetCustomerCampaignDetailAsync(
        Guid tenantId, Guid campaignId, CancellationToken ct = default)
    {
        var campaign = await _db.CustomerMessageCampaigns.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == campaignId, ct);
        if (campaign is null) return (null, []);
        var token = JsonSerializer.Serialize(new { campaignId });
        var queueItems = await _db.NotificationQueues.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.EventType == "customer_message.created" &&
                x.Payload != null && EF.Functions.JsonContains(x.Payload, token))
            .ToListAsync(ct);
        return (campaign, queueItems);
    }

    public async Task<NotificationQueue?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default)
    {
        return await _db.NotificationQueues
            .FirstOrDefaultAsync(q => q.Id == id && q.TenantId == tenantId, ct);
    }

    public async Task MarkAsReadAsync(Guid id, Guid tenantId, CancellationToken ct = default)
    {
        var item = await _db.NotificationQueues
            .FirstOrDefaultAsync(q => q.Id == id && q.TenantId == tenantId, ct);
        if (item is null || item.IsRead) return;
        item.MarkRead();
        await _db.SaveChangesAsync(ct);
    }

    public async Task MarkAsUnreadAsync(Guid id, Guid tenantId, CancellationToken ct = default)
    {
        var item = await _db.NotificationQueues
            .FirstOrDefaultAsync(q => q.Id == id && q.TenantId == tenantId, ct);
        if (item is null || !item.IsRead) return;
        item.MarkUnread();
        await _db.SaveChangesAsync(ct);
    }

    public async Task MarkAllAsReadAsync(Guid tenantId, CancellationToken ct = default)
    {
        var items = await _db.NotificationQueues
            .Where(q => q.TenantId == tenantId && !q.IsRead)
            .ToListAsync(ct);
        foreach (var item in items) item.MarkRead();
        await _db.SaveChangesAsync(ct);
    }

    public async Task<int> GetUnreadCountAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await _db.NotificationQueues
            .CountAsync(q => q.TenantId == tenantId && !q.IsRead && q.EventType != "customer_message.created", ct);
    }
}
