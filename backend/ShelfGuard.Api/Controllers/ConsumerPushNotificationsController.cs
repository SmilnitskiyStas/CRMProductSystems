using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Api.Controllers;

/// <summary>Registers the authenticated consumer's Expo token. A staff JWT cannot use this
/// endpoint because only consumer sessions contain the consumer_account_id claim.</summary>
[ApiController]
[Route("api/consumer/push-token")]
[Authorize]
public sealed class ConsumerPushNotificationsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ConsumerPushNotificationsController(AppDbContext db) => _db = db;

    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterPushTokenRequest request, CancellationToken ct)
    {
        var consumerId = ResolveConsumerAccountId();
        if (consumerId is null) return Forbid();

        var token = request.Token?.Trim() ?? string.Empty;
        if (token.Length is < 10 or > 255 ||
            (!token.StartsWith("ExponentPushToken[", StringComparison.Ordinal) &&
             !token.StartsWith("ExpoPushToken[", StringComparison.Ordinal)))
            return BadRequest(new { error = "Invalid Expo push token." });

        var account = await _db.ConsumerAccounts.FirstOrDefaultAsync(x => x.Id == consumerId.Value, ct);
        if (account is null || !account.IsActive) return NotFound();
        account.PushToken = token;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unregister(CancellationToken ct)
    {
        var consumerId = ResolveConsumerAccountId();
        if (consumerId is null) return Forbid();

        var account = await _db.ConsumerAccounts.FirstOrDefaultAsync(x => x.Id == consumerId.Value, ct);
        if (account is null) return NoContent();
        account.PushToken = null;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private Guid? ResolveConsumerAccountId()
    {
        var claim = User.FindFirst("consumer_account_id")?.Value;
        return Guid.TryParse(claim, out var id) && id != Guid.Empty ? id : null;
    }
}

public sealed record RegisterPushTokenRequest(string Token);
