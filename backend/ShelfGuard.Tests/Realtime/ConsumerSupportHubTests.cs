using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Realtime;
using Xunit;

namespace ShelfGuard.Tests.Realtime;

/// <summary>
/// TASK-625: <see cref="ConsumerSupportHub"/>'s Join/LeaveTicket authorization. Constructed
/// directly (no live server/WebSocket) — <c>Hub.Context</c>/<c>Hub.Groups</c> have public
/// setters specifically to support this style of unit test, same idea as mocking
/// <c>HttpContext</c> for a controller test.
/// </summary>
public sealed class ConsumerSupportHubTests
{
    private readonly IConsumerSupportTicketRepository _tickets = Substitute.For<IConsumerSupportTicketRepository>();
    private readonly IGroupManager _groups = Substitute.For<IGroupManager>();
    private readonly ConsumerSupportHub _sut;

    public ConsumerSupportHubTests()
    {
        _sut = new ConsumerSupportHub(_tickets, NullLogger<ConsumerSupportHub>.Instance)
        {
            Groups = _groups,
        };
    }

    private void SetCaller(ClaimsPrincipal user, string connectionId = "conn-1") =>
        _sut.Context = new FakeHubCallerContext(user, connectionId);

    private static ClaimsPrincipal ConsumerPrincipal(Guid consumerAccountId) => new(new ClaimsIdentity(new[]
    {
        new Claim("consumer_account_id", consumerAccountId.ToString()),
        new Claim(ClaimTypes.Role, "consumer"),
    }, "test"));

    private static ClaimsPrincipal StaffPrincipal(Guid tenantId, string role = "store_manager") => new(new ClaimsIdentity(new[]
    {
        new Claim("tenant_id", tenantId.ToString()),
        new Claim(ClaimTypes.Role, role),
    }, "test"));

    private static ConsumerSupportTicket MakeTicket(Guid tenantId, Guid consumerAccountId) => new()
    {
        TenantId = tenantId,
        ConsumerAccountId = consumerAccountId,
        Subject = "Питання",
    };

    // ── Consumer ────────────────────────────────────────────────────────────

    [Fact]
    public async Task JoinTicket_consumer_can_join_own_ticket()
    {
        var consumerId = Guid.NewGuid();
        var ticket = MakeTicket(Guid.NewGuid(), consumerId);
        _tickets.GetByIdAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);
        SetCaller(ConsumerPrincipal(consumerId), "conn-1");

        await _sut.JoinTicket(ticket.Id);

        await _groups.Received(1).AddToGroupAsync("conn-1", $"consumer-support-ticket:{ticket.Id:D}", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinTicket_consumer_cannot_join_another_consumers_ticket()
    {
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var ticket = MakeTicket(Guid.NewGuid(), owner);
        _tickets.GetByIdAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);
        SetCaller(ConsumerPrincipal(stranger));

        await Assert.ThrowsAsync<HubException>(() => _sut.JoinTicket(ticket.Id));

        await _groups.DidNotReceiveWithAnyArgs().AddToGroupAsync(default!, default!, default);
    }

    [Fact]
    public async Task JoinTicket_consumer_unknown_ticket_is_denied()
    {
        var consumerId = Guid.NewGuid();
        var ticketId = Guid.NewGuid();
        _tickets.GetByIdAsync(ticketId, Arg.Any<CancellationToken>()).ReturnsNull();
        SetCaller(ConsumerPrincipal(consumerId));

        await Assert.ThrowsAsync<HubException>(() => _sut.JoinTicket(ticketId));

        await _groups.DidNotReceiveWithAnyArgs().AddToGroupAsync(default!, default!, default);
    }

    // ── Staff ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task JoinTicket_staff_can_join_ticket_of_own_tenant()
    {
        var tenantId = Guid.NewGuid();
        var ticket = MakeTicket(tenantId, Guid.NewGuid());
        _tickets.GetByIdAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);
        SetCaller(StaffPrincipal(tenantId), "conn-2");

        await _sut.JoinTicket(ticket.Id);

        await _groups.Received(1).AddToGroupAsync("conn-2", $"consumer-support-ticket:{ticket.Id:D}", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinTicket_staff_cannot_join_ticket_of_another_tenant()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var ticket = MakeTicket(otherTenantId, Guid.NewGuid());
        _tickets.GetByIdAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);
        SetCaller(StaffPrincipal(tenantId));

        await Assert.ThrowsAsync<HubException>(() => _sut.JoinTicket(ticket.Id));

        await _groups.DidNotReceiveWithAnyArgs().AddToGroupAsync(default!, default!, default);
    }

    [Theory]
    [InlineData("cashier")]
    [InlineData("storekeeper")]
    [InlineData("merchandiser")]
    public async Task JoinTicket_staff_role_below_store_manager_floor_is_denied(string role)
    {
        var tenantId = Guid.NewGuid();
        var ticket = MakeTicket(tenantId, Guid.NewGuid());
        _tickets.GetByIdAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);
        SetCaller(StaffPrincipal(tenantId, role));

        await Assert.ThrowsAsync<HubException>(() => _sut.JoinTicket(ticket.Id));

        await _groups.DidNotReceiveWithAnyArgs().AddToGroupAsync(default!, default!, default);
    }

    [Fact]
    public async Task JoinTicket_connection_with_neither_claim_is_denied()
    {
        var ticket = MakeTicket(Guid.NewGuid(), Guid.NewGuid());
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity()); // no consumer_account_id, no tenant_id
        SetCaller(anonymous);

        await Assert.ThrowsAsync<HubException>(() => _sut.JoinTicket(ticket.Id));

        // Denied before even looking up the ticket — neither claim present.
        await _tickets.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ── LeaveTicket ─────────────────────────────────────────────────────────

    [Fact]
    public async Task LeaveTicket_removes_connection_from_group_without_ownership_check()
    {
        var ticketId = Guid.NewGuid();
        SetCaller(ConsumerPrincipal(Guid.NewGuid()), "conn-3");

        await _sut.LeaveTicket(ticketId);

        await _groups.Received(1).RemoveFromGroupAsync("conn-3", $"consumer-support-ticket:{ticketId:D}", Arg.Any<CancellationToken>());
        await _tickets.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Minimal <see cref="HubCallerContext"/> fake — only what ConsumerSupportHub
    /// touches (User, ConnectionId) needs to be real; the rest throws if ever exercised.</summary>
    private sealed class FakeHubCallerContext : HubCallerContext
    {
        public FakeHubCallerContext(ClaimsPrincipal user, string connectionId)
        {
            User = user;
            ConnectionId = connectionId;
        }

        public override string ConnectionId { get; }
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User { get; }
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features => throw new NotSupportedException();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() => throw new NotSupportedException();
    }
}
