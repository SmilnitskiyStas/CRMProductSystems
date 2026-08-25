using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using ShelfGuard.Application.Features.CustomerSupport;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.CustomerSupport;

public sealed class ConsumerSupportServiceTests
{
    private readonly IConsumerSupportTicketRepository _tickets = Substitute.For<IConsumerSupportTicketRepository>();
    private readonly IConsumerAccountRepository _consumerAccounts = Substitute.For<IConsumerAccountRepository>();
    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();
    private readonly ILoyaltyRepository _loyalty = Substitute.For<ILoyaltyRepository>();
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly ITenantSessionOverride _tenantScope = Substitute.For<ITenantSessionOverride>();
    private readonly ConsumerSupportService _sut;

    public ConsumerSupportServiceTests()
    {
        _sut = new ConsumerSupportService(
            _tickets, _consumerAccounts, _customers, _loyalty, _tenants, _tenantScope,
            NullLogger<ConsumerSupportService>.Instance);

        // Pure pass-through, same convention LoyaltyServiceTests uses for every
        // ITenantSessionOverride closed generic this service calls through it (Customer?).
        _tenantScope.ExecuteAsync(
                Arg.Any<Guid>(), Arg.Any<Func<Task<Customer?>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<Task<Customer?>>>()());
    }

    private static ConsumerAccount MakeConsumer(
        Guid? id = null, string phone = "+380501234567", string fullName = "Тест Тестенко", bool isActive = true) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Phone = phone,
        PasswordHash = "hash",
        FullName = fullName,
        IsActive = isActive,
    };

    private static Tenant MakeTenant() => Tenant.Create("Acme", "acme");

    private static ConsumerSupportTicket MakeTicket(
        Guid? id = null, Guid? tenantId = null, Guid? consumerAccountId = null,
        string status = ConsumerSupportTicketStatus.Open, Guid? customerId = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        TenantId = tenantId ?? Guid.NewGuid(),
        ConsumerAccountId = consumerAccountId ?? Guid.NewGuid(),
        CustomerId = customerId,
        Subject = "Питання по замовленню",
        Status = status,
    };

    // ── CreateTicketAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task CreateTicketAsync_unknown_consumer_returns_404()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _consumerAccounts.GetByIdAsync(consumerId, default).ReturnsNull();

        var (ticket, error, statusCode) = await _sut.CreateTicketAsync(consumerId, tenantId, "Subj", "Body");

        Assert.Null(ticket);
        Assert.Equal(404, statusCode);
        await _tickets.DidNotReceive().SaveChangesAsync(default);
    }

    [Fact]
    public async Task CreateTicketAsync_unknown_tenant_returns_404()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _consumerAccounts.GetByIdAsync(consumerId, default).Returns(MakeConsumer(consumerId));
        _tenants.GetByIdAsync(tenantId, default).ReturnsNull();

        var (ticket, error, statusCode) = await _sut.CreateTicketAsync(consumerId, tenantId, "Subj", "Body");

        Assert.Null(ticket);
        Assert.Equal(404, statusCode);
        await _tickets.DidNotReceive().SaveChangesAsync(default);
    }

    [Fact]
    public async Task CreateTicketAsync_blank_subject_returns_400_without_writing()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _consumerAccounts.GetByIdAsync(consumerId, default).Returns(MakeConsumer(consumerId));
        _tenants.GetByIdAsync(tenantId, default).Returns(MakeTenant());

        var (ticket, error, statusCode) = await _sut.CreateTicketAsync(consumerId, tenantId, "   ", "Body");

        Assert.Null(ticket);
        Assert.Equal(400, statusCode);
        await _tickets.DidNotReceive().SaveChangesAsync(default);
    }

    [Fact]
    public async Task CreateTicketAsync_blank_body_returns_400_without_writing()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _consumerAccounts.GetByIdAsync(consumerId, default).Returns(MakeConsumer(consumerId));
        _tenants.GetByIdAsync(tenantId, default).Returns(MakeTenant());

        var (ticket, error, statusCode) = await _sut.CreateTicketAsync(consumerId, tenantId, "Subj", "  ");

        Assert.Null(ticket);
        Assert.Equal(400, statusCode);
        await _tickets.DidNotReceive().SaveChangesAsync(default);
    }

    [Fact]
    public async Task CreateTicketAsync_creates_ticket_with_first_message_and_saves_once()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var consumer = MakeConsumer(consumerId);
        _consumerAccounts.GetByIdAsync(consumerId, default).Returns(consumer);
        _tenants.GetByIdAsync(tenantId, default).Returns(MakeTenant());
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default).ReturnsNull();
        _customers.FindByPhoneAsync(consumer.Phone, tenantId, default).ReturnsNull();

        var (ticket, error, statusCode) = await _sut.CreateTicketAsync(consumerId, tenantId, "  Питання  ", "  Текст  ");

        Assert.Null(error);
        Assert.NotNull(ticket);
        Assert.Equal("Питання", ticket!.Subject);
        Assert.Equal(ConsumerSupportTicketStatus.Open, ticket.Status);
        Assert.NotNull(ticket.Messages);
        Assert.Single(ticket.Messages!);
        Assert.Equal("Текст", ticket.Messages![0].Body);
        Assert.Equal(consumerId, ticket.Messages![0].SenderConsumerAccountId);
        Assert.Null(ticket.Messages![0].SenderUserId);

        await _tickets.Received(1).AddAsync(
            Arg.Is<ConsumerSupportTicket>(t =>
                t.TenantId == tenantId && t.ConsumerAccountId == consumerId &&
                t.Subject == "Питання" && t.Messages.Count == 1),
            default);
        await _tickets.Received(1).SaveChangesAsync(default);
    }

    [Fact]
    public async Task CreateTicketAsync_auto_links_CustomerId_from_existing_loyalty_membership()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var consumer = MakeConsumer(consumerId);
        _consumerAccounts.GetByIdAsync(consumerId, default).Returns(consumer);
        _tenants.GetByIdAsync(tenantId, default).Returns(MakeTenant());
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default)
            .Returns(new LoyaltyMembership { TenantId = tenantId, ConsumerAccountId = consumerId, CustomerId = customerId });

        var (ticket, error, statusCode) = await _sut.CreateTicketAsync(consumerId, tenantId, "Subj", "Body");

        Assert.Null(error);
        Assert.Equal(customerId, ticket!.CustomerId);
        // The membership already resolved a CustomerId — the phone-match fallback must not run.
        await _customers.DidNotReceive().FindByPhoneAsync(Arg.Any<string>(), Arg.Any<Guid>(), default);
    }

    [Fact]
    public async Task CreateTicketAsync_falls_back_to_phone_match_when_no_loyalty_membership()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var consumer = MakeConsumer(consumerId, phone: "+380671112233");
        _consumerAccounts.GetByIdAsync(consumerId, default).Returns(consumer);
        _tenants.GetByIdAsync(tenantId, default).Returns(MakeTenant());
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default).ReturnsNull();
        var customer = new Customer { TenantId = tenantId, Id = customerId, Name = "Іван" };
        _customers.FindByPhoneAsync("+380671112233", tenantId, default).Returns(customer);
        // ToDtoForConsumerAsync resolves CustomerName via a separate GetByIdAsync lookup (the DTO
        // builder doesn't reuse the FindByPhoneAsync result directly) — stub that path too.
        _customers.GetByIdAsync(customerId, tenantId, default).Returns(customer);

        var (ticket, error, statusCode) = await _sut.CreateTicketAsync(consumerId, tenantId, "Subj", "Body");

        Assert.Null(error);
        Assert.Equal(customerId, ticket!.CustomerId);
        Assert.Equal("Іван", ticket.CustomerName);
    }

    [Fact]
    public async Task CreateTicketAsync_no_membership_and_no_matching_customer_leaves_CustomerId_null()
    {
        var consumerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var consumer = MakeConsumer(consumerId);
        _consumerAccounts.GetByIdAsync(consumerId, default).Returns(consumer);
        _tenants.GetByIdAsync(tenantId, default).Returns(MakeTenant());
        _loyalty.GetMembershipByTenantConsumerAsync(tenantId, consumerId, default).ReturnsNull();
        _customers.FindByPhoneAsync(consumer.Phone, tenantId, default).ReturnsNull();

        var (ticket, error, statusCode) = await _sut.CreateTicketAsync(consumerId, tenantId, "Subj", "Body");

        Assert.Null(error);
        Assert.Null(ticket!.CustomerId);
    }

    // ── GetTicketAsync (consumer) ──────────────────────────────────────────

    [Fact]
    public async Task GetTicketAsync_unknown_ticket_returns_404()
    {
        var consumerId = Guid.NewGuid();
        var ticketId = Guid.NewGuid();
        _tickets.GetByIdAsync(ticketId, default).ReturnsNull();

        var (ticket, error, statusCode) = await _sut.GetTicketAsync(consumerId, ticketId);

        Assert.Null(ticket);
        Assert.Equal(404, statusCode);
    }

    [Fact]
    public async Task GetTicketAsync_another_consumers_ticket_returns_404_not_403()
    {
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var ticket = MakeTicket(consumerAccountId: owner);
        _tickets.GetByIdAsync(ticket.Id, default).Returns(ticket);

        var (result, error, statusCode) = await _sut.GetTicketAsync(stranger, ticket.Id);

        // Judgment call: uniform 404 for "doesn't exist" and "exists but isn't yours" so a
        // consumer can never learn that some other ticket id is valid.
        Assert.Null(result);
        Assert.Equal(404, statusCode);
    }

    [Fact]
    public async Task GetTicketAsync_owner_sees_ticket_with_messages()
    {
        var consumerId = Guid.NewGuid();
        var ticket = MakeTicket(consumerAccountId: consumerId);
        ticket.Messages.Add(new ConsumerSupportTicketMessage
        {
            TicketId = ticket.Id, SenderConsumerAccountId = consumerId, Body = "Привіт",
        });
        _tickets.GetByIdAsync(ticket.Id, default).Returns(ticket);
        _consumerAccounts.GetByIdAsync(consumerId, default).Returns(MakeConsumer(consumerId));

        var (result, error, statusCode) = await _sut.GetTicketAsync(consumerId, ticket.Id);

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Single(result!.Messages!);
    }

    // ── AddConsumerMessageAsync ────────────────────────────────────────────

    [Fact]
    public async Task AddConsumerMessageAsync_another_consumers_ticket_returns_404()
    {
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var ticket = MakeTicket(consumerAccountId: owner);
        _tickets.GetByIdAsync(ticket.Id, default).Returns(ticket);

        var (message, error, statusCode) = await _sut.AddConsumerMessageAsync(stranger, ticket.Id, "Body");

        Assert.Null(message);
        Assert.Equal(404, statusCode);
        await _tickets.DidNotReceive().AddMessageAsync(Arg.Any<ConsumerSupportTicketMessage>(), default);
    }

    [Theory]
    [InlineData(ConsumerSupportTicketStatus.Resolved)]
    [InlineData(ConsumerSupportTicketStatus.Closed)]
    public async Task AddConsumerMessageAsync_reopens_ticket_after_resolved_or_closed(string closedStatus)
    {
        var consumerId = Guid.NewGuid();
        var ticket = MakeTicket(consumerAccountId: consumerId, status: closedStatus);
        _tickets.GetByIdAsync(ticket.Id, default).Returns(ticket);

        var (message, error, statusCode) = await _sut.AddConsumerMessageAsync(consumerId, ticket.Id, "Ще одне питання");

        Assert.Null(error);
        Assert.NotNull(message);
        Assert.Equal(ConsumerSupportTicketStatus.Open, ticket.Status);
        _tickets.Received(1).Update(ticket);
        await _tickets.Received(1).SaveChangesAsync(default);
    }

    [Fact]
    public async Task AddConsumerMessageAsync_open_ticket_status_is_unaffected()
    {
        var consumerId = Guid.NewGuid();
        var ticket = MakeTicket(consumerAccountId: consumerId, status: ConsumerSupportTicketStatus.InProgress);
        _tickets.GetByIdAsync(ticket.Id, default).Returns(ticket);

        var (message, error, statusCode) = await _sut.AddConsumerMessageAsync(consumerId, ticket.Id, "Деталі");

        Assert.Null(error);
        Assert.Equal(ConsumerSupportTicketStatus.InProgress, ticket.Status);
    }

    [Fact]
    public async Task AddConsumerMessageAsync_blank_body_returns_400()
    {
        var consumerId = Guid.NewGuid();
        var ticket = MakeTicket(consumerAccountId: consumerId);

        var (message, error, statusCode) = await _sut.AddConsumerMessageAsync(consumerId, ticket.Id, "   ");

        Assert.Null(message);
        Assert.Equal(400, statusCode);
        await _tickets.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), default);
    }

    // ── Staff: AddStaffReplyAsync ──────────────────────────────────────────

    [Fact]
    public async Task AddStaffReplyAsync_wrong_tenant_returns_404()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var staffUserId = Guid.NewGuid();
        var ticket = MakeTicket(tenantId: otherTenantId);
        _tickets.GetByIdAsync(ticket.Id, default).Returns(ticket);

        var (message, error, statusCode) = await _sut.AddStaffReplyAsync(tenantId, ticket.Id, staffUserId, "Відповідь");

        Assert.Null(message);
        Assert.Equal(404, statusCode);
        await _tickets.DidNotReceive().AddMessageAsync(Arg.Any<ConsumerSupportTicketMessage>(), default);
    }

    [Fact]
    public async Task AddStaffReplyAsync_updates_ticket_UpdatedAt_and_sets_sender_user_id()
    {
        var tenantId = Guid.NewGuid();
        var staffUserId = Guid.NewGuid();
        var ticket = MakeTicket(tenantId: tenantId);
        _tickets.GetByIdAsync(ticket.Id, default).Returns(ticket);

        var before = DateTimeOffset.UtcNow;
        var (message, error, statusCode) = await _sut.AddStaffReplyAsync(tenantId, ticket.Id, staffUserId, "Відповідь");

        Assert.Null(error);
        Assert.NotNull(message);
        Assert.Equal(staffUserId, message!.SenderUserId);
        Assert.Null(message.SenderConsumerAccountId);
        Assert.True(ticket.UpdatedAt >= before);
        _tickets.Received(1).Update(ticket);
        await _tickets.Received(1).SaveChangesAsync(default);
    }

    [Fact]
    public async Task AddStaffReplyAsync_blank_body_returns_400()
    {
        var tenantId = Guid.NewGuid();
        var staffUserId = Guid.NewGuid();

        var (message, error, statusCode) = await _sut.AddStaffReplyAsync(tenantId, Guid.NewGuid(), staffUserId, "");

        Assert.Null(message);
        Assert.Equal(400, statusCode);
    }

    // ── Staff: UpdateStatusAsync ───────────────────────────────────────────

    [Fact]
    public async Task UpdateStatusAsync_unknown_status_returns_400_without_lookup()
    {
        var tenantId = Guid.NewGuid();
        var staffUserId = Guid.NewGuid();

        var (ticket, error, statusCode) = await _sut.UpdateStatusAsync(tenantId, Guid.NewGuid(), staffUserId, "bogus");

        Assert.Null(ticket);
        Assert.Equal(400, statusCode);
        await _tickets.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), default);
    }

    [Fact]
    public async Task UpdateStatusAsync_wrong_tenant_returns_404()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var staffUserId = Guid.NewGuid();
        var ticket = MakeTicket(tenantId: otherTenantId);
        _tickets.GetByIdAsync(ticket.Id, default).Returns(ticket);

        var (result, error, statusCode) = await _sut.UpdateStatusAsync(
            tenantId, ticket.Id, staffUserId, ConsumerSupportTicketStatus.Resolved);

        Assert.Null(result);
        Assert.Equal(404, statusCode);
    }

    [Fact]
    public async Task UpdateStatusAsync_valid_transition_updates_status_and_saves()
    {
        var tenantId = Guid.NewGuid();
        var staffUserId = Guid.NewGuid();
        var ticket = MakeTicket(tenantId: tenantId, status: ConsumerSupportTicketStatus.Open);
        _tickets.GetByIdAsync(ticket.Id, default).Returns(ticket);
        _consumerAccounts.GetByIdAsync(ticket.ConsumerAccountId, default).Returns(MakeConsumer(ticket.ConsumerAccountId));

        var (result, error, statusCode) = await _sut.UpdateStatusAsync(
            tenantId, ticket.Id, staffUserId, ConsumerSupportTicketStatus.Resolved);

        Assert.Null(error);
        Assert.Equal(ConsumerSupportTicketStatus.Resolved, result!.Status);
        Assert.Equal(ConsumerSupportTicketStatus.Resolved, ticket.Status);
        await _tickets.Received(1).SaveChangesAsync(default);
    }

    // ── Staff: GetTicketForStaffAsync ──────────────────────────────────────

    [Fact]
    public async Task GetTicketForStaffAsync_marks_unread_consumer_messages_read_but_leaves_staff_messages_alone()
    {
        var tenantId = Guid.NewGuid();
        var ticket = MakeTicket(tenantId: tenantId);
        var consumerMsg = new ConsumerSupportTicketMessage
        {
            TicketId = ticket.Id, SenderConsumerAccountId = ticket.ConsumerAccountId, Body = "Питання", IsRead = false,
        };
        var staffMsg = new ConsumerSupportTicketMessage
        {
            TicketId = ticket.Id, SenderUserId = Guid.NewGuid(), Body = "Відповідь", IsRead = false,
        };
        ticket.Messages.Add(consumerMsg);
        ticket.Messages.Add(staffMsg);
        _tickets.GetByIdAsync(ticket.Id, default).Returns(ticket);
        _consumerAccounts.GetByIdAsync(ticket.ConsumerAccountId, default).Returns(MakeConsumer(ticket.ConsumerAccountId));

        var (result, error, statusCode) = await _sut.GetTicketForStaffAsync(tenantId, ticket.Id);

        Assert.Null(error);
        Assert.True(consumerMsg.IsRead);
        Assert.False(staffMsg.IsRead); // staff's own message is never "unread" from the staff side
        await _tickets.Received(1).SaveChangesAsync(default);
    }

    [Fact]
    public async Task GetTicketForStaffAsync_no_unread_messages_does_not_save()
    {
        var tenantId = Guid.NewGuid();
        var ticket = MakeTicket(tenantId: tenantId);
        ticket.Messages.Add(new ConsumerSupportTicketMessage
        {
            TicketId = ticket.Id, SenderConsumerAccountId = ticket.ConsumerAccountId, Body = "Питання", IsRead = true,
        });
        _tickets.GetByIdAsync(ticket.Id, default).Returns(ticket);
        _consumerAccounts.GetByIdAsync(ticket.ConsumerAccountId, default).Returns(MakeConsumer(ticket.ConsumerAccountId));

        var (result, error, statusCode) = await _sut.GetTicketForStaffAsync(tenantId, ticket.Id);

        Assert.Null(error);
        await _tickets.DidNotReceive().SaveChangesAsync(default);
    }

    [Fact]
    public async Task GetTicketForStaffAsync_wrong_tenant_returns_404()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var ticket = MakeTicket(tenantId: otherTenantId);
        _tickets.GetByIdAsync(ticket.Id, default).Returns(ticket);

        var (result, error, statusCode) = await _sut.GetTicketForStaffAsync(tenantId, ticket.Id);

        Assert.Null(result);
        Assert.Equal(404, statusCode);
    }
}
