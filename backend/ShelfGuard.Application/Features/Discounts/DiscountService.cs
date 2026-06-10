using ShelfGuard.Application.Features.Discounts.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Discounts;

public sealed class DiscountService : IDiscountService
{
    private readonly IDiscountRepository _discounts;

    public DiscountService(IDiscountRepository discounts) => _discounts = discounts;

    // ── List ─────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<DiscountDto>> GetAllAsync(
        Guid tenantId,
        DiscountListQuery query,
        CancellationToken ct = default)
    {
        var list = await _discounts.GetAllAsync(tenantId, query.StoreId, query.Status, ct);
        return list.Select(ToDto).ToList();
    }

    // ── Get by id ─────────────────────────────────────────────────────────────

    public async Task<(DiscountDto? Discount, string? Error)> GetByIdAsync(
        Guid tenantId, Guid discountId, CancellationToken ct = default)
    {
        var d = await _discounts.GetByIdAsync(discountId, ct);
        if (d is null || d.TenantId != tenantId)
            return (null, "Discount not found.");
        return (ToDto(d), null);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    public async Task<(DiscountDto? Discount, string? Error)> CreateAsync(
        Guid tenantId, Guid createdBy,
        CreateDiscountRequest request,
        CancellationToken ct = default)
    {
        if (request.DiscountPercent is <= 0m or > 100m)
            return (null, "DiscountPercent must be between 0.01 and 100.");

        if (!DiscountReason.All.Contains(request.Reason))
            return (null, $"Invalid reason '{request.Reason}'. Allowed: {string.Join(", ", DiscountReason.All)}.");

        if (request.ValidUntil.HasValue && request.ValidUntil.Value <= DateTime.UtcNow)
            return (null, "ValidUntil must be in the future.");

        var discount = Discount.Create(
            tenantId:        tenantId,
            productId:       request.ProductId,
            storeId:         request.StoreId,
            discountPercent: request.DiscountPercent,
            reason:          request.Reason,
            productStockId:  request.ProductStockId,
            priceOriginal:   request.PriceOriginal,
            validFrom:       request.ValidFrom,
            validUntil:      request.ValidUntil,
            autoApplied:     false,
            createdBy:       createdBy);

        await _discounts.AddAsync(discount, ct);
        await _discounts.SaveChangesAsync(ct);
        return (ToDto(discount), null);
    }

    // ── Approve ───────────────────────────────────────────────────────────────

    public async Task<(DiscountDto? Discount, string? Error)> ApproveAsync(
        Guid tenantId, Guid discountId, Guid approvedBy,
        CancellationToken ct = default)
    {
        var d = await _discounts.GetByIdAsync(discountId, ct);
        if (d is null || d.TenantId != tenantId)
            return (null, "Discount not found.");

        try
        {
            d.Approve(approvedBy);
        }
        catch (InvalidOperationException ex)
        {
            return (null, ex.Message);
        }

        _discounts.Update(d);
        await _discounts.SaveChangesAsync(ct);
        return (ToDto(d), null);
    }

    // ── Cancel ────────────────────────────────────────────────────────────────

    public async Task<(DiscountDto? Discount, string? Error)> CancelAsync(
        Guid tenantId, Guid discountId,
        CancellationToken ct = default)
    {
        var d = await _discounts.GetByIdAsync(discountId, ct);
        if (d is null || d.TenantId != tenantId)
            return (null, "Discount not found.");

        try
        {
            d.Cancel();
        }
        catch (InvalidOperationException ex)
        {
            return (null, ex.Message);
        }

        _discounts.Update(d);
        await _discounts.SaveChangesAsync(ct);
        return (ToDto(d), null);
    }

    // ── Mapper ────────────────────────────────────────────────────────────────

    private static DiscountDto ToDto(Discount d) => new(
        d.Id, d.TenantId, d.ProductId, d.StoreId, d.ProductStockId,
        d.DiscountPercent, d.PriceOriginal, d.PriceDiscounted,
        d.Reason, d.ValidFrom, d.ValidUntil, d.Status, d.AutoApplied,
        d.CreatedBy, d.ApprovedBy, d.CreatedAt, d.ApprovedAt, d.WebhookSentAt
    );
}
