using NSubstitute;
using ShelfGuard.Application.Features.Notifications;
using ShelfGuard.Application.Features.Notifications.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Application.Features.MarketingAnalytics;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.MarketingAnalytics.AudienceBuilder;
using ShelfGuard.Application.Features.MarketingAnalytics.AudienceBuilder.Dtos;
using Xunit;

namespace ShelfGuard.Tests.Notifications;

/// <summary>
/// TASK-360 (Block 9 pre-launch audit) — Notifications had zero test coverage. The queue
/// generation/dedup logic itself lives in the worker (expiry-check.job.ts/notification.job.ts,
/// fixed in the same task — see that diff for the table-name and threshold-mismatch bugs found)
/// and in the RLS fail-open fix covered by
/// Infrastructure.RlsCrossTenantIntegrationTests.NotificationSettings_FullyResetSession_
/// ReturnsZeroRows_NotEveryTenant. What's testable on the C# side is SendTestAsync's
/// validation, and — directly tied to the RLS fix — that every enqueued row always carries a
/// real TenantId (the fail-open branch on notification_settings/notification_queue is only
/// exploitable for rows with a null/wrong TenantId; asserting the service never produces one is
/// a cheap regression guard for that class of bug).
/// </summary>
public sealed class NotificationServiceTests
{
    private readonly INotificationRepository _repo = Substitute.For<INotificationRepository>();
    private readonly IMarketingAnalyticsService _marketingAnalytics = Substitute.For<IMarketingAnalyticsService>();
    private readonly IAudienceBuilderService _audienceBuilder = Substitute.For<IAudienceBuilderService>();
    private readonly NotificationService _sut;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public NotificationServiceTests() => _sut = new NotificationService(_repo, _marketingAnalytics, _audienceBuilder);

    [Fact]
    public async Task SendTestAsync_UnknownChannel_ThrowsArgumentException()
    {
        var request = new TestNotificationRequest("carrier_pigeon", "stock.expiry_warning");

        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.SendTestAsync(_tenantId, _userId, request));
        await _repo.DidNotReceive().EnqueueAsync(Arg.Any<NotificationQueue>(), Arg.Any<CancellationToken>());
    }

    // Note: unlike UpsertSettingAsync, SendTestAsync only validates Channel — EventType passes
    // through unchecked into the queued row. Pre-existing behavior (test-only, [Authorize]'d
    // endpoint), left as-is — not part of this task's scope.

    [Fact]
    public async Task SendTestAsync_Valid_EnqueuesWithCallersTenantId_NeverNull()
    {
        var request = new TestNotificationRequest("telegram", "stock.expiry_critical");

        await _sut.SendTestAsync(_tenantId, _userId, request);

        await _repo.Received(1).EnqueueAsync(
            Arg.Is<NotificationQueue>(q =>
                q.TenantId == _tenantId &&
                q.UserId == _userId &&
                q.Channel == "telegram" &&
                q.EventType == "stock.expiry_critical" &&
                q.Status == "pending"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpsertSettingAsync_UnknownEventType_ThrowsArgumentException()
    {
        var request = new UpsertNotificationSettingRequest("not.a.real.event", "telegram", true);

        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.UpsertSettingAsync(_userId, request));
    }

    [Fact]
    public async Task CreateCustomerMessageAsync_RfmAudience_ResolvesExactDistinctRecipients()
    {
        var customerId = Guid.NewGuid();
        _marketingAnalytics.ResolveSegmentCustomerIdsAsync(
                _tenantId, Arg.Any<IReadOnlyList<Guid>?>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(),
                RfmSegmentKey.AtRisk, Arg.Any<CancellationToken>())
            .Returns(new[] { customerId, customerId });
        var request = new CreateCustomerMessageRequest(
            "Поверніться до нас", "Для вас є спеціальна пропозиція", "rfm_segment", ["push"],
            RfmAudience: new RfmAudienceDefinition("AtRisk", "6m", null, null, [], 25));

        await _sut.CreateCustomerMessageAsync(_tenantId, _userId, request);

        await _repo.Received(1).CreateCustomerCampaignAsync(
            Arg.Is<CustomerMessageCampaign>(campaign =>
                campaign.TenantId == _tenantId && campaign.ResolvedRecipients == 1),
            Arg.Is<IReadOnlyCollection<CustomerMessageRecipient>>(recipients =>
                recipients.Count == 1 && recipients.Single().CustomerId == customerId),
            Arg.Is<IReadOnlyCollection<NotificationQueue>>(items =>
                items.Count == 1 && items.Single().Payload!.Contains("\"resolvedRecipients\":1")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCustomerCampaignsAsync_ReturnsOneCampaignPerCampaign_NotPerChannel()
    {
        var campaign = new CustomerMessageCampaign
        {
            TenantId = _tenantId,
            CreatedByUserId = _userId,
            Title = "Акція",
            Message = "Текст",
            AudienceSource = "rfm_segment",
            AudienceDefinition = "{\"segment\":\"AtRisk\"}",
            Channels = ["push", "sms"],
            ResolvedRecipients = 12,
        };
        _repo.GetCustomerCampaignsAsync(_tenantId, 1, 20, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<CustomerMessageCampaign>)new[] { campaign }, 1));

        var result = await _sut.GetCustomerCampaignsAsync(_tenantId, new PagedQuery { Page = 1, PageSize = 20 });

        Assert.Single(result.Items);
        Assert.Equal(2, result.Items[0].Channels.Count);
        Assert.Equal(12, result.Items[0].ResolvedRecipients);
    }

    [Fact]
    public async Task CreateCustomerMessageAsync_LinkedContent_SavesTenantValidatedSnapshot()
    {
        var contentId = Guid.NewGuid();
        _repo.ResolveCustomerMessageContentAsync(_tenantId, "promotion", contentId, Arg.Any<CancellationToken>())
            .Returns(("Літня акція", "/uploads/summer.jpg"));
        var request = new CreateCustomerMessageRequest(
            "Акція для вас", "Відкрийте пропозицію", "all_customers", ["push"],
            Content: new CustomerMessageContentReference("promotion", contentId));

        await _sut.CreateCustomerMessageAsync(_tenantId, _userId, request);

        await _repo.Received(1).CreateCustomerCampaignAsync(
            Arg.Is<CustomerMessageCampaign>(campaign =>
                campaign.ContentType == "promotion" && campaign.ContentId == contentId &&
                campaign.ContentTitle == "Літня акція" && campaign.ContentImageUrl == "/uploads/summer.jpg"),
            Arg.Any<IReadOnlyCollection<CustomerMessageRecipient>>(),
            Arg.Is<IReadOnlyCollection<NotificationQueue>>(items =>
                items.Single().Payload!.Contains("\"type\":\"promotion\"")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateCustomerMessageAsync_ForeignOrMissingLinkedContent_IsRejected()
    {
        var contentId = Guid.NewGuid();
        _repo.ResolveCustomerMessageContentAsync(_tenantId, "banner", contentId, Arg.Any<CancellationToken>())
            .Returns(((string Title, string? ImageUrl)?)null);
        var request = new CreateCustomerMessageRequest(
            "Новина", "Текст", "loyalty_members", ["push"],
            Content: new CustomerMessageContentReference("banner", contentId));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.CreateCustomerMessageAsync(_tenantId, _userId, request));
        await _repo.DidNotReceive().CreateCustomerCampaignAsync(
            Arg.Any<CustomerMessageCampaign>(), Arg.Any<IReadOnlyCollection<CustomerMessageRecipient>>(),
            Arg.Any<IReadOnlyCollection<NotificationQueue>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateCustomerMessageAsync_PurchaseAudience_ReusesAudienceBuilderAndFreezesRecipients()
    {
        var customerIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        _audienceBuilder.ResolveCustomerIdsAsync(_tenantId, Arg.Any<AudienceBuildRequest>(), Arg.Any<CancellationToken>())
            .Returns(customerIds);
        var purchaseAudience = new PurchaseAudienceDefinition(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 8, 30), [],
            [new AudienceTermRequest(AudienceTermKind.Text, "кава", null)],
            AudienceCombineMode.Any, 2, 500, 2);
        var request = new CreateCustomerMessageRequest(
            "Для поціновувачів кави", "Персональна пропозиція", "purchase_history", ["push"],
            PurchaseAudience: purchaseAudience);

        await _sut.CreateCustomerMessageAsync(_tenantId, _userId, request);

        await _audienceBuilder.Received(1).ResolveCustomerIdsAsync(
            _tenantId,
            Arg.Is<AudienceBuildRequest>(x => x.Terms.Count == 1 && x.MinQuantity == 2 && x.MinAmount == 500),
            Arg.Any<CancellationToken>());
        await _repo.Received(1).CreateCustomerCampaignAsync(
            Arg.Is<CustomerMessageCampaign>(x => x.AudienceSource == "purchase_history" && x.ResolvedRecipients == 2),
            Arg.Is<IReadOnlyCollection<CustomerMessageRecipient>>(x =>
                x.Count == 2 && x.Select(r => r.CustomerId).Order().SequenceEqual(customerIds.Order())),
            Arg.Any<IReadOnlyCollection<NotificationQueue>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateCustomerMessageAsync_Scheduled_SavesLifecycleAndKeepsQueueNonPending()
    {
        var scheduledAt = DateTime.UtcNow.AddDays(1);
        var request = new CreateCustomerMessageRequest(
            "Завтра нова акція", "Не пропустіть", "all_customers", ["push"],
            DeliveryMode: "scheduled", ScheduledAt: scheduledAt);

        var result = await _sut.CreateCustomerMessageAsync(_tenantId, _userId, request);

        Assert.Equal("scheduled", result.Status);
        await _repo.Received(1).CreateCustomerCampaignAsync(
            Arg.Is<CustomerMessageCampaign>(x => x.DeliveryMode == "scheduled" &&
                x.Status == "scheduled" && x.ScheduledAt == scheduledAt && x.SubmittedAt != null),
            Arg.Any<IReadOnlyCollection<CustomerMessageRecipient>>(),
            Arg.Is<IReadOnlyCollection<NotificationQueue>>(x => x.All(q => q.Status == "scheduled")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateCustomerMessageAsync_ScheduledInPast_IsRejected()
    {
        var request = new CreateCustomerMessageRequest(
            "Акція", "Текст", "all_customers", ["push"],
            DeliveryMode: "scheduled", ScheduledAt: DateTime.UtcNow.AddMinutes(-1));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.CreateCustomerMessageAsync(_tenantId, _userId, request));
    }

    [Fact]
    public async Task SubmitCustomerMessageAsync_SendNow_DelegatesAtomicDraftTransition()
    {
        var campaignId = Guid.NewGuid();
        var campaign = new CustomerMessageCampaign
        {
            Id = campaignId, TenantId = _tenantId, CreatedByUserId = _userId,
            Title = "Чернетка", Message = "Текст", AudienceSource = "all_customers",
            Channels = ["push"], DeliveryMode = "send_now", Status = "integration_pending",
            SubmittedAt = DateTime.UtcNow,
        };
        _repo.SubmitCustomerCampaignAsync(_tenantId, campaignId, "send_now", null, Arg.Any<CancellationToken>())
            .Returns(campaign);

        var result = await _sut.SubmitCustomerMessageAsync(
            _tenantId, campaignId, new SubmitCustomerMessageRequest("send_now"));

        Assert.NotNull(result);
        Assert.Equal("integration_pending", result.Status);
        Assert.Equal("send_now", result.DeliveryMode);
    }

    [Fact]
    public async Task CreateCustomerMessageAsync_LoyaltyAudience_FreezesExactBasicAudience()
    {
        var customerIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        _repo.ResolveBasicCustomerAudienceAsync(_tenantId, true, Arg.Any<CancellationToken>())
            .Returns(customerIds);
        var request = new CreateCustomerMessageRequest(
            "Для учасників", "Персональна новина", "loyalty_members", ["push"]);

        await _sut.CreateCustomerMessageAsync(_tenantId, _userId, request);

        await _repo.Received(1).CreateCustomerCampaignAsync(
            Arg.Is<CustomerMessageCampaign>(x => x.ResolvedRecipients == 2 && x.EstimatedRecipients == 2),
            Arg.Is<IReadOnlyCollection<CustomerMessageRecipient>>(x => x.Count == 2),
            Arg.Any<IReadOnlyCollection<NotificationQueue>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCustomerCampaignDetailAsync_AggregatesPreparedDeliveryByChannel()
    {
        var campaignId = Guid.NewGuid();
        var campaign = new CustomerMessageCampaign
        {
            Id = campaignId, TenantId = _tenantId, CreatedByUserId = _userId,
            Title = "Кампанія", Message = "Текст", AudienceSource = "rfm_segment",
            Channels = ["push", "sms"], ResolvedRecipients = 12, Status = "integration_pending",
            DeliveryMode = "send_now",
        };
        IReadOnlyList<NotificationQueue> queueItems =
        [
            new() { TenantId = _tenantId, Channel = "push", EventType = "customer_message.created", Status = "integration_pending" },
            new() { TenantId = _tenantId, Channel = "sms", EventType = "customer_message.created", Status = "failed" },
        ];
        _repo.GetCustomerCampaignDetailAsync(_tenantId, campaignId, Arg.Any<CancellationToken>())
            .Returns((campaign, queueItems));

        var result = await _sut.GetCustomerCampaignDetailAsync(_tenantId, campaignId);

        Assert.NotNull(result);
        Assert.Equal(24, result.TotalDeliveries);
        Assert.Equal(12, result.PendingCount);
        Assert.Equal(12, result.FailedCount);
        Assert.Equal(2, result.Channels.Count);
    }
}
