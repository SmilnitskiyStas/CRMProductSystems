using NSubstitute;
using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Marketplace;

/// <summary>
/// TASK-695 (Phase 8): buyer-side per-employee supplier ratings. Two paths — a delivered order
/// (rates <c>ConfirmedByUserId</c>) and a chat thread (rates a staff member who replied). Both
/// are upserts. The RLS split (buyer writes / supplier reads) is proved on real Postgres by
/// <see cref="ShelfGuard.Tests.Infrastructure.SupplierEmployeeReviewRlsIntegrationTests"/>.
/// </summary>
public sealed class SupplierEmployeeReviewServiceTests
{
    private readonly ISupplierEmployeeReviewRepository _reviews = Substitute.For<ISupplierEmployeeReviewRepository>();
    private readonly IMarketplaceOrderRepository _orders = Substitute.For<IMarketplaceOrderRepository>();
    private readonly IMarketplaceRepository _marketplace = Substitute.For<IMarketplaceRepository>();
    private readonly ISupplierChatRepository _chat = Substitute.For<ISupplierChatRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly SupplierEmployeeReviewService _sut;

    private readonly Guid _clientTenantId = Guid.NewGuid();
    private readonly Guid _supplierTenantId = Guid.NewGuid();
    private readonly Guid _supplierId = Guid.NewGuid();
    private readonly Guid _managerId = Guid.NewGuid();
    private readonly Guid _buyerUserId = Guid.NewGuid();

    public SupplierEmployeeReviewServiceTests()
    {
        _sut = new SupplierEmployeeReviewService(_reviews, _orders, _marketplace, _chat, _users);
        _marketplace.GetSupplierTenantIdAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns(_supplierTenantId);
        _users.GetByIdAsync(_buyerUserId, Arg.Any<CancellationToken>())
            .Returns(User.Create(_clientTenantId, "buyer@x.com", "Олена Замовниця", "h", "store_manager"));
    }

    private MarketplaceOrder DeliveredOrder(Guid? managerId = null) => new()
    {
        OrderNumber = "MP-2026-050",
        SupplierTenantId = _supplierTenantId,
        ClientTenantId = _clientTenantId,
        Status = MarketplaceOrderStatus.Delivered,
        ConfirmedByUserId = managerId ?? _managerId,
        ConfirmedByUserName = "Петро Менеджер",
    };

    // ── rate-manager ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RateOrderManager_HappyPath_CreatesReviewForConfirmedByUser()
    {
        var order = DeliveredOrder();
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        _reviews.GetByOrderAsync(_clientTenantId, order.Id, Arg.Any<CancellationToken>())
            .Returns((SupplierEmployeeReview?)null);

        var (review, error) = await _sut.RateOrderManagerAsync(
            _clientTenantId, order.Id, new RateSupplierEmployeeDto(5, "Швидко відвантажили"), _buyerUserId);

        Assert.Null(error);
        Assert.NotNull(review);
        Assert.Equal(_managerId, review!.SupplierUserId);
        Assert.Equal("Петро Менеджер", review.SupplierUserName);
        Assert.Equal((short)5, review.Rating);
        Assert.Equal("order", review.Source);
        Assert.Equal(order.Id, review.OrderId);

        await _reviews.Received(1).AddAsync(
            Arg.Is<SupplierEmployeeReview>(r =>
                r.ClientTenantId == _clientTenantId
                && r.SupplierTenantId == _supplierTenantId
                && r.SupplierUserId == _managerId
                && r.RatedByUserId == _buyerUserId
                && r.RatedByName == "Олена Замовниця"
                && r.Source == "order"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task RateOrderManager_RatingOutOfRange_IsRejected(int rating)
    {
        var (review, error) = await _sut.RateOrderManagerAsync(
            _clientTenantId, Guid.NewGuid(), new RateSupplierEmployeeDto(rating), _buyerUserId);

        Assert.Null(review);
        Assert.Equal(SupplierEmployeeReviewService.RatingOutOfRangeError, error);
    }

    [Fact]
    public async Task RateOrderManager_OrderNotDelivered_IsRejected()
    {
        var order = DeliveredOrder();
        order.Status = MarketplaceOrderStatus.Shipped;
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var (review, error) = await _sut.RateOrderManagerAsync(
            _clientTenantId, order.Id, new RateSupplierEmployeeDto(4), _buyerUserId);

        Assert.Null(review);
        Assert.Equal(SupplierEmployeeReviewService.OrderNotDeliveredError, error);
    }

    [Fact]
    public async Task RateOrderManager_NoResponsibleManager_IsRejected()
    {
        var order = DeliveredOrder();
        order.ConfirmedByUserId = null;
        order.ConfirmedByUserName = null;
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var (review, error) = await _sut.RateOrderManagerAsync(
            _clientTenantId, order.Id, new RateSupplierEmployeeDto(4), _buyerUserId);

        Assert.Null(review);
        Assert.Equal(SupplierEmployeeReviewService.NoResponsibleManagerError, error);
    }

    [Fact]
    public async Task RateOrderManager_ForeignOrder_IsNotFound()
    {
        var order = DeliveredOrder();
        order.ClientTenantId = Guid.NewGuid();       // someone else's order
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var (review, error) = await _sut.RateOrderManagerAsync(
            _clientTenantId, order.Id, new RateSupplierEmployeeDto(4), _buyerUserId);

        Assert.Null(review);
        Assert.Equal(SupplierEmployeeReviewService.OrderNotFoundError, error);
    }

    [Fact]
    public async Task RateOrderManager_SecondCall_UpdatesExistingReview()
    {
        var order = DeliveredOrder();
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var existing = new SupplierEmployeeReview
        {
            SupplierTenantId = _supplierTenantId,
            ClientTenantId = _clientTenantId,
            SupplierUserId = _managerId,
            SupplierUserName = "Петро Менеджер",
            RatedByUserId = _buyerUserId,
            Rating = 2,
            Comment = "Повільно",
            Source = "order",
            OrderId = order.Id,
        };
        _reviews.GetByOrderAsync(_clientTenantId, order.Id, Arg.Any<CancellationToken>()).Returns(existing);

        var (review, error) = await _sut.RateOrderManagerAsync(
            _clientTenantId, order.Id, new RateSupplierEmployeeDto(5, "Виправились"), _buyerUserId);

        Assert.Null(error);
        Assert.NotNull(review);
        Assert.Equal(existing.Id, review!.Id);
        Assert.Equal((short)5, existing.Rating);
        Assert.Equal("Виправились", existing.Comment);
        _reviews.Received(1).Update(existing);
        await _reviews.DidNotReceive().AddAsync(Arg.Any<SupplierEmployeeReview>(), Arg.Any<CancellationToken>());
    }

    // ── chat rate-participant ────────────────────────────────────────────────

    [Fact]
    public async Task RateChatParticipant_HappyPath_SnapshotsNameFromTheirMessage()
    {
        var session = new SupplierChatSession
        {
            SupplierTenantId = _supplierTenantId,
            ClientTenantId = _clientTenantId,
        };
        _chat.GetSessionAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>()).Returns(session);
        _chat.GetMessagesAsync(session.Id, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new SupplierChatMessage
            {
                SessionId = session.Id, SenderUserId = _managerId, SenderTenantId = _supplierTenantId,
                SenderName = "Ірина Підтримка", Body = "Вітаю!",
            },
        });
        _reviews.GetByChatParticipantAsync(_clientTenantId, session.Id, _managerId, Arg.Any<CancellationToken>())
            .Returns((SupplierEmployeeReview?)null);

        var (review, error) = await _sut.RateChatParticipantAsync(
            _clientTenantId, _supplierId, new RateChatParticipantDto(_managerId, 4, "Допомогла"), _buyerUserId);

        Assert.Null(error);
        Assert.NotNull(review);
        Assert.Equal("Ірина Підтримка", review!.SupplierUserName);
        Assert.Equal("chat", review.Source);
        Assert.Equal(session.Id, review.ChatSessionId);
    }

    [Fact]
    public async Task RateChatParticipant_UserNeverMessagedInThread_IsRejected()
    {
        var session = new SupplierChatSession
        {
            SupplierTenantId = _supplierTenantId,
            ClientTenantId = _clientTenantId,
        };
        _chat.GetSessionAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>()).Returns(session);
        _chat.GetMessagesAsync(session.Id, Arg.Any<CancellationToken>()).Returns(new[]
        {
            // Only a message from a DIFFERENT supplier user, plus a client message.
            new SupplierChatMessage
            {
                SessionId = session.Id, SenderUserId = Guid.NewGuid(), SenderTenantId = _supplierTenantId,
                SenderName = "Хтось Інший", Body = "Привіт",
            },
            new SupplierChatMessage
            {
                SessionId = session.Id, SenderUserId = _buyerUserId, SenderTenantId = _clientTenantId,
                SenderName = "Олена", Body = "Питання",
            },
        });

        var (review, error) = await _sut.RateChatParticipantAsync(
            _clientTenantId, _supplierId, new RateChatParticipantDto(_managerId, 4), _buyerUserId);

        Assert.Null(review);
        Assert.Equal(SupplierEmployeeReviewService.ParticipantNotInChatError, error);
    }

    [Fact]
    public async Task RateChatParticipant_NoThreadYet_IsNotFound()
    {
        _chat.GetSessionAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns((SupplierChatSession?)null);

        var (review, error) = await _sut.RateChatParticipantAsync(
            _clientTenantId, _supplierId, new RateChatParticipantDto(_managerId, 4), _buyerUserId);

        Assert.Null(review);
        Assert.Equal(SupplierEmployeeReviewService.ChatNotFoundError, error);
    }

    [Fact]
    public async Task RateChatParticipant_ClientCannotForgeASupplierUserFromTheirOwnMessage()
    {
        var session = new SupplierChatSession
        {
            SupplierTenantId = _supplierTenantId,
            ClientTenantId = _clientTenantId,
        };
        _chat.GetSessionAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>()).Returns(session);
        // The "participant" only ever sent messages from the CLIENT side — must not qualify.
        _chat.GetMessagesAsync(session.Id, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new SupplierChatMessage
            {
                SessionId = session.Id, SenderUserId = _buyerUserId, SenderTenantId = _clientTenantId,
                SenderName = "Олена", Body = "Я сам собі постачальник",
            },
        });

        var (review, error) = await _sut.RateChatParticipantAsync(
            _clientTenantId, _supplierId, new RateChatParticipantDto(_buyerUserId, 5), _buyerUserId);

        Assert.Null(review);
        Assert.Equal(SupplierEmployeeReviewService.ParticipantNotInChatError, error);
    }
}
