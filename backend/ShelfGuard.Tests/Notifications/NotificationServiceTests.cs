using NSubstitute;
using ShelfGuard.Application.Features.Notifications;
using ShelfGuard.Application.Features.Notifications.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
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
    private readonly NotificationService _sut;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public NotificationServiceTests() => _sut = new NotificationService(_repo);

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
}
