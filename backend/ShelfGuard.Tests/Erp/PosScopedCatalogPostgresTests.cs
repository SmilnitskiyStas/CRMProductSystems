using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Features.Pos.Dtos;
using ShelfGuard.Application.Features.Pricing;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Entities.Erp;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Tests.Pos;
using Xunit;

namespace ShelfGuard.Tests.Erp;
[Collection("ERP schema acceptance")]
public sealed class PosScopedCatalogPostgresTests(ErpSchemaFixture fixture)
{
    private sealed class Clock(DateTime at) : TimeProvider { public DateTime At = at; public override DateTimeOffset GetUtcNow() => new(At); }
    private sealed class Access(WorkAccessScope s) : IWorkAccessContext { public Task<WorkAccessScope?> GetAsync(CancellationToken ct = default) => Task.FromResult<WorkAccessScope?>(s); }
    private async Task<(PosInventoryPostingPostgresTests.Scenario S, Guid Register, Clock Clock)> Seed(bool many = false)
    {
        var s = await PosInventoryPostingPostgresTests.Seed(fixture);
        await using var db = fixture.Context();
        var register = new CashRegister { TenantId = s.S.Tenant, StoreId = s.S.Location, Number = 1, Name = "P3-B register" };
        db.Add(register); db.Entry(register).Property(r => r.Status).CurrentValue = CashRegister.StatusActive;
        var shift = await db.PosShifts.SingleAsync(x => x.Id == s.Shift); db.Entry(shift).Property(x => x.RegisterId).CurrentValue = register.Id;
        db.Add(new RegisterSession { TenantId = s.S.Tenant, RegisterId = register.Id, CashierId = s.S.Actor, StartedAt = DateTime.UtcNow.AddMinutes(-5) });
        if (many) for (var n = 0; n < 100; n++) db.Add(new Item { TenantId = s.S.Tenant, Name = "Paged " + n, PriceRetail = 1 });
        await db.SaveChangesAsync(); return (s, register.Id, new Clock(DateTime.UtcNow.AddSeconds(1)));
    }
    private PricingDesktopStore Pricing(PosInventoryPostingPostgresTests.Scenario s)
    { using var db = fixture.Context(); return new((DbContextOptions<AppDbContext>)db.GetService<IDbContextOptions>(), new Access(s.Scope), TimeProvider.System); }
    private async Task Plan(PosInventoryPostingPostgresTests.Scenario s, decimal price, DateTime? until = null, int priority = 10)
    {
        await using var db = fixture.Context(); var i = await db.Items.SingleAsync(i => i.Id == s.S.Item);
        var at = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await Pricing(s).SaveAsync(new(Guid.NewGuid(), null, null, "Reviewed scoped catalog test price",
            new("Scoped plan", s.S.Location, priority, true, at.AddDays(-1), until == null ? null : new DateTimeOffset(until.Value),
                [new(i.Id, price, CatalogDesktopStore.View(i).Fingerprint)])), default);
    }
    private PosScopedCatalog Catalog(AppDbContext db, PosInventoryPostingPostgresTests.Scenario s, Clock clock, bool planned = true)
    {
        var offline = new PosOfflinePricingGate(planned, [new(s.S.Tenant, s.S.Location)]);
        var resolver = new PosBasePriceResolver(db, new PosOnlinePricingGate(false, []), s.Source.Gate, new(true), offline);
        return new(db, PosScopedPriceTests.Proofs(), resolver, offline, s.Source.Gate, new(true), clock);
    }
    [ErpPostgresFact] public async Task Plan_change_without_item_update_invalidates_signed_pagination()
    {
        var (s, reg, clock) = await Seed(true); await Plan(s, 30);
        await using var db = fixture.Context(); var before = (await db.Items.AsNoTracking().SingleAsync(i => i.Id == s.S.Item)).UpdatedAt;
        var page = await Catalog(db, s, clock).ReadAsync(s.S.Tenant, reg, null, default);
        Assert.Equal(100, page.Items.Count); Assert.NotNull(page.Cursor);
        var next = await Catalog(db, s, clock).ReadAsync(s.S.Tenant, reg, page.Cursor, default); Assert.Single(next.Items);
        await Plan(s, 40);
        var error = await Assert.ThrowsAsync<PosPricingAuthorityException>(() => Catalog(db, s, clock).ReadAsync(s.S.Tenant, reg, page.Cursor, default));
        Assert.Equal("catalog_generation_changed", error.Code);
        Assert.Equal(before, (await db.Items.AsNoTracking().SingleAsync(i => i.Id == s.S.Item)).UpdatedAt);
    }
    [ErpPostgresFact] public async Task Known_boundary_limits_price_lease_and_clock_transition_changes_price_without_a_write()
    {
        var (s, reg, clock) = await Seed(); var end = new DateTime((clock.At.AddMinutes(1).Ticks / TimeSpan.TicksPerSecond) * TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        await Plan(s, 30, end); await using var db = fixture.Context();
        var first = await Catalog(db, s, clock).ReadAsync(s.S.Tenant, reg, null, default);
        var row = Assert.Single(first.Items); Assert.Equal(30, row.PriceRetail); Assert.Equal(end, row.PriceSnapshotExpiresAt);
        clock.At = end; var second = await Catalog(db, s, clock).ReadAsync(s.S.Tenant, reg, null, default);
        Assert.Equal(50, Assert.Single(second.Items).PriceRetail);
        Assert.Null(PosScopedPriceTests.Proofs().Read(row.PriceSnapshot!, s.S.Tenant, s.S.Location, reg, s.Barcode, end));
    }
    [ErpPostgresFact] public async Task Missing_price_is_unavailable_without_poisoning_other_catalog_items_and_foreign_register_is_denied()
    {
        var (s, reg, clock) = await Seed(); await using var db = fixture.Context();
        db.Add(new Item { TenantId = s.S.Tenant, Name = "Missing price" }); await db.SaveChangesAsync();
        var page = await Catalog(db, s, clock).ReadAsync(s.S.Tenant, reg, null, default);
        Assert.NotNull(page.Items.Single(i => i.Id == s.S.Item).PriceSnapshot);
        Assert.Null(page.Items.Single(i => i.Id != s.S.Item).PriceSnapshot);
        await Assert.ThrowsAsync<PosPricingAuthorityException>(() => Catalog(db, s, clock).ReadAsync(Guid.NewGuid(), reg, null, default));
    }
    [ErpPostgresFact] public async Task Paid_scoped_receipt_survives_price_change_and_pricing_rollback_with_archived_original_version()
    {
        var (s, reg, clock) = await Seed(); await Plan(s, 30); await using var db = fixture.Context();
        var row = Assert.Single((await Catalog(db, s, clock).ReadAsync(s.S.Tenant, reg, null, default)).Items);
        var proof = PosPromotionSnapshotTests.Service(); var promotions = proof.Issue(s.S.Tenant, s.S.Location, reg, null, [], clock.At) ?? throw new InvalidOperationException("Synthetic promotion lease missing.");
        var request = new CreateSaleRequest(s.Shift, [new(s.Barcode, 1, row.PriceSnapshot)], "Cash", 30,
            ClientTransactionId: Guid.NewGuid(), OccurredAt: clock.At.AddSeconds(1), ExpectedTotal: 30,
            PromotionSnapshot: promotions.Token, ExpectedPriceSegments: [new(0, 1, 30)]);
        await Plan(s, 90, priority: 0);
        Assert.Equal(90, Assert.Single((await Catalog(db, s, clock).ReadAsync(s.S.Tenant, reg, null, default)).Items).PriceRetail);
        var svc = PosInventoryPostingPostgresTests.Service(db, s, scopedPrices: PosScopedPriceTests.Proofs(), promotionProofs: proof);
        var result = await svc.CreateSalesBatchAsync(s.S.Tenant, s.S.Actor, new([request]), registerId: reg);
        var accepted = Assert.Single(result.Result!.Results); Assert.Equal("created", accepted.Status); Assert.Equal(30, accepted.Sale!.Subtotal);
        var snapshot = await db.Set<PosPricingSnapshot>().SingleAsync(a => a.TransactionId == accepted.Sale.TransactionId);
        var d = JsonSerializer.Deserialize<PricingArchivedDecision>(snapshot.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(30, d.Lines[0].BaseResolution!.Price); Assert.True(d.FrozenBase);
        var history = new PosPricingHistoryStore((DbContextOptions<AppDbContext>)db.GetService<IDbContextOptions>(), new Access(s.Scope));
        Assert.Equal("matched", (await history.SnapshotAsync(accepted.Sale.TransactionId, default)).Comparison.Outcome);
        var replay = await svc.CreateSalesBatchAsync(s.S.Tenant, s.S.Actor, new([request]), registerId: reg);
        Assert.Equal("duplicate", Assert.Single(replay.Result!.Results).Status);
    }
    [ErpPostgresFact] public async Task Desktop_reports_actual_configured_scope_and_live_readiness()
    {
        var (s, _, _) = await Seed(); using var db = fixture.Context();
        var store = new PricingDesktopStore((DbContextOptions<AppDbContext>)db.GetService<IDbContextOptions>(), new Access(s.Scope), TimeProvider.System,
            new PosOnlinePricingGate(false, []), new PosOfflinePricingGate(true, [new(s.S.Tenant, s.S.Location)]), s.Source.Gate, new(true));
        var access = await store.AccessAsync(default); Assert.Equal("scoped_pos", access.Mode);
        var scope = Assert.Single(access.PosScopes!); Assert.True(scope.OfflineEnabled); Assert.False(scope.OnlineEnabled); Assert.True(scope.Ready);
    }
}
