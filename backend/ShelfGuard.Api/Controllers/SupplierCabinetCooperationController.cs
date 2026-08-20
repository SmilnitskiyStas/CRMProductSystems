using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Infrastructure.Authorization;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Supplier side of the cooperation flow (TASK-317): incoming cooperation
/// requests (approve → contract PDF → Вчасно / mark-signed → active →
/// terminate), contract requisites settings with signature/stamp uploads,
/// marketplace order processing, and support tickets. Same auth model as
/// SupplierCabinetController — every operation is scoped to the calling
/// supplier tenant, ids are never trusted across tenants.
/// </summary>
[ApiController]
[Route("api/supplier-cabinet")]
[Authorize(Policy = AppPolicies.SupplierCabinet)]
[RequireModule("marketplace_supplier")]
public sealed class SupplierCabinetCooperationController : ControllerBase
{
    private const long MaxImageSizeBytes = 2 * 1024 * 1024;

    private readonly ISupplierAgreementService _agreements;
    private readonly IMarketplaceOrderService _orders;
    private readonly ISupplierSupportService _support;

    public SupplierCabinetCooperationController(
        ISupplierAgreementService agreements,
        IMarketplaceOrderService orders,
        ISupplierSupportService support)
    {
        _agreements = agreements;
        _orders     = orders;
        _support    = support;
    }

    // ── Cooperation requests / agreements ─────────────────────────────────────

    /// <summary>Incoming cooperation requests/agreements, optionally filtered by status, newest first.</summary>
    [HttpGet("cooperation-requests")]
    [ProducesResponseType(typeof(IReadOnlyList<CooperationAgreementDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCooperationRequests(
        [FromQuery] string? status, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        return Ok(await _agreements.ListForSupplierAsync(tenantId.Value, status, ct));
    }

    /// <summary>Approves a pending request: generates the contract PDF and moves to awaiting_signature.</summary>
    [HttpPost("cooperation-requests/{id:guid}/approve")]
    [ProducesResponseType(typeof(CooperationAgreementDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (agreement, error) = await _agreements.ApproveAsync(tenantId.Value, id, ct);
        return MapAgreementResult(agreement, error);
    }

    /// <summary>Rejects a pending request with a reason.</summary>
    [HttpPost("cooperation-requests/{id:guid}/reject")]
    [ProducesResponseType(typeof(CooperationAgreementDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reject(
        Guid id, [FromBody] RejectCooperationRequestDto request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (agreement, error) = await _agreements.RejectAsync(tenantId.Value, id, request.Reason, ct);
        return MapAgreementResult(agreement, error);
    }

    /// <summary>Re-renders the contract PDF (same number). Only while awaiting_signature — never after signing.</summary>
    [HttpPost("cooperation-requests/{id:guid}/regenerate-contract")]
    [ProducesResponseType(typeof(CooperationAgreementDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RegenerateContract(Guid id, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (agreement, error) = await _agreements.RegenerateContractAsync(tenantId.Value, id, ct);
        return MapAgreementResult(agreement, error);
    }

    /// <summary>Sends the generated contract to Вчасно for e-signature (per-tenant API key).</summary>
    [HttpPost("cooperation-requests/{id:guid}/send-to-vchasno")]
    [ProducesResponseType(typeof(CooperationAgreementDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SendToVchasno(Guid id, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (agreement, error) = await _agreements.SendToVchasnoAsync(tenantId.Value, id, ct);
        return MapAgreementResult(agreement, error);
    }

    /// <summary>Confirms the contract is signed (via Вчасно or physically) → status active.</summary>
    [HttpPost("cooperation-requests/{id:guid}/mark-signed")]
    [ProducesResponseType(typeof(CooperationAgreementDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkSigned(Guid id, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (agreement, error) = await _agreements.MarkSignedAsync(tenantId.Value, id, ct);
        return MapAgreementResult(agreement, error);
    }

    /// <summary>Terminates an active agreement (optional reason).</summary>
    [HttpPost("cooperation-requests/{id:guid}/terminate")]
    [ProducesResponseType(typeof(CooperationAgreementDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Terminate(
        Guid id, [FromBody] TerminateAgreementDto? request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (agreement, error) = await _agreements.TerminateAsync(
            tenantId.Value, id, request?.Reason, ct);
        return MapAgreementResult(agreement, error);
    }

    /// <summary>Downloads the contract PDF of an own agreement.</summary>
    [HttpGet("cooperation-requests/{id:guid}/contract")]
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

    // ── Contract settings (requisites) ─────────────────────────────────────────

    /// <summary>Own contract requisites. 404 until first saved.</summary>
    [HttpGet("contract-settings")]
    [ProducesResponseType(typeof(SupplierContractSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetContractSettings(CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var settings = await _agreements.GetContractSettingsAsync(tenantId.Value, ct);
        return settings is null
            ? NotFound(new { error = "Реквізити договору ще не заповнено." })
            : Ok(settings);
    }

    /// <summary>Creates/replaces the own contract requisites (images are uploaded separately).</summary>
    [HttpPut("contract-settings")]
    [ProducesResponseType(typeof(SupplierContractSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpsertContractSettings(
        [FromBody] UpsertContractSettingsDto request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (settings, error) = await _agreements.UpsertContractSettingsAsync(tenantId.Value, request, ct);
        return error is not null ? BadRequest(new { error }) : Ok(settings);
    }

    /// <summary>Uploads the director's signature image (PNG/JPG, max 2 MB).</summary>
    [HttpPost("contract-settings/signature-image")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public Task<IActionResult> UploadSignatureImage(IFormFile file, CancellationToken ct) =>
        UploadContractAsset(file, ContractAssetKind.Signature, ct);

    /// <summary>Uploads the company stamp («мокра печатка») image (PNG/JPG, max 2 MB).</summary>
    [HttpPost("contract-settings/stamp-image")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public Task<IActionResult> UploadStampImage(IFormFile file, CancellationToken ct) =>
        UploadContractAsset(file, ContractAssetKind.Stamp, ct);

    // ── Orders ────────────────────────────────────────────────────────────────

    /// <summary>Incoming marketplace orders of the own supplier, newest first.</summary>
    [HttpGet("orders")]
    [ProducesResponseType(typeof(IReadOnlyList<MarketplaceOrderDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOrders(CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        return Ok(await _orders.ListForSupplierAsync(tenantId.Value, ct));
    }

    /// <summary>
    /// Changes an order status. Allowed: new → confirmed | cancelled;
    /// confirmed → shipped | cancelled; shipped → delivered. Cancelling requires Reason.
    /// </summary>
    [HttpPost("orders/{id:guid}/status")]
    [ProducesResponseType(typeof(MarketplaceOrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateOrderStatus(
        Guid id, [FromBody] UpdateMarketplaceOrderStatusDto request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (order, error) = await _orders.UpdateOrderStatusAsync(tenantId.Value, id, request, ct);

        if (error == MarketplaceOrderService.OrderNotFoundError)
            return NotFound(new { error });
        if (error is not null)
            return BadRequest(new { error });

        return Ok(order);
    }

    /// <summary>Records why a shipped order's delivery is running late. Only allowed while status = shipped.</summary>
    [HttpPost("orders/{id:guid}/delay-reason")]
    [ProducesResponseType(typeof(MarketplaceOrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetDelayReason(
        Guid id, [FromBody] SetOrderDelayReasonDto request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (order, error) = await _orders.SetDelayReasonAsync(tenantId.Value, id, request.Reason, ct);

        if (error == MarketplaceOrderService.OrderNotFoundError)
            return NotFound(new { error });
        if (error is not null)
            return BadRequest(new { error });

        return Ok(order);
    }

    // ── Support tickets ────────────────────────────────────────────────────────

    /// <summary>Support tickets addressed to the own supplier, newest first (no messages).</summary>
    [HttpGet("support-tickets")]
    [ProducesResponseType(typeof(IReadOnlyList<SupplierSupportTicketDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSupportTickets(CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        return Ok(await _support.ListForSupplierAsync(tenantId.Value, ct));
    }

    /// <summary>A single ticket with its messages (oldest first).</summary>
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

    /// <summary>Replies on a ticket; bumps the ticket's UpdatedAt.</summary>
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

    /// <summary>Changes a ticket status (open | in_progress | resolved | closed).</summary>
    [HttpPost("support-tickets/{id:guid}/status")]
    [ProducesResponseType(typeof(SupplierSupportTicketDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateSupportTicketStatus(
        Guid id, [FromBody] UpdateSupportTicketStatusDto request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (ticket, error) = await _support.UpdateStatusAsync(tenantId.Value, id, request.Status, ct);

        if (error == SupplierSupportService.TicketNotFoundError)
            return NotFound(new { error });
        if (error is not null)
            return BadRequest(new { error });

        return Ok(ticket);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<IActionResult> UploadContractAsset(
        IFormFile file, ContractAssetKind kind, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Файл не завантажено." });

        if (file.Length > MaxImageSizeBytes)
            return BadRequest(new { error = "Файл завеликий. Максимум 2 МБ." });

        using var stream = file.OpenReadStream();
        var (url, error) = await _agreements.UploadContractAssetAsync(
            tenantId.Value, stream, file.FileName, kind, ct);

        if (error is not null)
            return BadRequest(new { error });

        return Ok(new { imageUrl = url });
    }

    private IActionResult MapAgreementResult(CooperationAgreementDto? agreement, string? error)
    {
        if (error == SupplierAgreementService.AgreementNotFoundError)
            return NotFound(new { error });
        if (error is not null)
            return BadRequest(new { error });

        return Ok(agreement);
    }

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
