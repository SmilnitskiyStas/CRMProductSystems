using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Application.Features.Users.Dtos;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Infrastructure.Authorization;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Self-service supplier cabinet (v4.1, ADR-016, TASK-284).
/// Available only to supplier_admin users of supplier tenants with the
/// marketplace_supplier module. Every operation is scoped to the calling
/// tenant's owner-managed supplier — no supplier id is ever accepted from
/// the client.
/// </summary>
[ApiController]
[Route("api/supplier-cabinet")]
[Authorize(Policy = AppPolicies.SupplierCabinet)]
[RequireModule("marketplace_supplier")]
public sealed class SupplierCabinetController : ControllerBase
{
    private readonly ISupplierCabinetService _cabinet;
    private readonly ISupplierRolesService _roles;
    private readonly ISupplierTaskService _tasks;
    private readonly ISupplierChatService _chat;

    public SupplierCabinetController(
        ISupplierCabinetService cabinet, ISupplierRolesService roles, ISupplierTaskService tasks,
        ISupplierChatService chat)
    {
        _cabinet = cabinet;
        _roles   = roles;
        _tasks   = tasks;
        _chat    = chat;
    }

    // ── Profile ───────────────────────────────────────────────────────────────

    /// <summary>Own supplier profile with metrics.</summary>
    [HttpGet("profile")]
    [ProducesResponseType(typeof(SupplierProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProfile(CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        // Intentionally ungated (TASK-585, mirrors TASK-359): any supplier_admin can view their own profile regardless of assigned permissions.

        var (profile, error) = await _cabinet.GetProfileAsync(tenantId.Value, ct);
        return error is not null ? NotFound(new { error }) : Ok(profile);
    }

    /// <summary>Patch-updates the own profile (region, categories, website, delivery coverage, working hours, payment terms).</summary>
    [HttpPut("profile")]
    [ProducesResponseType(typeof(SupplierProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] CabinetProfileUpdateDto request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.ProfileManagement)) return Forbid();

        var (profile, error) = await _cabinet.UpdateProfileAsync(tenantId.Value, request, ct);
        if (error is null) return Ok(profile);

        // "cabinet not available" is the only resource-missing error here (TASK-650); a
        // delivery-coverage validation failure is a 400, mirroring SupplierProfileSettingsController.
        return error.Contains("not available", StringComparison.OrdinalIgnoreCase)
            ? NotFound(new { error })
            : BadRequest(new { error });
    }

    /// <summary>Toggles marketplace visibility (IsPublic) of the own profile.</summary>
    [HttpPost("profile/publish")]
    [ProducesResponseType(typeof(SupplierProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TogglePublish(CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.ProfileManagement)) return Forbid();

        var (profile, error) = await _cabinet.TogglePublishAsync(tenantId.Value, ct);
        return error is not null ? NotFound(new { error }) : Ok(profile);
    }

    // ── Items ─────────────────────────────────────────────────────────────────

    /// <summary>Own item catalog including unavailable items.</summary>
    [HttpGet("items")]
    [ProducesResponseType(typeof(IReadOnlyList<SupplierItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetItems(CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.CatalogManagement)) return Forbid();

        var (items, error) = await _cabinet.GetItemsAsync(tenantId.Value, ct);
        return error is not null ? NotFound(new { error }) : Ok(items);
    }

    /// <summary>Adds an item to the own catalog.</summary>
    [HttpPost("items")]
    [ProducesResponseType(typeof(SupplierItemDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddItem(
        [FromBody] AdminAddSupplierItemDto request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.CatalogManagement)) return Forbid();

        var (item, error) = await _cabinet.AddItemAsync(tenantId.Value, request, ct);

        if (error == SupplierCabinetService.CabinetNotAvailableError)
            return NotFound(new { error });
        if (error is not null)
            return BadRequest(new { error });

        return StatusCode(StatusCodes.Status201Created, item);
    }

    /// <summary>Patch-updates an item of the own catalog.</summary>
    [HttpPut("items/{id:guid}")]
    [ProducesResponseType(typeof(SupplierItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateItem(
        Guid id, [FromBody] AdminUpdateSupplierItemDto request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.CatalogManagement)) return Forbid();

        var (item, error) = await _cabinet.UpdateItemAsync(tenantId.Value, id, request, ct);

        if (error == SupplierCabinetService.CabinetNotAvailableError || error == "Item not found.")
            return NotFound(new { error });
        if (error is not null)
            return BadRequest(new { error });

        return Ok(item);
    }

    /// <summary>Removes an item from the own catalog.</summary>
    [HttpDelete("items/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteItem(Guid id, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.CatalogManagement)) return Forbid();

        var error = await _cabinet.DeleteItemAsync(tenantId.Value, id, ct);
        return error is not null ? NotFound(new { error }) : NoContent();
    }

    // ── Reviews / metrics (read-only) ─────────────────────────────────────────

    /// <summary>Reviews left for the own supplier (read-only, paginated).</summary>
    [HttpGet("reviews")]
    [ProducesResponseType(typeof(PagedResult<PublicSupplierReviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReviews(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.ClientReviews)) return Forbid();

        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var (reviews, error) = await _cabinet.GetReviewsAsync(tenantId.Value, page, pageSize, ct);
        return error is not null ? NotFound(new { error }) : Ok(reviews);
    }

    /// <summary>Aggregated metrics of the own supplier (read-only).</summary>
    [HttpGet("metrics")]
    [ProducesResponseType(typeof(SupplierMetricsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMetrics(CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.ClientReviews)) return Forbid();

        var (metrics, error) = await _cabinet.GetMetricsAsync(tenantId.Value, ct);
        return error is not null ? NotFound(new { error }) : Ok(metrics);
    }

    /// <summary>
    /// TASK-689 (Phase 6c, request #9): the own supplier's daily metric-snapshot history plus
    /// period-over-period deltas (read-only). <paramref name="days"/> clamps to [7, 365], default 90.
    /// </summary>
    [HttpGet("metrics-history")]
    [ProducesResponseType(typeof(SupplierMetricsHistoryResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMetricsHistory([FromQuery] int days = 90, CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.ClientReviews)) return Forbid();

        var result = await _cabinet.GetMetricsHistoryAsync(tenantId.Value, days, ct);
        return result is null
            ? NotFound(new { error = SupplierCabinetService.CabinetNotAvailableError })
            : Ok(result);
    }

    /// <summary>Posts/updates the own supplier's reply to a review left for it.</summary>
    [HttpPut("reviews/{id:guid}/reply")]
    [ProducesResponseType(typeof(PublicSupplierReviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReplyToReview(
        Guid id, [FromBody] CabinetReplyToReviewDto request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.ClientReviews)) return Forbid();

        var (review, error) = await _cabinet.ReplyToReviewAsync(tenantId.Value, id, request.ReplyText, ct);

        if (error == SupplierCabinetService.CabinetNotAvailableError
            || error == SupplierCabinetService.ReviewNotFoundError)
            return NotFound(new { error });
        if (error is not null)
            return BadRequest(new { error });

        return Ok(review);
    }

    /// <summary>Positive/neutral/negative breakdown of the own supplier's reviews.</summary>
    [HttpGet("reviews/stats")]
    [ProducesResponseType(typeof(SupplierReviewStatsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReviewStats(CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.ClientReviews)) return Forbid();

        var (stats, error) = await _cabinet.GetReviewStatsAsync(tenantId.Value, ct);
        return error is not null ? NotFound(new { error }) : Ok(stats);
    }

    // ── Staff management (self-service) ─────────────────────────────────────────

    /// <summary>Lists all staff/team members of the caller's own tenant.</summary>
    [HttpGet("staff")]
    [ProducesResponseType(typeof(IReadOnlyList<UserDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStaff(CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.StaffManagement)) return Forbid();

        var staff = await _cabinet.GetStaffAsync(tenantId.Value, ct);
        return Ok(staff);
    }

    /// <summary>Invites a new staff member into the own tenant. Always created as supplier_admin.</summary>
    [HttpPost("staff")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> InviteStaff(
        [FromBody] CabinetInviteStaffDto request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.StaffManagement)) return Forbid();

        var callerUserId = ResolveUserId();
        if (callerUserId is null) return Forbid();

        var inviterName = User.FindFirst("full_name")?.Value
                       ?? User.FindFirst(ClaimTypes.Name)?.Value
                       ?? User.FindFirst(ClaimTypes.Email)?.Value
                       ?? "Unknown";

        var (user, error) = await _cabinet.InviteStaffAsync(tenantId.Value, callerUserId.Value, request, inviterName, ct);
        if (user is null) return BadRequest(new { error });
        return StatusCode(StatusCodes.Status201Created, user);
    }

    /// <summary>Deactivates a staff member of the own tenant (soft delete).</summary>
    [HttpDelete("staff/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateStaff(Guid id, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.StaffManagement)) return Forbid();

        var callerUserId = ResolveUserId();
        if (callerUserId is null) return Forbid();

        var error = await _cabinet.DeactivateStaffAsync(tenantId.Value, callerUserId.Value, id, ct);
        return error is null ? NoContent() : NotFound(new { error });
    }

    // ── Roles (self-service, TASK-306) ───────────────────────────────────────

    /// <summary>Lists all custom staff roles of the caller's own tenant.</summary>
    [HttpGet("roles")]
    [ProducesResponseType(typeof(IReadOnlyList<SupplierRoleDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRoles(CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.StaffManagement)) return Forbid();

        var roles = await _roles.GetAllAsync(tenantId.Value, ct);
        return Ok(roles);
    }

    /// <summary>Creates a new custom staff role for the caller's own tenant.</summary>
    [HttpPost("roles")]
    [ProducesResponseType(typeof(SupplierRoleDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateRole([FromBody] CreateSupplierRoleRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.StaffManagement)) return Forbid();

        var (role, error) = await _roles.CreateAsync(tenantId.Value, request, ct);
        if (error is not null) return BadRequest(new { error });
        return StatusCode(StatusCodes.Status201Created, role);
    }

    /// <summary>Updates a custom staff role of the caller's own tenant.</summary>
    [HttpPut("roles/{id:guid}")]
    [ProducesResponseType(typeof(SupplierRoleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateRole(Guid id, [FromBody] UpdateSupplierRoleRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.StaffManagement)) return Forbid();

        var (role, error) = await _roles.UpdateAsync(tenantId.Value, id, request, ct);
        if (error is not null) return BadRequest(new { error });
        return Ok(role);
    }

    /// <summary>Deletes a custom staff role of the caller's own tenant.</summary>
    [HttpDelete("roles/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteRole(Guid id, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.StaffManagement)) return Forbid();

        var (success, error) = await _roles.DeleteAsync(tenantId.Value, id, ct);
        if (!success) return BadRequest(new { error });
        return NoContent();
    }

    // ── Task board (self-service, TASK-306) ──────────────────────────────────

    /// <summary>Lists tasks of the caller's own tenant, optionally filtered.</summary>
    [HttpGet("tasks")]
    [ProducesResponseType(typeof(IReadOnlyList<SupplierTaskDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetTasks(
        [FromQuery] bool assignedToMe = false,
        [FromQuery] Guid? clientTenantId = null,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.TaskBoard)) return Forbid();

        var callerUserId = ResolveUserId();
        if (callerUserId is null) return Forbid();

        var (tasks, error) = await _tasks.GetAllAsync(
            tenantId.Value, callerUserId.Value, assignedToMe, clientTenantId, status, ct);

        return error is not null ? BadRequest(new { error }) : Ok(tasks);
    }

    /// <summary>Creates a new task on the caller's own tenant's task board.</summary>
    [HttpPost("tasks")]
    [ProducesResponseType(typeof(SupplierTaskDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateTask([FromBody] CreateSupplierTaskRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.TaskBoard)) return Forbid();

        var callerUserId = ResolveUserId();
        if (callerUserId is null) return Forbid();

        var (task, error) = await _tasks.CreateAsync(tenantId.Value, callerUserId.Value, request, ct);

        if (error == "Supplier cabinet is not available for this tenant.")
            return NotFound(new { error });
        if (error is not null)
            return BadRequest(new { error });

        return StatusCode(StatusCodes.Status201Created, task);
    }

    /// <summary>Patch-updates a task of the caller's own tenant.</summary>
    [HttpPut("tasks/{id:guid}")]
    [ProducesResponseType(typeof(SupplierTaskDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateTask(Guid id, [FromBody] UpdateSupplierTaskRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.TaskBoard)) return Forbid();

        var (task, error) = await _tasks.UpdateAsync(tenantId.Value, id, request, ct);

        if (error == "Task not found.")
            return NotFound(new { error });
        if (error is not null)
            return BadRequest(new { error });

        return Ok(task);
    }

    /// <summary>Updates only the status of a task of the caller's own tenant.</summary>
    [HttpPut("tasks/{id:guid}/status")]
    [ProducesResponseType(typeof(SupplierTaskDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateTaskStatus(Guid id, [FromBody] UpdateSupplierTaskStatusRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.TaskBoard)) return Forbid();

        var (task, error) = await _tasks.UpdateStatusAsync(tenantId.Value, id, request, ct);

        if (error == "Task not found.")
            return NotFound(new { error });
        if (error is not null)
            return BadRequest(new { error });

        return Ok(task);
    }

    // ── Clients (self-service, TASK-313) ─────────────────────────────────────

    /// <summary>Union of client tenants that reviewed and/or have tasks linked to the own supplier.</summary>
    [HttpGet("clients")]
    [ProducesResponseType(typeof(IReadOnlyList<SupplierClientDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetClients(CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.ClientManagement)) return Forbid();

        var (clients, error) = await _cabinet.GetClientsAsync(tenantId.Value, ct);
        return error is not null ? NotFound(new { error }) : Ok(clients);
    }

    // ── Chat (self-service, TASK-313) ────────────────────────────────────────

    /// <summary>Lists all chat threads of the own supplier with its clients.</summary>
    [HttpGet("chat/sessions")]
    [ProducesResponseType(typeof(IReadOnlyList<SupplierChatSessionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetChatSessions(CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var sessions = await _chat.GetSessionsAsync(tenantId.Value, isSupplierSide: true, ct);
        return Ok(sessions);
    }

    /// <summary>Messages of the thread with a given client (creates the thread on first access).</summary>
    [HttpGet("chat/sessions/{clientTenantId:guid}/messages")]
    [ProducesResponseType(typeof(IReadOnlyList<SupplierChatMessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetChatMessages(Guid clientTenantId, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var callerUserId = ResolveUserId();
        if (callerUserId is null) return Forbid();

        var session = await _chat.GetOrCreateSessionAsync(
            tenantId.Value, clientTenantId, isSupplierSide: true, callerUserId.Value, ct);

        var (messages, error) = await _chat.GetMessagesAsync(session.Id, tenantId.Value, ct);
        return error is not null ? NotFound(new { error }) : Ok(messages);
    }

    /// <summary>Sends a message on the thread with a given client (creates the thread on first access).</summary>
    [HttpPost("chat/sessions/{clientTenantId:guid}/messages")]
    [ProducesResponseType(typeof(SupplierChatMessageDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SendChatMessage(
        Guid clientTenantId, [FromBody] SendSupplierChatMessageRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var callerUserId = ResolveUserId();
        if (callerUserId is null) return Forbid();

        var senderName = User.FindFirst("full_name")?.Value
                       ?? User.FindFirst(ClaimTypes.Name)?.Value
                       ?? User.FindFirst(ClaimTypes.Email)?.Value
                       ?? "Unknown";

        var session = await _chat.GetOrCreateSessionAsync(
            tenantId.Value, clientTenantId, isSupplierSide: true, callerUserId.Value, ct);

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
