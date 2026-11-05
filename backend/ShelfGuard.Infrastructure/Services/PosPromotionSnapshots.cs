using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using ShelfGuard.Application.Features.Pos;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Services;

/// <summary>Authenticated, self-contained offline lease; independent of mutable publication pointers.</summary>
public sealed class PosPromotionSnapshots : IPosPromotionSnapshots
{
    public const int MaxRules = 128;
    public const int MaxPlainBytes = 128 * 1024;
    public const int MaxTokenLength = 175000;
    private static readonly byte[] Purpose = Encoding.UTF8.GetBytes("ShelfGuard.POS.PromotionSnapshot.v1");
    private readonly byte[] _key;

    public PosPromotionSnapshots(IConfiguration configuration)
    {
        var secret = configuration["Pos:PriceSnapshotSecret"] ?? configuration["Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
            throw new InvalidOperationException("POS promotion snapshots require a secret of at least 32 bytes.");
        _key = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Purpose);
    }

    public IssuedPosPromotionSnapshot? Issue(Guid tenantId, Guid storeId, Guid registerId,
        string? timeZone, IReadOnlyList<PublishedPromotionRuleRow> rules, DateTime issuedAt)
    {
        if (tenantId == Guid.Empty || storeId == Guid.Empty || registerId == Guid.Empty)
            throw new ArgumentException("Offline promotion scope is required.");
        if (rules.Count > MaxRules) return null;
        var expires = issuedAt.AddDays(14);
        var plain = JsonSerializer.SerializeToUtf8Bytes(new PosPromotionSnapshot(tenantId, storeId,
            registerId, timeZone, rules, issuedAt, expires));
        if (plain.Length > MaxPlainBytes) return null;
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plain, cipher, tag, Purpose);
        return new("pr1." + WebEncoders.Base64UrlEncode(nonce.Concat(tag).Concat(cipher).ToArray()), expires);
    }

    public PosPromotionSnapshot? Read(string token, Guid tenantId, Guid storeId, Guid registerId, DateTime soldAt)
    {
        if (token.Length > MaxTokenLength || !token.StartsWith("pr1.", StringComparison.Ordinal)) return null;
        try
        {
            var bytes = WebEncoders.Base64UrlDecode(token[4..]);
            if (bytes.Length <= 28 || bytes.Length - 28 > MaxPlainBytes) return null;
            var plain = new byte[bytes.Length - 28];
            using var aes = new AesGcm(_key, 16);
            aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), plain, Purpose);
            var snapshot = JsonSerializer.Deserialize<PosPromotionSnapshot>(plain);
            return snapshot is not null && snapshot.TenantId == tenantId && snapshot.StoreId == storeId
                && snapshot.RegisterId == registerId && snapshot.Rules.Count <= MaxRules
                && soldAt >= snapshot.IssuedAt - TimeSpan.FromMinutes(5) && soldAt <= snapshot.ExpiresAt
                ? snapshot : null;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }
}
