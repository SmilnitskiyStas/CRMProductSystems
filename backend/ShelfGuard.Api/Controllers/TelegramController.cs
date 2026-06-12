using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Telegram;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/telegram")]
[Authorize] // any authenticated staff user may link their own account
public sealed class TelegramController : ControllerBase
{
    private readonly ITelegramLinkService _telegram;

    public TelegramController(ITelegramLinkService telegram) => _telegram = telegram;

    /// <summary>Issues a one-time deep-link code for binding the caller's Telegram account.</summary>
    [HttpPost("link-code")]
    [ProducesResponseType(typeof(LinkCodeDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateLinkCode(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Forbid();

        var dto = await _telegram.CreateLinkCodeAsync(userId, ct);
        return Ok(dto);
    }
}
