using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Authorization;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Client side of the supplier↔client chat (TASK-313, calm-singing-marble Частина 2).
/// Any authenticated tenant can message a supplier from its public marketplace
/// page — regular tenant authorization (no supplier-cabinet policy), gated by the
/// standard "marketplace" module like the rest of MarketplaceController.
/// </summary>
[ApiController]
[Route("api/marketplace")]
[Authorize]
[RequireModule("marketplace")]
public sealed class MarketplaceChatController : ControllerBase
{
    private readonly ISupplierChatService _chat;
    private readonly IMarketplaceRepository _marketplaceRepo;

    public MarketplaceChatController(ISupplierChatService chat, IMarketplaceRepository marketplaceRepo)
    {
        _chat            = chat;
        _marketplaceRepo = marketplaceRepo;
    }

    /// <summary>Messages of the thread with a supplier (creates the thread on first access).</summary>
    [HttpGet("suppliers/{supplierId:guid}/chat/messages")]
    [ProducesResponseType(typeof(IReadOnlyList<SupplierChatMessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMessages(Guid supplierId, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var callerUserId = ResolveUserId();
        if (callerUserId is null) return Forbid();

        var supplierTenantId = await _marketplaceRepo.GetSupplierTenantIdAsync(supplierId, ct);
        if (supplierTenantId is null) return NotFound(new { error = "Supplier not found." });

        var session = await _chat.GetOrCreateSessionAsync(
            tenantId.Value, supplierTenantId.Value, isSupplierSide: false, callerUserId.Value, ct);

        var (messages, error) = await _chat.GetMessagesAsync(session.Id, tenantId.Value, ct);
        return error is not null ? NotFound(new { error }) : Ok(messages);
    }

    /// <summary>Sends a message to a supplier (creates the thread on first access).</summary>
    [HttpPost("suppliers/{supplierId:guid}/chat/messages")]
    [ProducesResponseType(typeof(SupplierChatMessageDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SendMessage(
        Guid supplierId, [FromBody] SendSupplierChatMessageRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var callerUserId = ResolveUserId();
        if (callerUserId is null) return Forbid();

        var supplierTenantId = await _marketplaceRepo.GetSupplierTenantIdAsync(supplierId, ct);
        if (supplierTenantId is null) return NotFound(new { error = "Supplier not found." });

        var senderName = User.FindFirst("full_name")?.Value
                       ?? User.FindFirst(ClaimTypes.Name)?.Value
                       ?? User.FindFirst(ClaimTypes.Email)?.Value
                       ?? "Unknown";

        var session = await _chat.GetOrCreateSessionAsync(
            tenantId.Value, supplierTenantId.Value, isSupplierSide: false, callerUserId.Value, ct);

        var (message, error) = await _chat.SendMessageAsync(
            session.Id, tenantId.Value, callerUserId.Value, senderName, request.Body, ct);

        if (error is not null) return BadRequest(new { error });
        return StatusCode(StatusCodes.Status201Created, message);
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
