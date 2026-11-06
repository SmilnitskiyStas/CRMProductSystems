using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.BeautyControl;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// ShelfGuard -> Beauty CRM control plane (ADR-018). Provider-only proxy: the HMAC key stays on the server,
/// the browser never sees it. Responses may carry a one-time owner invite URL, hence <c>no-store</c>.
/// Errors: <c>{ "error": "&lt;code&gt;", "code": "&lt;code&gt;" }</c>; Beauty's raw errors are never forwarded.
/// </summary>
[ApiController]
[Route("api/provider/beauty/tenants")]
[Authorize(Policy = AppPolicies.ProviderOnly)]
public sealed class ProviderBeautyController(IBeautyProvisioningService beauty) : ControllerBase
{
    [HttpPost]
    [RequireRecentMfa]
    public async Task<IActionResult> Create([FromBody] CreateBeautyTenantRequest request, CancellationToken ct)
    {
        var (actor, userId) = Actor();
        return Map(await beauty.CreateAsync(request, actor, userId, ct));
    }

    [HttpGet("{shelfGuardTenantId:guid}")]
    public async Task<IActionResult> Get(Guid shelfGuardTenantId, CancellationToken ct) =>
        Map(await beauty.GetAsync(shelfGuardTenantId, ct));

    [HttpPost("{shelfGuardTenantId:guid}/suspend")]
    [RequireRecentMfa]
    public async Task<IActionResult> Suspend(
        Guid shelfGuardTenantId, [FromBody] SuspendBeautyTenantRequest? request, CancellationToken ct)
    {
        var (actor, userId) = Actor();
        return Map(await beauty.SuspendAsync(shelfGuardTenantId, request ?? new(null), actor, userId, ct));
    }

    [HttpPost("{shelfGuardTenantId:guid}/resume")]
    [RequireRecentMfa]
    public async Task<IActionResult> Resume(Guid shelfGuardTenantId, CancellationToken ct)
    {
        var (actor, userId) = Actor();
        return Map(await beauty.ResumeAsync(shelfGuardTenantId, actor, userId, ct));
    }

    /// <summary>Re-issues the owner invite. The URL is returned once and is neither stored nor logged.</summary>
    [HttpPost("{shelfGuardTenantId:guid}/owner-invite")]
    [RequireRecentMfa]
    public async Task<IActionResult> OwnerInvite(Guid shelfGuardTenantId, CancellationToken ct)
    {
        var (actor, userId) = Actor();
        return Map(await beauty.IssueOwnerInviteAsync(shelfGuardTenantId, actor, userId, ct));
    }

    [HttpPut("{shelfGuardTenantId:guid}/modules")]
    [RequireRecentMfa]
    public async Task<IActionResult> SetModules(
        Guid shelfGuardTenantId, [FromBody] SetBeautyModulesRequest request, CancellationToken ct)
    {
        var (actor, userId) = Actor();
        return Map(await beauty.SetModulesAsync(shelfGuardTenantId, request, actor, userId, ct));
    }

    private (BeautyActor Actor, Guid? UserId) Actor()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? "provider";
        var display = User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue(ClaimTypes.Email) ?? "ShelfGuard provider";
        Guid? userId = Guid.TryParse(idClaim, out var g) ? g : null;
        return (new BeautyActor(Trunc(idClaim, 64), Trunc(display, 120)), userId);
    }

    private IActionResult Map<T>(BeautyResult<T> result)
    {
        Response.Headers.CacheControl = "no-store";
        if (result.Ok)
            return StatusCode(result.HttpStatus is 201 ? StatusCodes.Status201Created : StatusCodes.Status200OK, result.Data);

        var code = result.Code ?? "beauty_error";
        var status = result.Error switch
        {
            BeautyErrorKind.NotFound => StatusCodes.Status404NotFound,
            BeautyErrorKind.Conflict => StatusCodes.Status409Conflict,
            BeautyErrorKind.Validation => StatusCodes.Status422UnprocessableEntity,
            BeautyErrorKind.RateLimited => StatusCodes.Status429TooManyRequests,
            BeautyErrorKind.Disabled => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status502BadGateway,
        };
        return StatusCode(status, new { error = code, code });
    }

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max];
}
