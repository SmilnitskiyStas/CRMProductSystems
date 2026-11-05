using System.Text;
using System.Text.Json;
using ShelfGuard.Application.Features.BeautyControl;

namespace ShelfGuard.Tests.BeautyControl;

public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

public sealed record Vector(
    string Name, string Method, string Path, string Query, long Timestamp, string Nonce,
    byte[] Body, string Canonical, string ExpectedSignature, string TrailingSpaceSignature);

public static class VectorFile
{
    public const string Kid = "test-k1";

    public static byte[] Key { get; private set; } = [];
    public static string SecretBase64 { get; private set; } = string.Empty;
    public static string Fingerprint { get; private set; } = string.Empty;
    public static IReadOnlyList<Vector> Vectors { get; private set; } = [];

    static VectorFile()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "BeautyControl", "ADR-018-hmac-test-vector.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        var root = doc.RootElement;
        SecretBase64 = root.GetProperty("key").GetProperty("secretBase64").GetString()!;
        Fingerprint = root.GetProperty("key").GetProperty("fingerprint").GetString()!;
        Key = Convert.FromBase64String(SecretBase64);
        Vectors = root.GetProperty("vectors").EnumerateArray().Select(v => new Vector(
            v.GetProperty("name").GetString()!,
            v.GetProperty("method").GetString()!,
            v.GetProperty("path").GetString()!,
            v.GetProperty("query").GetString()!,
            v.GetProperty("timestamp").GetInt64(),
            v.GetProperty("nonce").GetString()!,
            Convert.FromBase64String(v.GetProperty("bodyBase64").GetString()!),
            v.GetProperty("canonicalString").GetString()!,
            v.GetProperty("expectedSignature").GetString()!,
            v.GetProperty("negative").GetProperty("bodyWithTrailingSpaceSignature").GetString()!)).ToList();
    }

    public static Vector Get(string name) => Vectors.Single(v => v.Name == name);
}

public sealed class TestKeyProvider(string status = "active") : IBeautyInboundKeyProvider
{
    public bool TryGet(string kid, out BeautyInboundKey key)
    {
        if (kid == VectorFile.Kid)
        {
            key = new BeautyInboundKey { Secret = VectorFile.SecretBase64, Status = status };
            return true;
        }
        key = new BeautyInboundKey();
        return false;
    }
}

public sealed class FakeNonceStore : IBeautyNonceStore
{
    private readonly HashSet<string> _seen = [];
    public bool TryRegister(string kid, string nonce, TimeSpan ttl) => _seen.Add($"{kid}:{nonce}");
}
