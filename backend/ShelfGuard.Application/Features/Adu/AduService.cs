using ShelfGuard.Application.Features.Adu.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Adu;

public sealed class AduService : IAduService
{
    private readonly IAduRepository _repo;

    public AduService(IAduRepository repo) => _repo = repo;

    public async Task<(AduDto? Adu, string? Error)> GetAsync(
        Guid storeId, Guid productId, CancellationToken ct = default)
    {
        var adu = await _repo.GetAsync(storeId, productId, ct);
        if (adu is null)
            return (null, "ADU not calculated for this product/store.");

        return (ToDto(adu), null);
    }

    public async Task<(RecalculateResult? Result, string? Error)> RecalculateAsync(
        Guid tenantId, Guid storeId, CancellationToken ct = default)
    {
        if (!await _repo.StoreExistsAsync(storeId, ct))
            return (null, "Store not found.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var productIds = await _repo.GetEligibleProductIdsAsync(storeId, ct);
        if (productIds.Count == 0)
            return (new RecalculateResult(storeId, 0, 0, 0), null);

        var windowStart = today.AddDays(-AduCalculator.MaxWindowDays);
        var sales = await _repo.GetSalesWindowAsync(storeId, productIds, windowStart, ct);
        var salesByProduct = sales
            .GroupBy(s => s.ProductId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var existing = await _repo.GetByStoreAsync(storeId, ct);
        var now = DateTime.UtcNow;
        int withEffective = 0, insufficient = 0;

        foreach (var productId in productIds)
        {
            salesByProduct.TryGetValue(productId, out var productSales);
            var calc = AduCalculator.Compute(productSales ?? [], today);

            if (calc.AduEffective is not null) withEffective++;
            else insufficient++;

            if (existing.TryGetValue(productId, out var row))
            {
                row.Adu30d = calc.Adu30d;
                row.Adu60d = calc.Adu60d;
                row.Adu90d = calc.Adu90d;
                row.AduEffective = calc.AduEffective;
                row.ProductGroup = calc.ProductGroup;
                row.ValidDays30d = calc.ValidDays30;
                row.ValidDays60d = calc.ValidDays60;
                row.CalculatedAt = now;
            }
            else
            {
                await _repo.AddAsync(new ProductAdu
                {
                    TenantId = tenantId,
                    StoreId = storeId,
                    ProductId = productId,
                    Adu30d = calc.Adu30d,
                    Adu60d = calc.Adu60d,
                    Adu90d = calc.Adu90d,
                    AduEffective = calc.AduEffective,
                    ProductGroup = calc.ProductGroup,
                    ValidDays30d = calc.ValidDays30,
                    ValidDays60d = calc.ValidDays60,
                    CalculatedAt = now,
                }, ct);
            }
        }

        await _repo.SaveChangesAsync(ct);
        return (new RecalculateResult(storeId, productIds.Count, withEffective, insufficient), null);
    }

    private static AduDto ToDto(ProductAdu a) => new(
        a.StoreId, a.ProductId, a.Product?.Name,
        a.Adu30d, a.Adu60d, a.Adu90d, a.AduEffective,
        a.ProductGroup, a.ValidDays30d, a.ValidDays60d, a.CalculatedAt);
}

/// <summary>
/// Pure ADU math (v2-spec §1). Static and side-effect-free so it can be unit-tested
/// without a database.
///
/// Valid day: not promo, not anomaly, and (sold > 0 OR end-of-day stock > 0).
/// Days with no daily_sales row carry no information and are never valid.
///
/// Product group is assigned from data density, tightest window first:
///   group 3 — ≥20 valid days in 30  → effective = ADU30
///   group 2 — ≥15 valid days in 60  → effective = ADU60
///   group 1 — ≥10 valid days in 90  → effective = ADU90
/// If even group 1's minimum is not met → no group, effective = null (insufficient data).
/// </summary>
internal static class AduCalculator
{
    internal const int MaxWindowDays = 90;

    internal sealed record AduResult(
        decimal? Adu30d, decimal? Adu60d, decimal? Adu90d,
        decimal? AduEffective, short? ProductGroup,
        int ValidDays30, int ValidDays60, int ValidDays90);

    internal static AduResult Compute(IReadOnlyList<DailySale> sales, DateOnly today)
    {
        var validDays = sales
            .Where(IsValidDay)
            .Where(s => s.Date < today) // today is incomplete — never count it
            .DistinctBy(s => s.Date)
            .ToList();

        var (adu30, valid30) = WindowAdu(validDays, today, 30);
        var (adu60, valid60) = WindowAdu(validDays, today, 60);
        var (adu90, valid90) = WindowAdu(validDays, today, 90);

        short? group =
            valid30 >= 20 ? (short)3 :
            valid60 >= 15 ? (short)2 :
            valid90 >= 10 ? (short)1 : null;

        var effective = group switch
        {
            3 => adu30,
            2 => adu60,
            1 => adu90,
            _ => null,
        };

        return new AduResult(adu30, adu60, adu90, effective, group, valid30, valid60, valid90);
    }

    internal static bool IsValidDay(DailySale s) =>
        !s.IsPromoDay
        && !s.IsAnomaly
        && (s.QuantitySold > 0 || s.QuantityEndOfDay > 0);

    private static (decimal? Adu, int ValidDays) WindowAdu(
        List<DailySale> validDays, DateOnly today, int windowDays)
    {
        var from = today.AddDays(-windowDays);
        var inWindow = validDays.Where(s => s.Date >= from).ToList();
        if (inWindow.Count == 0) return (null, 0);

        var adu = Math.Round(inWindow.Sum(s => s.QuantitySold) / inWindow.Count, 4);
        return (adu, inWindow.Count);
    }
}
