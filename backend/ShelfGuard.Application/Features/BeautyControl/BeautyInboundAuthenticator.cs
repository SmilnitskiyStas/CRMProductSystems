using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace ShelfGuard.Application.Features.BeautyControl;

public enum BeautyAuthOutcome { Ok, Failed, RateLimited }

public sealed record BeautyInboundAuthInput(
    string Method, string Path, string Query,
    string? KeyId, string? Timestamp, string? Nonce, string? Signature, byte[] Body);

/// <summary>Replay protection for nonces (registered only after a valid signature).</summary>
public interface IBeautyNonceStore
{
    /// <returns>true if the nonce was new (and is now remembered for <paramref name="ttl"/>), false on replay.</returns>
    bool TryRegister(string kid, string nonce, TimeSpan ttl);
}

/// <summary>Source of inbound keys (config-backed in Infrastructure; secrets come only from env).</summary>
public interface IBeautyInboundKeyProvider
{
    bool TryGet(string kid, out BeautyInboundKey key);
}

public interface IBeautyInboundAuthenticator
{
    BeautyAuthOutcome Verify(BeautyInboundAuthInput input);
}

/// <summary>
/// Verifies Beauty -> ShelfGuard requests (Beauty contracts §27.2). Order: headers -> kid -> skew -> body size ->
/// signature (constant time) -> nonce -> per-kid rate limit. The real failure reason goes only to the log
/// (no signature, no secret, no PII); the HTTP answer is always the same 401 control_auth_failed.
/// </summary>
public sealed partial class BeautyInboundAuthenticator(
    IBeautyInboundKeyProvider keys,
    IBeautyNonceStore nonces,
    TimeProvider clock,
    ILogger<BeautyInboundAuthenticator> logger) : IBeautyInboundAuthenticator
{
    public const int RateLimitPerMinute = 60;
    public static readonly TimeSpan NonceTtl = TimeSpan.FromMinutes(15);

    [GeneratedRegex("^[a-z0-9-]{3,32}$")] private static partial Regex KidRegex();
    [GeneratedRegex("^[A-Za-z0-9_-]{22,64}$")] private static partial Regex NonceRegex();
    [GeneratedRegex("^(0|[1-9][0-9]{0,15})$")] private static partial Regex TimestampRegex();
    [GeneratedRegex("^v1=[A-Za-z0-9_-]{43}$")] private static partial Regex SignatureRegex();

    // kid -> (window start unix minute, count). Only known kids are tracked, so the dictionary is bounded by config.
    private readonly ConcurrentDictionary<string, (long Window, int Count)> _windows = new();

    public BeautyAuthOutcome Verify(BeautyInboundAuthInput i)
    {
        if (string.IsNullOrEmpty(i.KeyId) || string.IsNullOrEmpty(i.Timestamp) || string.IsNullOrEmpty(i.Nonce)
            || string.IsNullOrEmpty(i.Signature))
            return Fail("missing_header");
        if (!KidRegex().IsMatch(i.KeyId) || !NonceRegex().IsMatch(i.Nonce)
            || !TimestampRegex().IsMatch(i.Timestamp) || !SignatureRegex().IsMatch(i.Signature))
            return Fail("missing_header");
        if (i.Query.Length > 0) return Fail("query_not_allowed");

        if (!keys.TryGet(i.KeyId, out var entry)
            || !IsUsable(entry.Status)
            || !BeautyHmac.TryDecodeKey(entry.Secret, out var key))
            return Fail("unknown_key");

        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        if (!long.TryParse(i.Timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var ts)
            || Math.Abs(now - ts) > BeautyHmac.MaxSkewSeconds)
            return Fail("timestamp_skew");

        if (i.Body.Length > BeautyHmac.MaxBodyBytes) return Fail("body_too_large");

        var expected = BeautyHmac.Sign(key, i.KeyId, i.Timestamp, i.Nonce, i.Method, i.Path, i.Query, i.Body);
        if (!BeautyHmac.SignatureEquals(expected, i.Signature)) return Fail("bad_signature");

        if (!nonces.TryRegister(i.KeyId, i.Nonce, NonceTtl)) return Fail("nonce_replayed");

        if (!AllowRate(i.KeyId, now / 60))
        {
            logger.LogWarning("Beauty inbound rate limited: kid {Kid}.", i.KeyId);
            return BeautyAuthOutcome.RateLimited;
        }
        return BeautyAuthOutcome.Ok;
    }

    private static bool IsUsable(string? status) =>
        string.Equals(status, "active", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "retiring", StringComparison.OrdinalIgnoreCase);

    private bool AllowRate(string kid, long window)
    {
        var allowed = true;
        _windows.AddOrUpdate(kid,
            _ => (window, 1),
            (_, cur) =>
            {
                if (cur.Window != window) return (window, 1);
                if (cur.Count >= RateLimitPerMinute) { allowed = false; return cur; }
                return (window, cur.Count + 1);
            });
        return allowed;
    }

    private BeautyAuthOutcome Fail(string reason)
    {
        logger.LogWarning("Beauty inbound auth failed: {Reason}.", reason);
        return BeautyAuthOutcome.Failed;
    }
}
