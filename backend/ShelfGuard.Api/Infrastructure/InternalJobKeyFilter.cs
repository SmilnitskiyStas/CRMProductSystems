using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ShelfGuard.Api.Infrastructure;

/// <summary>
/// TASK-754: authenticates worker -> API internal job calls with a shared secret
/// (config <c>InternalJobs:Key</c>, env <c>InternalJobs__Key</c> = <c>INTERNAL_JOB_KEY</c> in compose).
///
///  - Key unset or shorter than <see cref="MinKeyLength"/> -> endpoint DISABLED (404, looks absent).
///  - Missing / wrong <c>X-Internal-Job-Key</c> header -> 401 (same response for both; no oracle).
///  - Request carrying an <c>Authorization</c> header -> 400: internal job calls must never mix with
///    a user JWT (the JWT would otherwise set a tenant/role on the DB session for this request).
/// Comparison is constant-time over SHA-256 digests (length-independent).
/// Network-level: /api/internal/ is also blocked at the gateway (infra/gateway/nginx.conf); the
/// worker reaches api_core on a loopback-only host port.
/// </summary>
public sealed class InternalJobKeyFilter : IAuthorizationFilter
{
    public const string HeaderName = "X-Internal-Job-Key";
    public const string ConfigKey = "InternalJobs:Key";
    public const int MinKeyLength = 32;

    private readonly IConfiguration _config;
    private readonly ILogger<InternalJobKeyFilter> _logger;

    public InternalJobKeyFilter(IConfiguration config, ILogger<InternalJobKeyFilter> logger)
    {
        _config = config;
        _logger = logger;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var expected = _config[ConfigKey];
        if (string.IsNullOrWhiteSpace(expected) || expected.Length < MinKeyLength)
        {
            context.Result = new NotFoundResult();
            return;
        }

        var request = context.HttpContext.Request;
        if (request.Headers.ContainsKey("Authorization"))
        {
            context.Result = new BadRequestObjectResult(new { error = "Internal job calls must not carry an Authorization header." });
            return;
        }

        var provided = request.Headers[HeaderName].ToString();
        if (provided.Length == 0 || !KeysMatch(provided, expected))
        {
            _logger.LogWarning("Rejected internal job call to {Path}: missing or invalid {Header}",
                request.Path, HeaderName);
            context.Result = new UnauthorizedObjectResult(new { error = "Unauthorized." });
        }
    }

    internal static bool KeysMatch(string provided, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(provided)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}
