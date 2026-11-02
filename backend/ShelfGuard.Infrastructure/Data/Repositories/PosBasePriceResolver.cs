using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Features.Pricing;
using ShelfGuard.Domain.Entities.Erp;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>Read-only, bounded source capture. All list revisions for a basket are read in one SQL snapshot.</summary>
public sealed class PosBasePriceResolver(AppDbContext db, IPosOnlinePricingGate pricingGate,
    IInventoryPostingGate inventoryGate, PosInventoryPostingOptions posOptions, IPosOfflinePricingGate? offlineGate = null) : IPosBasePriceResolver
{
    public bool IsEnabled(Guid tenant, Guid location) => pricingGate.Allows(tenant, location);

    public Task<IReadOnlyDictionary<Guid, PosBasePriceDecision>> ResolveAsync(Guid tenant, Guid location,
        DateTime at, IReadOnlyList<PosBasePriceItem> items, CancellationToken ct = default) => Resolve(tenant, location, at, items, false, ct);
    public Task<IReadOnlyDictionary<Guid, PosBasePriceDecision>> ResolveAvailableAsync(Guid tenant, Guid location,
        DateTime at, IReadOnlyList<PosBasePriceItem> items, CancellationToken ct = default) => Resolve(tenant, location, at, items, true, ct);
    private async Task<IReadOnlyDictionary<Guid, PosBasePriceDecision>> Resolve(Guid tenant, Guid location,
        DateTime at, IReadOnlyList<PosBasePriceItem> items, bool availableOnly, CancellationToken ct)
    {
        if (!IsEnabled(tenant, location) && offlineGate?.Allows(tenant, location) != true) throw new PosPricingAuthorityException("pricing_scope_disabled");
        if (!inventoryGate.Allows(tenant, location) || !posOptions.Enabled
            || !await IntegratedScopeReadiness.Ready(db, tenant, location, ct))
            throw new PosPricingAuthorityException("pricing_inventory_not_ready");
        if (at.Kind != DateTimeKind.Utc || items.Count is < 1 or > 100
            || items.Any(i => i.ItemId == Guid.Empty) || items.DistinctBy(i => i.ItemId).Count() != items.Count)
            throw new PosPricingAuthorityException("pricing_context_invalid");
        if (!await db.Locations.AnyAsync(l => l.TenantId == tenant && l.Id == location && l.IsActive, ct))
            throw new PosPricingAuthorityException("pricing_location_unavailable");
        var ids = items.Select(i => i.ItemId).ToArray();
        if (await db.Items.CountAsync(i => i.TenantId == tenant && ids.Contains(i.Id) && i.IsActive, ct) != ids.Length)
            throw new PosPricingAuthorityException("pricing_items_unavailable");
        // Latest includes inactive/future revisions when masking their older versions.
        var heads = db.Set<PriceList>().AsNoTracking().Where(p => p.TenantId == tenant
            && !db.Set<PriceList>().Any(n => n.TenantId == tenant && n.ListKey == p.ListKey && n.Version > p.Version)
            && p.IsActive && (p.LocationId == null || p.LocationId == location));
        var choices = await heads.Join(db.Set<PriceListItem>().AsNoTracking()
                .Where(l => l.TenantId == tenant && ids.Contains(l.ItemId)), p => p.Id, l => l.PriceListId,
                (p, l) => new BasePriceCandidate(p, l)).Take(10001).ToArrayAsync(ct);
        if (choices.Length > 10000) throw new PosPricingAuthorityException("pricing_limit");
        var result = new Dictionary<Guid, PosBasePriceDecision>();
        foreach (var item in items)
        {
            try
            {
            var selection = PlannedBasePricing.Select(tenant, location, item.ItemId, at, choices);
            var selected = selection.Selected;
            if (selected != null && (selected.Line.Unit != item.Unit || selected.List.Currency != "UAH"))
                throw new PosPricingAuthorityException("pricing_source_incompatible");
            var price = selected?.Line.Price ?? item.LegacyPrice;
            var vat = selected?.Line.VatRate ?? item.LegacyVat;
            if (price == null) throw new PosPricingAuthorityException("pricing_missing_price");
            if (price < 0 || price > 9999999999.99m || decimal.Round(price.Value, 2) != price || vat is < 0 or > 100
                || item.LegacyPrice < 0)
                throw new PosPricingAuthorityException("pricing_source_invalid");
            result.Add(item.ItemId, new(item.ItemId, PricingRules.Policy,
                selected == null ? "legacy_item_base" : "planned_price_list", price.Value, vat,
                selected?.List.Id, selected?.List.ListKey, selected?.List.Version, selected?.List.Fingerprint,
                at, selection.NextBoundary, item.LegacyPrice, item.LegacyVat));
            }
            catch (PosPricingAuthorityException e) when (availableOnly && e.Code is "pricing_source_incompatible" or "pricing_missing_price" or "pricing_source_invalid")
            { /* Visible item, unavailable price: never silently substitute a different source. */ }
        }
        return result;
    }
}
