using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Chat;
using ShelfGuard.Application.Features.Chat.Dtos;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Provider cross-tenant Live Chat admin panel.
/// All endpoints require ProviderOnly policy — no tenant scoping.
/// </summary>
[ApiController]
[Route("api/admin/chat")]
[Authorize(Policy = AppPolicies.ProviderTeamMember)]
public sealed class AdminChatController : ControllerBase
{
    private readonly IChatService _chat;

    public AdminChatController(IChatService chat) => _chat = chat;

    // ── GET /api/admin/chat/sessions ──────────────────────────────────────────

    [HttpGet("sessions")]
    [ProducesResponseType(typeof(IReadOnlyList<ChatSessionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllSessions(CancellationToken ct)
    {
        var sessions = await _chat.GetAllSessionsForProviderAsync(ct);
        return Ok(sessions);
    }

    // ── GET /api/admin/chat/sessions/{id}/messages ────────────────────────────

    [HttpGet("sessions/{id:guid}/messages")]
    [ProducesResponseType(typeof(IReadOnlyList<ChatMessageDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMessages(Guid id, CancellationToken ct)
    {
        var messages = await _chat.GetMessagesForProviderAsync(id, ct);
        return Ok(messages);
    }

    // ── POST /api/admin/chat/sessions/{id}/messages ───────────────────────────

    [HttpPost("sessions/{id:guid}/messages")]
    [ProducesResponseType(typeof(ChatMessageDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SendMessage(
        Guid id,
        [FromBody] SendChatMessageRequest req,
        CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return Forbid();

        var userName = GetUserName();

        var message = await _chat.ProviderSendMessageAsync(id, userId.Value, userName, req, ct);
        return StatusCode(StatusCodes.Status201Created, message);
    }

    // ── POST /api/admin/chat/sessions/{id}/close ──────────────────────────────

    [HttpPost("sessions/{id:guid}/close")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> CloseSession(Guid id, CancellationToken ct)
    {
        await _chat.ProviderCloseSessionAsync(id, ct);
        return NoContent();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Guid? GetUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? User.FindFirstValue("sub");
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    private string GetUserName() =>
        User.FindFirstValue("full_name")
        ?? User.FindFirstValue(ClaimTypes.Name)
        ?? "Provider";
}
