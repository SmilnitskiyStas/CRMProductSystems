using System.Data;
using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Features.Pos;
using ShelfGuard.Application.Features.Pricing;
using ShelfGuard.Application.Features.Registers;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Entities.Erp;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class PosScopedCatalog(AppDbContext db, IPosScopedPrices proofs, IPosBasePriceResolver prices,
    IPosOfflinePricingGate gate, IInventoryPostingGate inventoryGate, PosInventoryPostingOptions posOptions,
    TimeProvider clock) : IPosScopedCatalog
{
    public async Task<ScopedCatalogPage> ReadAsync(Guid tenant, Guid register, string? cursor, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var prior = cursor == null ? null : proofs.ReadCursor(cursor, tenant, register, now)
            ?? throw new PosPricingAuthorityException("catalog_cursor_expired");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY; SET LOCAL statement_timeout='15s'", ct);
        var reg = await db.CashRegisters.AsNoTracking().SingleOrDefaultAsync(r => r.Id == register && r.TenantId == tenant && r.Status == CashRegister.StatusActive, ct)
            ?? throw new PosPricingAuthorityException("catalog_register_unavailable");
        if (prior != null && prior.StoreId != reg.StoreId) throw new PosPricingAuthorityException("catalog_scope_changed");
        if (!await db.Locations.AnyAsync(l => l.Id == reg.StoreId && l.TenantId == tenant && l.IsActive, ct))
            throw new PosPricingAuthorityException("catalog_location_unavailable");
        var planned = gate.Allows(tenant, reg.StoreId);
        if (planned && (!inventoryGate.Allows(tenant, reg.StoreId) || !posOptions.Enabled
            || !await IntegratedScopeReadiness.Ready(db, tenant, reg.StoreId, ct)))
            throw new PosPricingAuthorityException("pricing_inventory_not_ready");
        if (await db.Items.CountAsync(i => i.TenantId == tenant, ct) > 20000) throw new PosPricingAuthorityException("catalog_limit");
        var catalogHash = await db.Database.SqlQuery<string>($"""
            SELECT encode(sha256(convert_to(coalesce(string_agg(encode(sha256(convert_to(to_jsonb(i)::text,'UTF8')),'hex'),'' ORDER BY i."Id"),''),'UTF8')),'hex') AS "Value"
            FROM public.items i WHERE i."TenantId"={tenant}
            """).SingleAsync(ct);
        var at = prior?.At ?? now;
        var heads = planned ? await db.Set<PriceList>().AsNoTracking().Where(p => p.TenantId == tenant
            && (p.LocationId == null || p.LocationId == reg.StoreId)
            && !db.Set<PriceList>().Any(n => n.TenantId == tenant && n.ListKey == p.ListKey && n.Version > p.Version))
            .OrderBy(p => p.Id).Take(1001).ToArrayAsync(ct) : [];
        if (heads.Length > 1000) throw new PosPricingAuthorityException("pricing_limit");
        var boundary = heads.Where(p => p.IsActive).SelectMany(p => new DateTime?[] { p.ValidFrom, p.ValidUntil })
            .Where(t => t > at).OrderBy(t => t).FirstOrDefault();
        var generation = CompatibilityBasket.Fingerprint(new { contract = "scoped-base.v1", tenant, reg.StoreId, register,
            planned, catalogHash, heads = heads.Select(p => new { p.Id, p.Fingerprint }), at });
        if (prior != null && prior.Generation != generation) throw new PosPricingAuthorityException("catalog_generation_changed");
        var query = db.Items.AsNoTracking().Where(i => i.TenantId == tenant);
        if (prior != null) query = query.Where(i => i.Id.CompareTo(prior.AfterId) > 0);
        var rows = await query.OrderBy(i => i.Id).Take(101).ToArrayAsync(ct); var page = rows.Take(100).ToArray();
        var active = page.Where(i => i.IsActive).ToArray();
        var decisions = planned && active.Length != 0 ? await prices.ResolveAvailableAsync(tenant, reg.StoreId, at,
            active.Select(i => new PosBasePriceItem(i.Id, i.Unit, i.PriceRetail, i.VatRate)).ToArray(), ct) : null;
        var items = page.Select(i => {
            PosBasePriceDecision? basis = null;
            var available = !planned || decisions != null && decisions.TryGetValue(i.Id, out basis);
            var price = available ? basis?.Price ?? i.PriceRetail : null; var vat = basis?.VatRate ?? i.VatRate;
            var proof = i.IsActive && price is >= 0 && vat is >= 0 and <= 100 ? proofs.Issue(
                new(i.Id, i.Name, i.Barcodes, price, vat, i.Unit, i.IsActive, i.UpdatedAt, i.CategoryId, tenant, i.PricePurchase),
                reg.StoreId, register, at, basis?.NextBoundary, basis) : null;
            return new SyncCatalogItemDto(i.Id, i.Name, i.Barcodes, price, vat, i.Unit, i.IsActive, i.UpdatedAt,
                i.CategoryId, proof?.Token, proof?.ExpiresAt);
        }).ToArray();
        var expires = prior?.ExpiresAt ?? at.AddMinutes(5); if (boundary is DateTime end && end < expires) expires = end;
        var next = rows.Length > 100 ? proofs.Cursor(new(tenant, reg.StoreId, register, generation, page[^1].Id, at, expires)) : null;
        await tx.CommitAsync(ct);
        return new("scoped-base.v1", tenant, reg.StoreId, register, generation, planned ? "planned" : "legacy", at, boundary, items, next);
    }
}
