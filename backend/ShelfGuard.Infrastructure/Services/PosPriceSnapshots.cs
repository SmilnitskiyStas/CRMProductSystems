using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using ShelfGuard.Application.Features.Pos;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Services;

public sealed class PosPriceSnapshots : IPosPriceSnapshots
{
    private static readonly byte[] Purpose = Encoding.UTF8.GetBytes("ShelfGuard.POS.PriceSnapshot.v1");
    private readonly byte[] _key;

    public PosPriceSnapshots(IConfiguration configuration)
    {
        var secret = configuration["Pos:PriceSnapshotSecret"] ?? configuration["Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
            throw new InvalidOperationException("POS price snapshots require a secret of at least 32 bytes.");
        _key = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Purpose);
    }

    public IssuedPosPriceSnapshot Issue(CatalogSyncRow item, DateTime issuedAt)
    {
        if (item.TenantId == Guid.Empty || item.PriceRetail is not >= 0 || item.VatRate is < 0 or > 100)
            throw new ArgumentException("Invalid catalog pricing values.", nameof(item));
        var expiresAt = issuedAt.AddDays(14);
        var snapshot = new PosPriceSnapshot(item.TenantId, item.Id, item.Name, item.Barcodes.ToArray(),
            item.CategoryId, item.Unit, item.PriceRetail.Value, item.VatRate, item.PricePurchase, issuedAt, expiresAt);
        var plain = JsonSerializer.SerializeToUtf8Bytes(snapshot);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plain, cipher, tag, Purpose);
        var bytes = nonce.Concat(tag).Concat(cipher).ToArray();
        return new IssuedPosPriceSnapshot("p1." + WebEncoders.Base64UrlEncode(bytes), expiresAt);
    }

    public PosPriceSnapshot? Read(string token, Guid tenantId, string barcode, DateTime soldAt)
    {
        if (token.Length > 16000 || !token.StartsWith("p1.", StringComparison.Ordinal)) return null;
        try
        {
            var bytes = WebEncoders.Base64UrlDecode(token[3..]);
            if (bytes.Length <= 28) return null;
            var plain = new byte[bytes.Length - 28];
            using var aes = new AesGcm(_key, 16);
            aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), plain, Purpose);
            var snapshot = JsonSerializer.Deserialize<PosPriceSnapshot>(plain);
            return snapshot is not null && snapshot.TenantId == tenantId
                && snapshot.Barcodes.Contains(barcode, StringComparer.Ordinal)
                && soldAt >= snapshot.IssuedAt - TimeSpan.FromMinutes(5) && soldAt <= snapshot.ExpiresAt
                ? snapshot : null;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }
}
