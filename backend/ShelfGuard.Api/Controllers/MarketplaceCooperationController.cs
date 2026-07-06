using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Infrastructure.Authorization;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Client side of supplier cooperation (TASK-317): cooperation requests,
/// contract download, marketplace orders (gated by an ACTIVE agreement → 403
/// otherwise), and supplier support tickets (open to everyone — no agreement
/// required). Same auth model as the marketplace review POST: any authenticated
/// tenant user with the "marketplace" module.
/// </summary>
[ApiController]
[Route("api/marketplace")]
[Authorize]
[RequireModule("marketplace")]
public sealed class MarketplaceCooperationController : ControllerBase
{
    private readonly ISupplierAgreementService _agreements;
    private readonly IMarketplaceOrderService _orders;
    private readonly ISupplierSupportService _support;

    public MarketplaceCooperationController(
        ISupplierAgreementService agreements,
        IMarketplaceOrderService orders,
        ISupplierSupportService support)
    {
        _agreements = agreements;
        _orders     = orders;
        _support    = support;
    }

    // ── Cooperation requests / agreements ─────────────────────────────────────

    /// <summary>Submits a cooperation request to a supplier. 409 when a live agreement already exists.</summary>
    [HttpPost("suppliers/{id:guid}/cooperation-requests")]
    [ProducesResponseType(typeof(CooperationAgreementDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(object), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubmitCooperationRequest(
        Guid id, [FromBody] SubmitCooperationRequestDto request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var userId = ResolveUserId();
        if (userId is null) return Forbid();

        var (agreement, error, isDuplicate) = await _agreements.SubmitRequestAsync(
            tenantId.Value, id, request.Message, userId.Value, ct);

        if (error == SupplierAgreementService.SupplierNotFoundError)
            return NotFound(new { error });
        if (error is not null)
            return isDuplicate ? Conflict(new { error }) : BadRequest(new { error });

        return StatusCode(StatusCodes.Status201Created, agreement);
    }

    /// <summary>All cooperation agreements of the calling tenant (client side), newest first.</summary>
    [HttpGet("cooperation")]
    [ProducesResponseType(typeof(IReadOnlyList<CooperationAgreementDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyAgreements(CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        return Ok(await _agreements.ListForClientAsync(tenantId.Value, ct));
    }

    /// <summary>Downloads the contract PDF. Allowed for either party of the agreement.</summary>
    [HttpGet("cooperation/{id:guid}/contract")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadContract(Guid id, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (content, fileName, error) = await _agreements.DownloadContractAsync(id, tenantId.Value, ct);

        if (error == SupplierAgreementService.AgreementNotFoundError)
            return NotFound(new { error });
        if (error is not null)
            return BadRequest(new { error });

        return File(content!, "application/pdf", fileName);
    }

    // ── Marketplace orders ─────────────────────────────────────────────────────

    /// <summary>Places an order with a supplier. 403 without an ACTIVE cooperation agreement.</summary>
    [HttpPost("suppliers/{id:guid}/orders")]
    [ProducesResponseType(typeof(MarketplaceOrderDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateOrder(
        Guid id, [FromBody] CreateMarketplaceOrderDto request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var userId = ResolveUserId();
        if (userId is null) return Forbid();

        var (order, error, isGateViolation) = await _orders.CreateOrderAsync(
            tenantId.Value, id, request, userId.Value, ct);

        if (isGateViolation)
            return StatusCode(StatusCodes.Status403Forbidden, new { error });
        if (error == MarketplaceOrderService.SupplierNotFoundError)
            return NotFound(new { error });
        if (error is not null)
            return BadRequest(new { error });

        return StatusCode(StatusCodes.Status201Created, order);
    }

    /// <summary>All marketplace orders of the calling tenant (client side), newest first.</summary>
    [HttpGet("my-orders")]
    [ProducesResponseType(typeof(IReadOnlyList<MarketplaceOrderDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyOrders(CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        return Ok(await _orders.ListForClientAsync(tenantId.Value, ct));
    }

    /// <summary>Cancels an own order. Only orders still in status "new" can be cancelled by the client.</summary>
    [HttpPost("orders/{id:guid}/cancel")]
    [ProducesResponseType(typeof(MarketplaceOrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelOrder(
        Guid id, [FromBody] CancelMarketplaceOrderDto request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (order, error) = await _orders.CancelOrderAsync(tenantId.Value, id, request.Reason, ct);

        if (error == MarketplaceOrderService.OrderNotFoundError)
            return NotFound(new { error });
        if (error is not null)
            return BadRequest(new { error });

        return Ok(order);
    }

    // ── Supplier support tickets ───────────────────────────────────────────────

    /// <summary>Opens a support ticket to a supplier. No cooperation agreement required.</summary>
    [HttpPost("suppliers/{id:guid}/support-tickets")]
    [ProducesResponseType(typeof(SupplierSupportTicketDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateSupportTicket(
        Guid id, [FromBody] CreateSupportTicketDto request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var userId = ResolveUserId();
        if (userId is null) return Forbid();

        var (ticket, error) = await _support.CreateTicketAsync(
            tenantId.Value, id, request, userId.Value, ct);

        if (error == SupplierSupportService.SupplierNotFoundError)
            return NotFound(new { error });
        if (error is not null)
            return BadRequest(new { error });

        return StatusCode(StatusCodes.Status201Created, ticket);
    }

    /// <summary>All support tickets opened by the calling tenant, newest first (no messages).</summary>
    [HttpGet("my-support-tickets")]
    [ProducesResponseType(typeof(IReadOnlyList<SupplierSupportTicketDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMySupportTickets(CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        return Ok(await _support.ListForClientAsync(tenantId.Value, ct));
    }

    /// <summary>A single ticket with its messages (oldest first). Caller must be a party.</summary>
    [HttpGet("support-tickets/{id:guid}")]
    [ProducesResponseType(typeof(SupplierSupportTicketDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSupportTicket(Guid id, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (ticket, error) = await _support.GetTicketAsync(id, tenantId.Value, ct);
        return error is not null ? NotFound(new { error }) : Ok(ticket);
    }

    /// <summary>Adds a message to a ticket. Caller must be a party; bumps the ticket's UpdatedAt.</summary>
    [HttpPost("support-tickets/{id:guid}/messages")]
    [ProducesResponseType(typeof(SupportTicketMessageDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddSupportTicketMessage(
        Guid id, [FromBody] AddSupportTicketMessageDto request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var userId = ResolveUserId();
        if (userId is null) return Forbid();

        var (message, error) = await _support.AddMessageAsync(
            id, tenantId.Value, userId.Value, request.Body, ct);

        if (error == SupplierSupportService.TicketNotFoundError)
            return NotFound(new { error });
        if (error is not null)
            return BadRequest(new { error });

        return StatusCode(StatusCodes.Status201Created, message);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private Guid? ResolveTenantId()
    {
        var raw = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }

    private Guid? ResolveUserId()
    {
        var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }
}
