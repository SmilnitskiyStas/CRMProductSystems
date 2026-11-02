using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using ShelfGuard.Application.Features.Pos;
using ShelfGuard.Application.Features.Pricing;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Services;

public sealed class PosScopedPrices : IPosScopedPrices
{
    private readonly byte[] key;
    public PosScopedPrices(IConfiguration configuration)
    {
        var secret = configuration["Pos:PriceSnapshotSecret"] ?? configuration["Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
            throw new InvalidOperationException("Scoped POS prices require a secret of at least 32 bytes.");
        key = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes("ShelfGuard.POS.ScopedPrices.v1"));
    }
    private string Seal<T>(string prefix, T value)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(value); var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length]; var tag = new byte[16];
        using var aes = new AesGcm(key, 16); aes.Encrypt(nonce, plain, cipher, tag, Encoding.UTF8.GetBytes(prefix));
        return prefix + WebEncoders.Base64UrlEncode(nonce.Concat(tag).Concat(cipher).ToArray());
    }
    private T? Open<T>(string token, string prefix) where T : class
    {
        if (token.Length > 16000 || !token.StartsWith(prefix, StringComparison.Ordinal)) return null;
        try
        {
            var bytes = WebEncoders.Base64UrlDecode(token[prefix.Length..]); if (bytes.Length <= 28) return null;
            var plain = new byte[bytes.Length - 28]; using var aes = new AesGcm(key, 16);
            aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), plain, Encoding.UTF8.GetBytes(prefix));
            return JsonSerializer.Deserialize<T>(plain);
        }
        catch (Exception e) when (e is CryptographicException or FormatException or JsonException or ArgumentException) { return null; }
    }
    public IssuedPosPriceSnapshot Issue(CatalogSyncRow row, Guid store, Guid register, DateTime at, DateTime? boundary, PosBasePriceDecision? basis)
    {
        if (row.TenantId == Guid.Empty || store == Guid.Empty || register == Guid.Empty || !row.IsActive
            || row.PriceRetail is not >= 0 || row.VatRate is < 0 or > 100 || at.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Invalid scoped price context.");
        var expires = at.AddDays(14); if (boundary is DateTime end && end < expires) expires = end;
        if (expires <= at) throw new ArgumentException("Price boundary already passed.");
        if (basis is not null && (!PlannedBasePricing.Matches(basis, new(row.Id, row.CategoryId, row.PriceRetail, 1, row.VatRate, false, []), at)
            || basis.NextBoundary != boundary)) throw new ArgumentException("Inconsistent base resolution.");
        var price = new PosPriceSnapshot(row.TenantId, row.Id, row.Name, row.Barcodes.ToArray(), row.CategoryId,
            row.Unit, row.PriceRetail.Value, row.VatRate, row.PricePurchase, at, expires);
        basis ??= new(row.Id, "planned-base.v1", "legacy_item_base", price.Price, price.VatRate,
            null, null, null, null, at, boundary, price.Price, price.VatRate);
        return new(Seal("pb1.", new ScopedPosPrice(price, store, register, basis)), expires);
    }
    public ScopedPosPrice? Read(string token, Guid tenant, Guid store, Guid register, string barcode, DateTime soldAt)
    {
        var value = Open<ScopedPosPrice>(token, "pb1.");
        return value is not null && value.Price.TenantId == tenant && value.StoreId == store && value.RegisterId == register
            && value.Price.Barcodes.Contains(barcode, StringComparer.Ordinal) && soldAt >= value.Price.IssuedAt.AddMinutes(-5)
            && soldAt < value.Price.ExpiresAt ? value : null;
    }
    public string Cursor(ScopedCatalogCursor cursor) => Seal("cc1.", cursor);
    public ScopedCatalogCursor? ReadCursor(string token, Guid tenant, Guid register, DateTime now)
    {
        var value = Open<ScopedCatalogCursor>(token, "cc1.");
        return value is not null && value.TenantId == tenant && value.RegisterId == register
            && now >= value.At.AddMinutes(-5) && now < value.ExpiresAt ? value : null;
    }
}
