using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Chat;
using ShelfGuard.Application.Features.Chat.Dtos;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Tenant-scoped Live Chat.
/// All endpoints require a valid JWT; tenantId resolved from JWT claim.
/// </summary>
[ApiController]
[Route("api/chat")]
[Authorize]
public sealed class ChatController : ControllerBase
{
    private readonly IChatService _chat;

    public ChatController(IChatService chat) => _chat = chat;

    // ── POST /api/chat/sessions ───────────────────────────────────────────────

    [HttpPost("sessions")]
    [ProducesResponseType(typeof(ChatSessionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateSession(
        [FromBody] CreateChatSessionRequest req,
        CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var userId = GetUserId();
        if (userId is null) return Forbid();

        var userName = GetUserName();

        var session = await _chat.CreateSessionAsync(tenantId.Value, userId.Value, userName, req, ct);
        return StatusCode(StatusCodes.Status201Created, session);
    }

    // ── GET /api/chat/sessions ────────────────────────────────────────────────

    [HttpGet("sessions")]
    [ProducesResponseType(typeof(IReadOnlyList<ChatSessionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSessions(CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var sessions = await _chat.GetSessionsAsync(tenantId.Value, ct);
        return Ok(sessions);
    }

    // ── GET /api/chat/sessions/{id}/messages ─────────────────────────────────

    [HttpGet("sessions/{id:guid}/messages")]
    [ProducesResponseType(typeof(IReadOnlyList<ChatMessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMessages(Guid id, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var messages = await _chat.GetMessagesAsync(id, tenantId.Value, ct);
        return Ok(messages);
    }

    // ── POST /api/chat/sessions/{id}/messages ─────────────────────────────────

    [HttpPost("sessions/{id:guid}/messages")]
    [ProducesResponseType(typeof(ChatMessageDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SendMessage(
        Guid id,
        [FromBody] SendChatMessageRequest req,
        CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var userId = GetUserId();
        if (userId is null) return Forbid();

        var userName = GetUserName();

        var message = await _chat.SendMessageAsync(id, tenantId.Value, userId.Value, userName, req, ct);
        return StatusCode(StatusCodes.Status201Created, message);
    }

    // ── POST /api/chat/sessions/{id}/close ────────────────────────────────────

    [HttpPost("sessions/{id:guid}/close")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CloseSession(Guid id, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        await _chat.CloseSessionAsync(id, tenantId.Value, ct);
        return NoContent();
    }

    // ── POST /api/chat/sessions/{id}/rating ──────────────────────────────────

    [HttpPost("sessions/{id:guid}/rating")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SubmitRating(
        Guid id,
        [FromBody] SubmitRatingRequest req,
        CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var error = await _chat.SubmitRatingAsync(id, tenantId.Value, req.Rating, req.Comment, ct);
        return error is null ? NoContent() : BadRequest(new { error });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Guid? GetTenantId()
    {
        var claim = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    private Guid? GetUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? User.FindFirstValue("sub");
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    private string GetUserName() =>
        User.FindFirstValue(ClaimTypes.Name)
        ?? User.FindFirstValue("name")
        ?? "Unknown";
}
