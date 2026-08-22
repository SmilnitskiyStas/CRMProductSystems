using NSubstitute;
using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Marketplace;

/// <summary>
/// TASK-316: client → supplier support tickets — mostly covered indirectly elsewhere; this file
/// focuses on TASK-599 Wave 2's <see cref="SupplierSupportService.CreateSystemTicketAsync"/>, the
/// system-originated ticket path MarketplaceOrderReceiptService.ReceiveAsync uses to auto-open a
/// ticket from a receipt discrepancy.
/// </summary>
public sealed class SupplierSupportServiceTests
{
    private readonly ISupplierSupportTicketRepository _tickets = Substitute.For<ISupplierSupportTicketRepository>();
    private readonly IMarketplaceRepository _marketplace = Substitute.For<IMarketplaceRepository>();
    private readonly ISupplierChatRepository _tenantNames = Substitute.For<ISupplierChatRepository>();
    private readonly IMarketplaceOrderRepository _orders = Substitute.For<IMarketplaceOrderRepository>();
    private readonly SupplierSupportService _sut;

    private readonly Guid _clientTenantId = Guid.NewGuid();
    private readonly Guid _supplierTenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public SupplierSupportServiceTests()
    {
        _sut = new SupplierSupportService(_tickets, _marketplace, _tenantNames, _orders);

        _tenantNames.GetTenantDisplayNameAsync(_supplierTenantId, Arg.Any<CancellationToken>())
            .Returns("Постачальник ТОВ");
        _tenantNames.GetTenantDisplayNameAsync(_clientTenantId, Arg.Any<CancellationToken>())
            .Returns("Клієнт ТОВ");
    }

    [Fact]
    public async Task CreateSystemTicketAsync_BuildsTicketAndFirstMessage_WithMarketplaceOrderIdSet()
    {
        var orderId = Guid.NewGuid();
        SupplierSupportTicket? added = null;
        _tickets.AddAsync(Arg.Do<SupplierSupportTicket>(t => added = t), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var order = new MarketplaceOrder
        {
            OrderNumber = "MP-2026-007",
            AgreementId = Guid.NewGuid(),
            SupplierTenantId = _supplierTenantId,
            ClientTenantId = _clientTenantId,
            Status = MarketplaceOrderStatus.Delivered,
        };
        _orders.GetByIdAsync(orderId, Arg.Any<CancellationToken>()).Returns(order);

        var dto = await _sut.CreateSystemTicketAsync(
            _clientTenantId, _supplierTenantId, orderId,
            "Розбіжності при прийомці замовлення MP-2026-007",
            "Хліб житній: Пошкоджена упаковка",
            _userId);

        Assert.NotNull(added);
        Assert.Equal(_supplierTenantId, added!.SupplierTenantId);
        Assert.Equal(_clientTenantId, added.ClientTenantId);
        Assert.Equal(orderId, added.MarketplaceOrderId);
        Assert.Equal(SupplierSupportTicketStatus.Open, added.Status);
        Assert.Equal(_userId, added.CreatedByUserId);

        var message = Assert.Single(added.Messages);
        Assert.Equal(_clientTenantId, message.SenderTenantId);
        Assert.Equal(_userId, message.SenderUserId);
        Assert.Equal("Хліб житній: Пошкоджена упаковка", message.Body);

        // Deliberately does NOT save — the caller (running inside its own tenant-session
        // override) flushes together with its own writes.
        await _tickets.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());

        Assert.Equal(_supplierTenantId, dto.SupplierTenantId);
        Assert.Equal(_clientTenantId, dto.ClientTenantId);
        Assert.Equal("MP-2026-007", dto.OrderNumber);
        Assert.Equal(SupplierSupportTicketStatus.Open, dto.Status);
    }

    [Fact]
    public async Task CreateSystemTicketAsync_ReturnsNullOrderNumber_WhenOrderCannotBeResolved()
    {
        var orderId = Guid.NewGuid();
        _tickets.AddAsync(Arg.Any<SupplierSupportTicket>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _orders.GetByIdAsync(orderId, Arg.Any<CancellationToken>()).Returns((MarketplaceOrder?)null);

        var dto = await _sut.CreateSystemTicketAsync(
            _clientTenantId, _supplierTenantId, orderId, "Subject", "Body", _userId);

        Assert.Null(dto.OrderNumber);
    }
}
