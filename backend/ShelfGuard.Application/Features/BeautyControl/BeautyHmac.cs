using System.Security.Cryptography;
using System.Text;

namespace ShelfGuard.Application.Features.BeautyControl;

/// <summary>
/// HMAC-SHA256 request signing v1 shared by both directions with Beauty CRM (ADR-018, Beauty contracts §27.2).
/// Pure functions: no I/O, no clock. The key is the RAW bytes of the base64-decoded secret (not the base64 text).
/// </summary>
public static class BeautyHmac
{
    public const string Prefix = "BCRM-HMAC-SHA256-v1";
    public const string SignaturePrefix = "v1=";
    public const int MaxSkewSeconds = 300;
    public const int MaxBodyBytes = 64 * 1024;
    public const int MinKeyBytes = 32;

    public const string HeaderKeyId = "X-BCRM-Key-Id";
    public const string HeaderTimestamp = "X-BCRM-Timestamp";
    public const string HeaderNonce = "X-BCRM-Nonce";
    public const string HeaderSignature = "X-BCRM-Signature";
    public const string HeaderRequestId = "X-Request-Id";
    public const string HeaderServerTime = "X-BCRM-Server-Time";

    public static string BodySha256Hex(ReadOnlySpan<byte> body) =>
        Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();

    /// <summary>Lines joined with LF, no trailing newline, UTF-8 when hashed.</summary>
    public static string Canonical(
        string kid, string timestamp, string nonce, string method, string path, string query, ReadOnlySpan<byte> body) =>
        string.Join('\n', Prefix, kid, timestamp, nonce, method.ToUpperInvariant(), path, query, BodySha256Hex(body));

    /// <returns><c>v1=&lt;base64url without padding&gt;</c></returns>
    public static string Sign(byte[] key, string canonical)
    {
        var mac = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(canonical));
        return SignaturePrefix + Base64Url(mac);
    }

    public static string Sign(
        byte[] key, string kid, string timestamp, string nonce, string method, string path, string query, ReadOnlySpan<byte> body) =>
        Sign(key, Canonical(kid, timestamp, nonce, method, path, query, body));

    public static bool SignatureEquals(string expected, string? provided)
    {
        if (provided is null) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(provided));
    }

    /// <summary>Decodes a base64 secret; false if invalid or shorter than 32 bytes (fail closed).</summary>
    public static bool TryDecodeKey(string? base64Secret, out byte[] key)
    {
        key = [];
        if (string.IsNullOrWhiteSpace(base64Secret)) return false;
        Span<byte> buffer = new byte[base64Secret.Length];
        if (!Convert.TryFromBase64String(base64Secret.Trim(), buffer, out var written) || written < MinKeyBytes)
            return false;
        key = buffer[..written].ToArray();
        return true;
    }

    /// <summary>First 12 hex of sha256(raw key bytes): the only key-derived value allowed in logs.</summary>
    public static string Fingerprint(byte[] key) => BodySha256Hex(key)[..12];

    /// <summary>128+ bits of CSPRNG as base64url (32 chars from 24 bytes).</summary>
    public static string NewNonce() => Base64Url(RandomNumberGenerator.GetBytes(24));

    public static string Base64Url(ReadOnlySpan<byte> data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
