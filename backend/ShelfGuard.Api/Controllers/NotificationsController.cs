using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Notifications;
using ShelfGuard.Application.Features.Notifications.Dtos;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public sealed class NotificationsController : ControllerBase
{
    private readonly INotificationService _notifications;

    public NotificationsController(INotificationService notifications)
        => _notifications = notifications;

    /// <summary>Returns all notification settings for the current user.</summary>
    [HttpGet("settings")]
    [ProducesResponseType(typeof(IReadOnlyList<NotificationSettingDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
    {
        var userId = ResolveUserId();
        if (userId is null) return Unauthorized();

        var result = await _notifications.GetSettingsAsync(userId.Value, ct);
        return Ok(result);
    }

    /// <summary>Upsert a single notification setting (event + channel + isEnabled).</summary>
    [HttpPut("settings")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpsertSetting(
        [FromBody] UpsertNotificationSettingRequest request,
        CancellationToken ct)
    {
        var userId = ResolveUserId();
        if (userId is null) return Unauthorized();

        try
        {
            await _notifications.UpsertSettingAsync(userId.Value, request, ct);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Returns notification history for the current tenant (last 100 items).</summary>
    [HttpGet("history")]
    [ProducesResponseType(typeof(IReadOnlyList<NotificationHistoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistory(CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var result = await _notifications.GetHistoryAsync(tenantId.Value, ct);
        return Ok(result);
    }

    /// <summary>Returns a single notification by id.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(NotificationHistoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var result = await _notifications.GetByIdAsync(id, tenantId.Value, ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>Marks one notification as read.</summary>
    [HttpPost("{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAsRead(Guid id, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        await _notifications.MarkAsReadAsync(id, tenantId.Value, ct);
        return NoContent();
    }

    /// <summary>Marks one notification as unread.</summary>
    [HttpPost("{id:guid}/unread")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAsUnread(Guid id, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        await _notifications.MarkAsUnreadAsync(id, tenantId.Value, ct);
        return NoContent();
    }

    /// <summary>Marks all notifications as read for the current tenant.</summary>
    [HttpPost("read-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAllAsRead(CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        await _notifications.MarkAllAsReadAsync(tenantId.Value, ct);
        return NoContent();
    }

    /// <summary>Returns unread notification count for the TopBar badge.</summary>
    [HttpGet("unread-count")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUnreadCount(CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Ok(new { count = 0 });

        var count = await _notifications.GetUnreadCountAsync(tenantId.Value, ct);
        return Ok(new { count });
    }

    /// <summary>Enqueues a test notification for the current user.</summary>
    [HttpPost("test")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SendTest(
        [FromBody] TestNotificationRequest request,
        CancellationToken ct)
    {
        var userId   = ResolveUserId();
        var tenantId = ResolveTenantId();
        if (userId is null) return Unauthorized();
        if (tenantId is null) return Forbid();

        try
        {
            await _notifications.SendTestAsync(tenantId.Value, userId.Value, request, ct);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private Guid? ResolveUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    private Guid? ResolveTenantId()
    {
        var raw = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }
}
