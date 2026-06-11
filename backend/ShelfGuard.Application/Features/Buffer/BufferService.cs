using ShelfGuard.Application.Features.Adu;
using ShelfGuard.Application.Features.Buffer.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Buffer;

public sealed class BufferService : IBufferService
{
    private readonly IBufferRepository _repo;

    public BufferService(IBufferRepository repo) => _repo = repo;

    public async Task<(BufferDto? Buffer, string? Error)> GetAsync(
        Guid storeId, Guid productId, CancellationToken ct = default)
    {
        var buffer = await _repo.GetAsync(storeId, productId, ct);
        if (buffer is null)
            return (null, "Buffer not calculated for this product/store.");

        return (ToDto(buffer), null);
    }

    public async Task<(RecalculateBuffersResult? Result, string? Error)> RecalculateAsync(
        Guid tenantId, Guid storeId, CancellationToken ct = default)
    {
        if (!await _repo.StoreExistsAsync(storeId, ct))
            return (null, "Store not found.");

        var adus = await _repo.GetEffectiveAdusAsync(storeId, ct);
        if (adus.Count == 0)
            return (new RecalculateBuffersResult(storeId, 0, 0), null);

        var productIds = adus.Select(a => a.ProductId).ToList();
        var suppliers = await _repo.GetProductSuppliersAsync(productIds, ct);
        var schedules = await _repo.GetActiveSchedulesAsync(storeId, ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var windowStart = today.AddDays(-AduCalculator.MaxWindowDays);
        var sales = await _repo.GetSalesWindowAsync(storeId, productIds, windowStart, ct);
        var salesByProduct = sales.GroupBy(s => s.ProductId).ToDictionary(g => g.Key, g => g.ToList());

        var existing = await _repo.GetByStoreAsync(storeId, ct);
        var now = DateTime.UtcNow;
        int calculated = 0, skipped = 0;

        foreach (var adu in adus)
        {
            if (!suppliers.TryGetValue(adu.ProductId, out var supplierId)
                || !schedules.TryGetValue(supplierId, out var schedule))
            {
                skipped++;
                continue;
            }

            var (leadTime, orderCycle) = CdaBufferCalculator.CycleFromSchedule(schedule);

            salesByProduct.TryGetValue(adu.ProductId, out var productSales);
            var variability = CdaBufferCalculator.Variability(
                productSales ?? [], adu.ProductGroup ?? 1, today);

            var zones = CdaBufferCalculator.Compute(
                adu.AduEffective!.Value, leadTime, orderCycle, variability);

            if (existing.TryGetValue(adu.ProductId, out var row))
            {
                row.BufferGreen = zones.Green;
                row.BufferYellow = zones.Yellow;
                row.BufferRed = zones.Red;
                row.BufferTotal = zones.Total;
                row.LeadTimeDays = leadTime;
                row.OrderCycleDays = orderCycle;
                row.CalculatedAt = now;
            }
            else
            {
                await _repo.AddAsync(new ProductBuffer
                {
                    TenantId = tenantId,
                    StoreId = storeId,
                    ProductId = adu.ProductId,
                    BufferGreen = zones.Green,
                    BufferYellow = zones.Yellow,
                    BufferRed = zones.Red,
                    BufferTotal = zones.Total,
                    LeadTimeDays = leadTime,
                    OrderCycleDays = orderCycle,
                    CalculatedAt = now,
                }, ct);
            }

            calculated++;
        }

        await _repo.SaveChangesAsync(ct);
        return (new RecalculateBuffersResult(storeId, calculated, skipped), null);
    }

    private static BufferDto ToDto(ProductBuffer b) => new(
        b.StoreId, b.ProductId, b.Product?.Name,
        b.BufferTotal, b.BufferGreen, b.BufferYellow, b.BufferRed,
        b.LeadTimeDays, b.OrderCycleDays, b.CalculatedAt);
}

/// <summary>
/// Pure CDA buffer math (v2-spec §2). Side-effect-free for unit testing.
///
///   Green  = ADU × (LT + OC)        — demand over the full replenishment cycle
///   Yellow = ADU × OC × variability — reserve for demand unevenness
///   Red    = ADU × LT × safety      — safety stock for unexpected events
///   Total  = Green + Yellow + Red
///
/// LT/OC derive from the supply schedule at calculation time (rule 3):
///   OC = 7 / deliveries per week; LT = schedule.OrderLeadDays (default 1).
/// Variability = coefficient of variation (σ/μ) of valid-day sales over the
/// product group's ADU window, clamped to [0.2, 1.5]. Safety factor: 1.0
/// (per-tenant setting planned; spec range 0.5–1.5).
/// </summary>
internal static class CdaBufferCalculator
{
    internal const decimal DefaultSafetyFactor = 1.0m;
    internal const decimal MinVariability = 0.2m;
    internal const decimal MaxVariability = 1.5m;
    private const decimal DefaultLeadTimeDays = 1m;

    internal sealed record BufferZones(decimal Green, decimal Yellow, decimal Red, decimal Total);

    internal static BufferZones Compute(
        decimal adu, decimal leadTime, decimal orderCycle,
        decimal variability, decimal safetyFactor = DefaultSafetyFactor)
    {
        var green = Math.Round(adu * (leadTime + orderCycle), 2);
        var yellow = Math.Round(adu * orderCycle * variability, 2);
        var red = Math.Round(adu * leadTime * safetyFactor, 2);
        return new BufferZones(green, yellow, red, green + yellow + red);
    }

    internal static (decimal LeadTime, decimal OrderCycle) CycleFromSchedule(SupplySchedule schedule)
    {
        var deliveriesPerWeek = Math.Max(1, schedule.DayOfWeek.Count);
        var orderCycle = Math.Round(7m / deliveriesPerWeek, 1);
        var leadTime = schedule.OrderLeadDays is > 0 ? schedule.OrderLeadDays.Value : DefaultLeadTimeDays;
        return (leadTime, orderCycle);
    }

    /// <summary>CV of valid-day sales over the group's window, clamped to [0.2, 1.5].</summary>
    internal static decimal Variability(IReadOnlyList<DailySale> sales, short productGroup, DateOnly today)
    {
        var windowDays = productGroup switch { 3 => 30, 2 => 60, _ => 90 };
        var from = today.AddDays(-windowDays);

        var quantities = sales
            .Where(AduCalculator.IsValidDay)
            .Where(s => s.Date < today && s.Date >= from)
            .Select(s => (double)s.QuantitySold)
            .ToList();

        if (quantities.Count < 2)
            return MinVariability;

        var mean = quantities.Average();
        if (mean <= 0)
            return MinVariability;

        var variance = quantities.Sum(q => (q - mean) * (q - mean)) / quantities.Count;
        var cv = (decimal)(Math.Sqrt(variance) / mean);

        return Math.Clamp(Math.Round(cv, 2), MinVariability, MaxVariability);
    }
}
