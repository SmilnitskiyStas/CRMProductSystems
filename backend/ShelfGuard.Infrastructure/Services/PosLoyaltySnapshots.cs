using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using ShelfGuard.Application.Features.Pos;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Services;

/// <summary>Authenticated scoped sale terms; never contains or authorizes a cached balance.</summary>
public sealed class PosLoyaltySnapshots : IPosLoyaltySnapshots
{
    public const int MaxPlainBytes = 1024 * 1024;
    public const int MaxTokenLength = 1400000;
    private static readonly byte[] Purpose = Encoding.UTF8.GetBytes("ShelfGuard.POS.LoyaltySnapshot.v1");
    private readonly byte[] _key;

    public PosLoyaltySnapshots(IConfiguration configuration)
    {
        var secret = configuration["Pos:PriceSnapshotSecret"] ?? configuration["Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
            throw new InvalidOperationException("POS loyalty snapshots require a secret of at least 32 bytes.");
        _key = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Purpose);
    }

    public IssuedPosLoyaltySnapshot? Issue(Guid tenantId, Guid storeId, Guid registerId, Guid memberId,
        Guid? customerId, PosLoyaltyTerms terms, DateTime issuedAt)
    {
        if (tenantId == Guid.Empty || storeId == Guid.Empty || registerId == Guid.Empty || memberId == Guid.Empty)
            throw new ArgumentException("Offline loyalty scope is required.");
        if (!terms.Valid) return null;
        var expires = issuedAt.AddDays(14);
        var plain = JsonSerializer.SerializeToUtf8Bytes(new PosLoyaltySnapshot(tenantId, storeId,
            registerId, memberId, customerId, terms, issuedAt, expires));
        if (plain.Length > MaxPlainBytes) return null;
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plain, cipher, tag, Purpose);
        return new("lo1." + WebEncoders.Base64UrlEncode(nonce.Concat(tag).Concat(cipher).ToArray()), expires);
    }

    public PosLoyaltySnapshot? Read(string token, Guid tenantId, Guid storeId, Guid registerId, Guid memberId, DateTime soldAt)
    {
        if (token.Length > MaxTokenLength || !token.StartsWith("lo1.", StringComparison.Ordinal)) return null;
        try
        {
            var bytes = WebEncoders.Base64UrlDecode(token[4..]);
            if (bytes.Length <= 28 || bytes.Length - 28 > MaxPlainBytes) return null;
            var plain = new byte[bytes.Length - 28];
            using var aes = new AesGcm(_key, 16);
            aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), plain, Purpose);
            var snapshot = JsonSerializer.Deserialize<PosLoyaltySnapshot>(plain);
            return snapshot is not null && snapshot.TenantId == tenantId && snapshot.StoreId == storeId
                && snapshot.RegisterId == registerId && snapshot.MembershipId == memberId && snapshot.Terms.Valid
                && soldAt >= snapshot.IssuedAt - TimeSpan.FromMinutes(5) && soldAt <= snapshot.ExpiresAt
                ? snapshot : null;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }
}
