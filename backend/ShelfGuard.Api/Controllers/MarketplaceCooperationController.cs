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
    private readonly IMarketplaceOrderReceiptService _receiving;

    public MarketplaceCooperationController(
        ISupplierAgreementService agreements,
        IMarketplaceOrderService orders,
        ISupplierSupportService support,
        IMarketplaceOrderReceiptService receiving)
    {
        _agreements = agreements;
        _orders     = orders;
        _support    = support;
        _receiving  = receiving;
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
            tenantId.Value, id, request.Message, userId.Value, request.ClientLegalEntityId, ct);

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

    /// <summary>Client chooses how to sign the contract: "physical" (mail to supplier's address) or "vchasno" (emails the doc for e-signature).</summary>
    [HttpPost("cooperation/{id:guid}/signing-method")]
    [ProducesResponseType(typeof(CooperationAgreementDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChooseSigningMethod(
        Guid id, [FromBody] ChooseSigningMethodDto request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (agreement, error) = await _agreements.ChooseSigningMethodAsync(
            tenantId.Value, id, request.Method, request.Email, ct);

        if (error == SupplierAgreementService.AgreementNotFoundError)
            return NotFound(new { error });
        if (error is not null)
            return BadRequest(new { error });

        return Ok(agreement);
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

    /// <summary>
    /// Read-only pre-flight (TASK-597): checks whether any of the given supplier items collide
    /// (by barcode) with an Item already in the calling tenant's own catalog, before the order is
    /// actually submitted. Empty result means every line is safe to submit with CatalogAction
    /// left null/"auto". Same auth posture as CreateOrder (class-level module gate only) — this
    /// endpoint creates nothing, it only reads.
    /// </summary>
    [HttpPost("suppliers/{id:guid}/orders/conflicts")]
    [ProducesResponseType(typeof(IReadOnlyList<MarketplaceOrderConflictDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CheckOrderConflicts(
        Guid id, [FromBody] CheckMarketplaceOrderConflictsDto request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (conflicts, error, isGateViolation) = await _orders.CheckCatalogConflictsAsync(
            tenantId.Value, id, request.Items, ct);

        if (isGateViolation)
            return StatusCode(StatusCodes.Status403Forbidden, new { error });
        if (error == MarketplaceOrderService.SupplierNotFoundError)
            return NotFound(new { error });
        if (error is not null)
            return BadRequest(new { error });

        return Ok(conflicts);
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

    /// <summary>Cancels an own order. The client may cancel any time before it ships — status "new" or "confirmed" (TASK-693).</summary>
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

    // ── Marketplace order receiving (TASK-586, ADR-033) ───────────────────────
    // Client-confirmed receipt of a shipped order — replaces the supplier's one-click Deliver.
    // Route addressing is order-centric throughout (orderId, never a separately surfaced
    // receiptId) — the receipt is 1:1 with its order, so callers never need a second id.

    /// <summary>Shipped orders of the calling tenant that still need to be received.</summary>
    [HttpGet("orders/awaiting-receipt")]
    [ProducesResponseType(typeof(IReadOnlyList<MarketplaceOrderDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOrdersAwaitingReceipt(CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        return Ok(await _receiving.ListAwaitingReceiptAsync(tenantId.Value, ct));
    }

    /// <summary>
    /// Idempotent create-or-get: starts a receiving session for a shipped order, or returns the
    /// one already in progress. 400 if the order isn't shipped yet or has no destination store.
    /// </summary>
    [HttpPost("orders/{orderId:guid}/receipt")]
    [Authorize(Policy = AppPolicies.CanReceiveStock)]
    [ProducesResponseType(typeof(MarketplaceOrderReceiptDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateOrGetReceipt(Guid orderId, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var userId = ResolveUserId();
        if (userId is null) return Forbid();

        var (receipt, error) = await _receiving.GetOrCreateDraftAsync(tenantId.Value, orderId, userId.Value, ct);

        if (error == MarketplaceOrderReceiptService.OrderNotFoundError)
            return NotFound(new { error });
        if (error is not null)
            return BadRequest(new { error });

        return Ok(receipt);
    }

    /// <summary>Reads the receiving session for an order, read-only. 404 if none started yet.</summary>
    [HttpGet("orders/{orderId:guid}/receipt")]
    [ProducesResponseType(typeof(MarketplaceOrderReceiptDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReceipt(Guid orderId, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (receipt, error) = await _receiving.GetAsync(tenantId.Value, orderId, ct);
        return error is not null ? NotFound(new { error }) : Ok(receipt);
    }

    /// <summary>
    /// Records a scan/count for one item of the receiving session: ProductId (resolved from a
    /// barcode by the caller beforehand, via GET /api/items/by-barcode), QuantityReceived,
    /// ExpiryDate, BatchNumber, DiscrepancyNotes. One physical item per call (not bulk).
    /// </summary>
    [HttpPut("orders/{orderId:guid}/receipt/items/{itemId:guid}")]
    [Authorize(Policy = AppPolicies.CanReceiveStock)]
    [ProducesResponseType(typeof(MarketplaceOrderReceiptDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateReceiptItem(
        Guid orderId, Guid itemId, [FromBody] UpdateMarketplaceOrderReceiptItemRequest request,
        CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (receipt, error) = await _receiving.UpdateItemAsync(tenantId.Value, orderId, itemId, request, ct);

        if (error is MarketplaceOrderReceiptService.ReceiptNotFoundError
            or MarketplaceOrderReceiptService.ReceiptItemNotFoundError)
            return NotFound(new { error });
        if (error is not null)
            return BadRequest(new { error });

        return Ok(receipt);
    }

    /// <summary>
    /// Finalizes the receiving session: every item must have been scanned (ProductId) with a
    /// quantity and expiry date. Creates ProductStock/StockMovement per item and sets the order
    /// to Delivered — the only remaining path to that status (ADR-033 Decision 4).
    /// </summary>
    [HttpPost("orders/{orderId:guid}/receipt/finalize")]
    [Authorize(Policy = AppPolicies.CanReceiveStock)]
    [ProducesResponseType(typeof(MarketplaceOrderReceiptDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> FinalizeReceipt(Guid orderId, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var userId = ResolveUserId();
        if (userId is null) return Forbid();

        var (receipt, error) = await _receiving.ReceiveAsync(tenantId.Value, orderId, userId.Value, ct);

        if (error is MarketplaceOrderReceiptService.OrderNotFoundError
            or MarketplaceOrderReceiptService.ReceiptNotFoundError)
            return NotFound(new { error });
        if (error is not null)
            return BadRequest(new { error });

        return Ok(receipt);
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
