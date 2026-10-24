using ShelfGuard.Api.Infrastructure;
using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ShelfGuard.Application.Features.Auth;
using ShelfGuard.Application.Features.Auth.Dtos;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/internal/worker-session")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InternalWorkerAuthController(WorkerAuthService auth, IConfiguration configuration) : ControllerBase
{
    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        // Only the host-network worker may exchange the explicitly configured service credential.
        // Public gateway blocks /api/internal; core's :5110 is bound exclusively to loopback.
        if (!WorkerPeerBoundary.IsTrusted(HttpContext, configuration)) return NotFound();
        var grant = await auth.LoginAsync(request.Email, request.Password, ct);
        return grant == null ? Unauthorized(new { error = "Invalid service credentials." }) : Ok(grant);
    }
}
