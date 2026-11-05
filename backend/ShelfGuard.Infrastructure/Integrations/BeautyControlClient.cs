using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShelfGuard.Application.Features.BeautyControl;

namespace ShelfGuard.Infrastructure.Integrations;

/// <summary>
/// HMAC-signed HttpClient for Beauty CRM control plane (ADR-018 / Beauty contracts §27).
/// The secret comes only from env config; request/response bodies and invite URLs are never logged.
/// No retries: every attempt is a new request with a fresh nonce, decided by the caller.
/// </summary>
public sealed partial class BeautyControlClient(
    HttpClient http,
    IOptionsMonitor<BeautyControlOptions> options,
    TimeProvider clock,
    ILogger<BeautyControlClient> logger) : IBeautyControlClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string BasePath = "/api/control/v1/tenants";

    [GeneratedRegex("^[a-z][a-z0-9_]{0,63}$")]
    private static partial Regex SafeCode();

    public Task<BeautyResult<BeautyCreatedTenant>> CreateTenantAsync(BeautyCreateTenantCommand c, CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["externalRef"] = c.ExternalRef,
            ["name"] = c.Name,
            ["slug"] = c.Slug,
            ["owner"] = new { email = c.OwnerEmail, fullName = c.OwnerFullName },
            ["actor"] = c.Actor,
        };
        if (c.Modules is not null) body["modules"] = c.Modules;
        return SendAsync<BeautyCreatedTenant>("create_tenant", HttpMethod.Post, BasePath, body, ct);
    }

    public Task<BeautyResult<BeautyTenantInfo>> GetTenantAsync(string externalRef, CancellationToken ct) =>
        SendAsync<BeautyTenantInfo>("get_tenant", HttpMethod.Get, $"{BasePath}/{externalRef}", null, ct);

    public Task<BeautyResult<BeautyStatusInfo>> SuspendAsync(string externalRef, string reason, BeautyActor actor, CancellationToken ct) =>
        SendAsync<BeautyStatusInfo>("suspend", HttpMethod.Post, $"{BasePath}/{externalRef}/suspend", new { reason, actor }, ct);

    public Task<BeautyResult<BeautyStatusInfo>> ResumeAsync(string externalRef, BeautyActor actor, CancellationToken ct) =>
        SendAsync<BeautyStatusInfo>("resume", HttpMethod.Post, $"{BasePath}/{externalRef}/resume", new { actor }, ct);

    public Task<BeautyResult<BeautyModulesInfo>> SetModulesAsync(string externalRef, IReadOnlyList<string> modules, BeautyActor actor, CancellationToken ct) =>
        SendAsync<BeautyModulesInfo>("set_modules", HttpMethod.Put, $"{BasePath}/{externalRef}/modules", new { modules, actor }, ct);

    public Task<BeautyResult<BeautyInviteInfo>> IssueOwnerInviteAsync(string externalRef, BeautyActor actor, CancellationToken ct) =>
        SendAsync<BeautyInviteInfo>("owner_invite", HttpMethod.Post, $"{BasePath}/{externalRef}/owner-invite", new { actor }, ct);

    private async Task<BeautyResult<T>> SendAsync<T>(string op, HttpMethod method, string path, object? payload, CancellationToken ct)
    {
        var o = options.CurrentValue;
        if (!o.Enabled
            || string.IsNullOrWhiteSpace(o.KeyId)
            || !Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out var baseUri)
            || !IsAllowedBaseUri(baseUri)
            || !BeautyHmac.TryDecodeKey(o.Secret, out var key))
        {
            return BeautyResult<T>.Fail(BeautyErrorKind.Disabled, "beauty_control_disabled");
        }

        var uri = new Uri(baseUri, path);
        // Sign exactly the bytes that are sent (no re-serialization after signing).
        var bodyBytes = payload is null ? [] : JsonSerializer.SerializeToUtf8Bytes(payload, Json);
        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var nonce = BeautyHmac.NewNonce();
        var signature = BeautyHmac.Sign(key, o.KeyId, timestamp, nonce, method.Method, uri.AbsolutePath, string.Empty, bodyBytes);

        using var request = new HttpRequestMessage(method, uri);
        request.Headers.TryAddWithoutValidation(BeautyHmac.HeaderKeyId, o.KeyId);
        request.Headers.TryAddWithoutValidation(BeautyHmac.HeaderTimestamp, timestamp);
        request.Headers.TryAddWithoutValidation(BeautyHmac.HeaderNonce, nonce);
        request.Headers.TryAddWithoutValidation(BeautyHmac.HeaderSignature, signature);
        request.Headers.TryAddWithoutValidation(BeautyHmac.HeaderRequestId, Guid.NewGuid().ToString("N"));
        if (payload is not null)
        {
            request.Content = new ByteArrayContent(bodyBytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(o.TimeoutSeconds, 1, 60)));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cts.Token);
            var status = (int)response.StatusCode;
            if (status is >= 300 and < 400)
            {
                // Redirects are never followed (signed headers must not reach another target).
                logger.LogWarning("Beauty control {Op}: unexpected redirect (HTTP {Status}).", op, status);
                return BeautyResult<T>.Fail(BeautyErrorKind.Unknown, "beauty_bad_response", status);
            }
            var text = await response.Content.ReadAsStringAsync(cts.Token);

            if (response.IsSuccessStatusCode)
            {
                try
                {
                    var data = JsonSerializer.Deserialize<T>(text, Json);
                    return new BeautyResult<T>(data, BeautyErrorKind.None, null, status)
                    {
                        Replayed = status == 200 && op == "create_tenant",
                    };
                }
                catch (JsonException)
                {
                    logger.LogWarning("Beauty control {Op}: unparsable success body (HTTP {Status}).", op, status);
                    return BeautyResult<T>.Fail(BeautyErrorKind.Unknown, "beauty_bad_response", status);
                }
            }

            var kind = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => BeautyErrorKind.AuthFailed,
                HttpStatusCode.NotFound => BeautyErrorKind.NotFound,
                HttpStatusCode.Conflict => BeautyErrorKind.Conflict,
                HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => BeautyErrorKind.Validation,
                HttpStatusCode.TooManyRequests => BeautyErrorKind.RateLimited,
                >= HttpStatusCode.InternalServerError => BeautyErrorKind.Unavailable,
                _ => BeautyErrorKind.Unknown,
            };
            var code = kind == BeautyErrorKind.AuthFailed ? "beauty_auth_failed" : ExtractCode(text) ?? DefaultCode(kind);
            logger.LogWarning("Beauty control {Op} failed: HTTP {Status}, code {Code}.", op, status, code);
            return BeautyResult<T>.Fail(kind, code, status);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Beauty control {Op} timed out.", op);
            return BeautyResult<T>.Fail(BeautyErrorKind.Unavailable, "beauty_unavailable");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Beauty control {Op} transport error: {ErrorType}.", op, ex.GetType().Name);
            return BeautyResult<T>.Fail(BeautyErrorKind.Unavailable, "beauty_unavailable");
        }
    }

    /// <summary>https always; plain http only for loopback (local dev). Anything else fails closed.</summary>
    internal static bool IsAllowedBaseUri(Uri uri)
    {
        if (!string.IsNullOrEmpty(uri.UserInfo)) return false;
        if (uri.Scheme == Uri.UriSchemeHttps) return true;
        return uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
    }

    private static string DefaultCode(BeautyErrorKind kind) => kind switch
    {
        BeautyErrorKind.NotFound => "tenant_not_found",
        BeautyErrorKind.Conflict => "beauty_conflict",
        BeautyErrorKind.Validation => "beauty_validation_failed",
        BeautyErrorKind.RateLimited => "rate_limited",
        BeautyErrorKind.Unavailable => "beauty_unavailable",
        _ => "beauty_error",
    };

    /// <summary>Only a well-formed snake_case <c>code</c> from Beauty is passed on; everything else is dropped.</summary>
    private static string? ExtractCode(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("code", out var c)
                && c.ValueKind == JsonValueKind.String
                && c.GetString() is { } s && SafeCode().IsMatch(s))
                return s;
        }
        catch (JsonException) { }
        return null;
    }
}
