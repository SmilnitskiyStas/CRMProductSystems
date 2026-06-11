using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Cannibalization;

public sealed record CannibalizationRowDto(
    Guid Id,
    Guid AffectedProductId,
    string ProductName,
    decimal OrderCoefficient,
    string Source,
    bool IsApplied,
    bool IsPromoProduct
);

public sealed record UpdateCannibalizationRequest(decimal OrderCoefficient);

public sealed record ApplyResult(Guid DiscountId, int RowsApplied);

public interface ICannibalizationService
{
    /// <summary>Returns suggestions for the discount; generates them on first call (v2-spec §5).</summary>
    Task<(List<CannibalizationRowDto>? Rows, string? Error)> GetOrGenerateAsync(
        Guid discountId, CancellationToken ct = default);

    Task<(CannibalizationRowDto? Row, string? Error)> UpdateAsync(
        Guid id, UpdateCannibalizationRequest request, CancellationToken ct = default);

    Task<(ApplyResult? Result, string? Error)> ApplyAsync(Guid discountId, CancellationToken ct = default);
}

public sealed class CannibalizationService : ICannibalizationService
{
    internal const decimal PromoProductCoefficient = 2.0m;   // spec §5: 2.0..2.5
    internal const decimal SiblingCoefficient = 0.7m;        // spec §5: 0.6..0.7

    private readonly ICannibalizationRepository _repo;

    public CannibalizationService(ICannibalizationRepository repo) => _repo = repo;

    public async Task<(List<CannibalizationRowDto>? Rows, string? Error)> GetOrGenerateAsync(
        Guid discountId, CancellationToken ct = default)
    {
        var discount = await _repo.GetDiscountAsync(discountId, ct);
        if (discount is null)
            return (null, "Discount not found.");

        var existing = await _repo.GetByDiscountAsync(discountId, ct);
        if (existing.Count == 0)
        {
            // Auto-generation: the promo product gets a boost; same-segment
            // neighbours in the same store lose demand to it.
            await _repo.AddAsync(new PromoCannibalization
            {
                TenantId = discount.TenantId,
                DiscountId = discountId,
                AffectedProductId = discount.ProductId,
                OrderCoefficient = PromoProductCoefficient,
            }, ct);

            var promoProduct = await _repo.GetDiscountProductAsync(discountId, ct);
            if (promoProduct?.SegmentId is not null)
            {
                var siblings = await _repo.GetSegmentSiblingsAsync(
                    promoProduct.SegmentId.Value, discount.ProductId, ct);

                foreach (var sibling in siblings)
                {
                    await _repo.AddAsync(new PromoCannibalization
                    {
                        TenantId = discount.TenantId,
                        DiscountId = discountId,
                        AffectedProductId = sibling.Id,
                        OrderCoefficient = SiblingCoefficient,
                    }, ct);
                }
            }

            await _repo.SaveChangesAsync(ct);
            existing = await _repo.GetByDiscountAsync(discountId, ct);
        }

        var rows = existing
            .Select(r => ToDto(r, r.AffectedProductId == discount.ProductId))
            .OrderByDescending(r => r.IsPromoProduct)
            .ThenBy(r => r.ProductName)
            .ToList();

        return (rows, null);
    }

    public async Task<(CannibalizationRowDto? Row, string? Error)> UpdateAsync(
        Guid id, UpdateCannibalizationRequest request, CancellationToken ct = default)
    {
        if (request.OrderCoefficient is <= 0 or > 10)
            return (null, "OrderCoefficient must be between 0.01 and 10.");

        var row = await _repo.GetByIdAsync(id, ct);
        if (row is null)
            return (null, "Cannibalization row not found.");

        row.OrderCoefficient = request.OrderCoefficient;
        row.Source = "manual";
        await _repo.SaveChangesAsync(ct);

        var discount = await _repo.GetDiscountAsync(row.DiscountId, ct);
        return (ToDto(row, discount?.ProductId == row.AffectedProductId), null);
    }

    public async Task<(ApplyResult? Result, string? Error)> ApplyAsync(
        Guid discountId, CancellationToken ct = default)
    {
        var discount = await _repo.GetDiscountAsync(discountId, ct);
        if (discount is null)
            return (null, "Discount not found.");

        var rows = await _repo.GetByDiscountAsync(discountId, ct);
        if (rows.Count == 0)
            return (null, "No cannibalization suggestions to apply. Call GET first.");

        foreach (var row in rows)
            row.IsApplied = true;

        await _repo.SaveChangesAsync(ct);
        return (new ApplyResult(discountId, rows.Count), null);
    }

    private static CannibalizationRowDto ToDto(PromoCannibalization r, bool isPromo) => new(
        r.Id, r.AffectedProductId, r.AffectedProduct?.Name ?? "",
        r.OrderCoefficient, r.Source, r.IsApplied, isPromo);
}
