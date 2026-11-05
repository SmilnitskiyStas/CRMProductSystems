using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.BeautyControl;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Inbound requests from Beauty CRM (ADR-018, Beauty contracts §27.9). Authenticated by HMAC v1 with a dedicated
/// inbound key (no JWT, no IP allowlist). Every authentication failure is the same 401 <c>control_auth_failed</c>.
/// </summary>
[ApiController]
[Route("api/integrations/beauty")]
[AllowAnonymous]
public sealed class BeautyIntegrationController(
    IBeautyInboundAuthenticator auth,
    IBeautyFeedbackService feedback,
    TimeProvider clock) : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [HttpPost("feedback")]
    public async Task<IActionResult> Feedback(CancellationToken ct)
    {
        Response.Headers[BeautyHmac.HeaderServerTime] = clock.GetUtcNow().ToUnixTimeSeconds().ToString();
        Response.Headers.CacheControl = "no-store";

        // Read at most MaxBodyBytes+1 so an oversized body is detected without buffering it all.
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while (ms.Length <= BeautyHmac.MaxBodyBytes
               && (read = await Request.Body.ReadAsync(buffer, ct)) > 0)
            ms.Write(buffer, 0, read);
        var body = ms.ToArray();

        var outcome = auth.Verify(new BeautyInboundAuthInput(
            Request.Method,
            Request.PathBase.Add(Request.Path).Value ?? string.Empty,
            Request.QueryString.HasValue ? Request.QueryString.Value!.TrimStart('?') : string.Empty,
            Request.Headers[BeautyHmac.HeaderKeyId].ToString(),
            Request.Headers[BeautyHmac.HeaderTimestamp].ToString(),
            Request.Headers[BeautyHmac.HeaderNonce].ToString(),
            Request.Headers[BeautyHmac.HeaderSignature].ToString(),
            body));

        if (outcome == BeautyAuthOutcome.RateLimited)
        {
            Response.Headers.RetryAfter = "60";
            return StatusCode(StatusCodes.Status429TooManyRequests, new { code = "rate_limited" });
        }
        if (outcome != BeautyAuthOutcome.Ok)
            return Unauthorized(new { code = "control_auth_failed" });

        BeautyFeedbackRequest? request;
        try { request = JsonSerializer.Deserialize<BeautyFeedbackRequest>(body, Json); }
        catch (JsonException) { request = null; }
        if (request is null) return UnprocessableEntity(new { code = "invalid_body" });

        var result = await feedback.IngestAsync(request, ct);
        switch (result.Status)
        {
            case BeautyFeedbackStatus.Created:
                return StatusCode(StatusCodes.Status201Created, new { leadId = result.LeadId, replayed = false });
            case BeautyFeedbackStatus.Replayed:
                Response.Headers["Idempotent-Replayed"] = "true";
                return Ok(new { leadId = result.LeadId, replayed = true });
            default:
                return UnprocessableEntity(new { code = result.ErrorCode ?? "invalid_body" });
        }
    }
}
