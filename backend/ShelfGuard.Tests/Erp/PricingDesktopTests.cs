using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using NSubstitute;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Entities.Erp;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Tests.Infrastructure;
using Xunit;
namespace ShelfGuard.Tests.Erp;
[Collection("ERP schema acceptance")]
public sealed class PricingDesktopTests(ErpSchemaFixture fixture)
{
    private static readonly DateTimeOffset At = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private sealed class Access(WorkAccessScope scope) : IWorkAccessContext { public Task<WorkAccessScope?> GetAsync(CancellationToken ct = default) => Task.FromResult<WorkAccessScope?>(scope); }
    private sealed class RuntimeRole(string role) : DbConnectionInterceptor
    {
        public override async Task ConnectionOpenedAsync(DbConnection c, ConnectionEndEventData e, CancellationToken ct = default) { await using var command = c.CreateCommand(); command.CommandText = "SET ROLE " + role; await command.ExecuteNonQueryAsync(ct); }
    }
    private DbContextOptions<AppDbContext> Options(params IInterceptor[] extra) => new DbContextOptionsBuilder<AppDbContext>(TestPostgres.Options(Environment.GetEnvironmentVariable("SHELFGUARD_ERP_SCHEMA_TEST_DB")!)).AddInterceptors([new RuntimeRole(fixture.ReaderRole), .. extra]).IgnoreManyServiceProvidersWarning().Options;
    private static WorkAccessScope Scope(InventoryFenceScenario s, bool wide = false) => new(s.Tenant, s.Actor, "store_manager", wide, [s.Location], new HashSet<string>(), new Dictionary<string, bool>());
    private PricingDesktopStore Store(InventoryFenceScenario s, bool wide = false, params IInterceptor[] extra) => new(Options(extra), new Access(Scope(s, wide)), TimeProvider.System);
    private async Task<PricingSave> Command(InventoryFenceScenario s, decimal price = 19.95m)
    {
        var catalog = new CatalogDesktopStore(Options(), new Access(Scope(s)), TimeProvider.System); var i = await catalog.ItemAsync(s.Item, default);
        return new(Guid.NewGuid(), null, null, "Reviewed synthetic planned retail price", new("Planned store prices", s.Location, 100, true, At.AddDays(-1), At.AddDays(1), [new(s.Item, price, i.Fingerprint)]));
    }
    private static PricingPreview Preview(InventoryFenceScenario s) => new(Guid.NewGuid(), s.Location, s.Item, At);

    [ErpPostgresFact] public async Task Planned_price_approval_is_atomic_and_does_not_change_checkout_stock_or_inventory_outbox()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var c = await Command(s); var store = Store(s); var saved = await store.SaveAsync(c, default); var view = await store.ListAsync(saved.ListKey, default); Assert.Equal(19.95m, Assert.Single(view.Lines).Price); Assert.Equal(1, view.Version);
        await using var db = fixture.Context(); Assert.Null((await db.Items.SingleAsync(i => i.Id == s.Item)).PriceRetail); Assert.Equal(3.25m, (await db.ProductStocks.SingleAsync(i => i.Id == s.Batch)).Quantity); Assert.Equal("approved", (await db.BusinessDocuments.SingleAsync(d => d.Id == saved.DocumentId)).Status); Assert.False(await db.DocumentPostings.AnyAsync(p => p.DocumentId == saved.DocumentId)); Assert.False(await db.OutboxEvents.AnyAsync(o => o.TenantId == s.Tenant && o.AggregateId == saved.DocumentId));
    }
    [ErpPostgresFact] public async Task Concurrent_same_command_replays_original_revision_and_changed_intent_is_rejected()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var c = await Command(s); var results = await Task.WhenAll(Store(s).SaveAsync(c, default), Store(s).SaveAsync(c, default)); Assert.Single(results, r => r.Replayed); Assert.Equal(results[0].DocumentId, results[1].DocumentId); Assert.Equal("pricing_command_conflict", (await Assert.ThrowsAsync<PricingConflict>(() => Store(s).SaveAsync(c with { Reason = "Another reviewed pricing reason" }, default))).Code);
    }
    [ErpPostgresFact] public async Task Complete_revision_preserves_history_and_stale_editor_cannot_overwrite_or_resurrect_old_price()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var c = await Command(s); var store = Store(s); await store.SaveAsync(c, default); var old = await store.ListAsync(c.OperationId, default); var next = c with { OperationId = Guid.NewGuid(), ListKey = old.ListKey, ExpectedFingerprint = old.Fingerprint, Data = c.Data with { IsActive = false } }; var result = await store.SaveAsync(next, default); Assert.Equal(2, result.Version); Assert.Null((await store.PreviewAsync(Preview(s), default)).Offer.PlannedPrice); Assert.Equal("pricing_changed", (await Assert.ThrowsAsync<PricingConflict>(() => store.SaveAsync(next with { OperationId = Guid.NewGuid() }, default))).Code); Assert.True((await store.SaveAsync(c, default)).Replayed); await using var db = fixture.Context(); Assert.Equal(2, await db.Set<PriceList>().CountAsync(p => p.TenantId == s.Tenant));
    }
    [ErpPostgresFact] public async Task Store_specific_precedes_tenant_wide_then_priority_then_logical_uuid_with_half_open_time()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var c = await Command(s); var store = Store(s, true); await store.SaveAsync(c with { OperationId = Guid.NewGuid(), Data = c.Data with { LocationId = null, Priority = 0, Lines = [c.Data.Lines[0] with { Price = 30 }] } }, default); await store.SaveAsync(c, default); var better = c with { OperationId = Guid.Parse("00000000-0000-4000-8000-000000000010"), Data = c.Data with { Priority = 50, Lines = [c.Data.Lines[0] with { Price = 20 }] } }; await store.SaveAsync(better, default); await store.SaveAsync(better with { OperationId = Guid.Parse("00000000-0000-4000-8000-000000000011"), Data = better.Data with { Lines = [c.Data.Lines[0] with { Price = 21 }] } }, default);
        Assert.Equal(20m, (await store.PreviewAsync(Preview(s), default)).Offer.PlannedPrice); var start = await store.PreviewAsync(Preview(s) with { At = c.Data.ValidFrom }, default); Assert.Equal(20m, start.Offer.PlannedPrice); Assert.Null((await store.PreviewAsync(Preview(s) with { At = c.Data.ValidUntil!.Value }, default)).Offer.PlannedPrice); Assert.Equal(c.Data.ValidUntil.Value.UtcDateTime, start.Offer.NextBoundary);
    }
    [ErpPostgresFact] public async Task New_future_revision_replaces_old_whole_list_instead_of_using_old_rows_until_start()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var c = await Command(s); var store = Store(s); await store.SaveAsync(c, default); var old = await store.ListAsync(c.OperationId, default); await store.SaveAsync(c with { OperationId = Guid.NewGuid(), ListKey = old.ListKey, ExpectedFingerprint = old.Fingerprint, Data = c.Data with { ValidFrom = At.AddHours(1) } }, default); var offer = (await store.PreviewAsync(Preview(s), default)).Offer; Assert.Null(offer.PlannedPrice); Assert.Equal(At.AddHours(1).UtcDateTime, offer.NextBoundary);
    }
    [ErpPostgresFact] public async Task Concurrent_preview_retries_and_read_archive_do_not_recalculate_after_price_change()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var store = Store(s); var c = await Command(s); await store.SaveAsync(c, default); var request = Preview(s); var snapshots = await Task.WhenAll(store.PreviewAsync(request, default), Store(s).PreviewAsync(request, default)); Assert.Single(snapshots, p => p.Replayed); var original = snapshots[0]; await using (var db = fixture.Context()) { (await db.Items.SingleAsync(i => i.Id == s.Item)).PriceRetail = 99; await db.SaveChangesAsync(); } var replay = await store.PreviewAsync(request, default); Assert.Equal(original.ContentHash, replay.ContentHash); Assert.Equal(System.Text.Json.JsonSerializer.Serialize(original.Offer), System.Text.Json.JsonSerializer.Serialize(replay.Offer)); Assert.Equal(original.ContentHash, (await store.SnapshotAsync(request.OperationId, default)).ContentHash); await Assert.ThrowsAsync<PricingConflict>(() => store.PreviewAsync(request with { At = At.AddSeconds(1) }, default));
    }
    [ErpPostgresFact] public async Task Missing_and_zero_prices_remain_distinct_and_preview_is_explicitly_base_only()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var store = Store(s); var missing = (await store.PreviewAsync(Preview(s), default)).Offer; Assert.Null(missing.PlannedPrice); Assert.Contains("missing_price", missing.Warnings); await store.SaveAsync(await Command(s, 0), default); var zero = (await store.PreviewAsync(Preview(s), default)).Offer; Assert.Equal(0, zero.PlannedPrice); Assert.Contains("zero_price", zero.Warnings); Assert.Contains("promotions_loyalty_and_basket_not_evaluated", zero.Warnings); Assert.Contains("not_for_retail_printing", zero.Warnings);
    }
    [ErpPostgresFact] public async Task Changed_item_snapshot_rejects_save_and_legacy_unit_change_blocks_planned_offer()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var c = await Command(s); await using (var db = fixture.Context()) { (await db.Items.SingleAsync(i => i.Id == s.Item)).Name = "Changed source"; await db.SaveChangesAsync(); } Assert.Equal("pricing_items_changed", (await Assert.ThrowsAsync<PricingConflict>(() => Store(s).SaveAsync(c, default))).Code); await Store(s).SaveAsync(await Command(s), default); await using (var db = fixture.Context()) { (await db.Items.SingleAsync(i => i.Id == s.Item)).Unit = "litre"; await db.SaveChangesAsync(); } var o = (await Store(s).PreviewAsync(Preview(s), default)).Offer; Assert.Null(o.PlannedPrice); Assert.Contains("unit_changed_requires_new_revision", o.Warnings);
    }
    [ErpPostgresFact] public async Task Live_denied_manager_cannot_save_or_replay_and_view_denial_blocks_preview()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var c = await Command(s); var store = Store(s); await store.SaveAsync(c, default); await using (var db = fixture.Context()) { (await db.Users.SingleAsync(u => u.Id == s.Actor)).SetPermissions(new() { ["pricing.manage"] = false }); await db.SaveChangesAsync(); } Assert.False((await store.AccessAsync(default)).CanManage); await Assert.ThrowsAsync<WorkAccessDeniedException>(() => store.SaveAsync(c, default)); await using (var db = fixture.Context()) { (await db.Users.SingleAsync(u => u.Id == s.Actor)).SetPermissions(new() { ["pricing.view"] = false }); await db.SaveChangesAsync(); } await Assert.ThrowsAsync<WorkAccessDeniedException>(() => store.PreviewAsync(Preview(s), default));
    }
    [ErpPostgresFact] public async Task Store_manager_cannot_write_global_or_other_store_and_cannot_move_list_from_other_scope()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var c = await Command(s); var another = new Location { TenantId = s.Tenant, Name = "Restricted location" }; await using (var db = fixture.Context()) { db.Add(another); await db.SaveChangesAsync(); } await Assert.ThrowsAsync<WorkAccessDeniedException>(() => Store(s).SaveAsync(c with { Data = c.Data with { LocationId = null } }, default)); await Assert.ThrowsAsync<WorkAccessDeniedException>(() => Store(s).SaveAsync(c with { Data = c.Data with { LocationId = another.Id } }, default)); var saved = await Store(s, true).SaveAsync(c with { Data = c.Data with { LocationId = another.Id } }, default); await Assert.ThrowsAsync<InventoryPostingNotFoundException>(() => Store(s).ListAsync(saved.ListKey, default)); var old = await Store(s, true).ListAsync(saved.ListKey, default); await Assert.ThrowsAsync<WorkAccessDeniedException>(() => Store(s).SaveAsync(c with { OperationId = Guid.NewGuid(), ListKey = old.ListKey, ExpectedFingerprint = old.Fingerprint }, default));
    }
    [ErpPostgresFact] public async Task Foreign_item_location_list_and_snapshot_are_not_visible_through_runtime_RLS()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var foreign = await InventoryFenceScenario.Seed(fixture); var c = await Command(s); var other = await Command(foreign); await Store(foreign).SaveAsync(other, default); var shot = await Store(foreign).PreviewAsync(Preview(foreign), default); await Assert.ThrowsAsync<PricingConflict>(() => Store(s).SaveAsync(c with { Data = c.Data with { Lines = other.Data.Lines } }, default)); await Assert.ThrowsAsync<InventoryPostingNotFoundException>(() => Store(s).ListAsync(other.OperationId, default)); await Assert.ThrowsAsync<InventoryPostingNotFoundException>(() => Store(s).SnapshotAsync(shot.Offer.SnapshotId, default)); await Assert.ThrowsAsync<WorkAccessDeniedException>(() => Store(s).PreviewAsync(Preview(s) with { LocationId = foreign.Location }, default));
        await using var db = new AppDbContext(Options()); await using var tx = await db.Database.BeginTransactionAsync(); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.tenant_id',{s.Tenant.ToString()},true)"); Assert.False(await db.Set<PriceList>().AnyAsync(p => p.TenantId == foreign.Tenant)); Assert.False(await db.Set<PriceResolutionSnapshot>().AnyAsync(p => p.TenantId == foreign.Tenant));
    }
    [ErpPostgresFact] public async Task Database_rejects_mutation_late_line_insertion_document_reopening_and_truncation()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var saved = await Store(s).SaveAsync(await Command(s), default); await Store(s).PreviewAsync(Preview(s), default);
        foreach (var sql in new[] { $"UPDATE public.price_lists SET \"Name\"='tampered' WHERE \"Id\"='{saved.Id}'", $"UPDATE public.price_list_items SET \"Price\"=0 WHERE \"PriceListId\"='{saved.Id}'", "DELETE FROM public.price_resolution_snapshots", $"UPDATE public.business_documents SET \"Status\"='submitted' WHERE \"Id\"='{saved.DocumentId}'", "TRUNCATE public.price_resolution_snapshots" })
        { await using var db = fixture.Context(); await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql)); }
        await using var ctx = fixture.Context(); var line = await ctx.Set<PriceListItem>().SingleAsync(l => l.PriceListId == saved.Id); ctx.Add(new PriceListItem { TenantId = s.Tenant, PriceListId = saved.Id, ItemId = line.ItemId, Price = 1, VatRate = line.VatRate, Unit = line.Unit, ItemName = line.ItemName, ItemFingerprint = line.ItemFingerprint }); await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }
    private sealed class FailApproval : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e, InterceptionResult<int> r, CancellationToken ct = default) { if (e.Context!.ChangeTracker.Entries<DocumentApproval>().Any(x => x.State == EntityState.Added)) throw new InvalidOperationException("Synthetic approval failure"); return ValueTask.FromResult(r); }
    }
    [ErpPostgresFact] public async Task Failed_approval_rolls_back_list_document_and_all_lines()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var c = await Command(s); await Assert.ThrowsAsync<InvalidOperationException>(() => Store(s, false, new FailApproval()).SaveAsync(c, default)); await using var db = fixture.Context(); Assert.False(await db.Set<PriceList>().AnyAsync(p => p.Id == c.OperationId)); Assert.False(await db.BusinessDocuments.AnyAsync(p => p.DocumentKey == c.OperationId));
    }
    [ErpPostgresFact] public async Task New_tables_have_FORCE_RLS_and_migration_preserves_legacy_facts()
    {
        await using var db = fixture.Context(); Assert.Equal(fixture.LegacyFingerprint, await fixture.FingerprintAsync(db)); var count = await db.Database.SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM pg_class WHERE relname IN ('price_lists','price_list_items','price_resolution_snapshots') AND relnamespace='public'::regnamespace AND relrowsecurity AND relforcerowsecurity").SingleAsync(); Assert.Equal(3, count);
    }
    [ErpPostgresFact] public async Task Deferred_seal_rejects_incomplete_unapproved_receipt_at_commit()
    {
        var s = await InventoryFenceScenario.Seed(fixture); await using var db = fixture.Context(); await using var tx = await db.Database.BeginTransactionAsync(); var id = Guid.NewGuid(); var doc = new BusinessDocument { TenantId = s.Tenant, DocumentKey = id, DocumentType = "price_plan", SourceSystem = "pricing.plan.v1", CreatedBy = s.Actor, ContentHash = new('a', 64) }; db.Add(doc); await db.SaveChangesAsync(); db.Add(new PriceList { Id = id, TenantId = s.Tenant, ListKey = id, Version = 1, DocumentId = doc.Id, Name = "Unapproved injection", LocationId = s.Location, Fingerprint = new('b', 64), CreatedBy = s.Actor, CreatedAt = At.UtcDateTime, ValidFrom = At.UtcDateTime }); await db.SaveChangesAsync(); var error = await Assert.ThrowsAsync<PostgresException>(() => tx.CommitAsync()); Assert.Equal("23514", error.SqlState);
    }
    [ErpPostgresFact] public async Task Stock_viewer_cannot_gain_manage_through_permission_and_disabled_inventory_denies_read()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var c = await Command(s); var viewer = User.Create(s.Tenant, Guid.NewGuid()+"@test.local", "Viewer", "hash", "storekeeper"); viewer.SetPermissions(new() { ["pricing.manage"] = true }); await using (var db = fixture.Context()) { db.AddRange(viewer, UserLocation.Create(s.Tenant, viewer.Id, s.Location, null)); await db.SaveChangesAsync(); } var scope = Scope(s) with { UserId = viewer.Id, Role = "storekeeper" }; var store = new PricingDesktopStore(Options(), new Access(scope), TimeProvider.System); Assert.False((await store.AccessAsync(default)).CanManage); await Assert.ThrowsAsync<WorkAccessDeniedException>(() => store.SaveAsync(c, default)); await using (var db = fixture.Context()) { (await db.Tenants.SingleAsync(t => t.Id == s.Tenant)).UpdateModules([]); await db.SaveChangesAsync(); } await Assert.ThrowsAsync<WorkAccessDeniedException>(() => store.ListsAsync(1, default));
    }
    [ErpPostgresFact] public async Task Recorded_planned_VAT_stays_explicit_after_legacy_tax_change_and_archive_stays_unchanged()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var c = await Command(s); var store = Store(s); await store.SaveAsync(c, default); var old = await store.PreviewAsync(Preview(s), default); await using (var db = fixture.Context()) { var i = await db.Items.SingleAsync(i => i.Id == s.Item); i.VatRate = 7; i.PriceRetail = 44; await db.SaveChangesAsync(); } var current = (await store.PreviewAsync(Preview(s), default)).Offer; Assert.Equal(20m, current.PlannedVatRate); Assert.Equal(7m, current.LegacyVatRate); Assert.Equal(44m, current.LegacyBasePrice); Assert.True(current.Differs); Assert.Equal(old.ContentHash, (await store.SnapshotAsync(old.Offer.SnapshotId, default)).ContentHash);
    }
    [ErpPostgresFact] public async Task Database_composite_foreign_keys_reject_cross_tenant_snapshot_even_with_maintenance_connection()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var foreign = await InventoryFenceScenario.Seed(fixture); await using var db = fixture.Context(); db.Add(new PriceResolutionSnapshot { Id = Guid.NewGuid(), TenantId = s.Tenant, CreatedBy = s.Actor, LocationId = s.Location, ItemId = foreign.Item, EvaluatedAt = At.UtcDateTime, CreatedAt = At.UtcDateTime, RequestHash = new('a', 64), ContentHash = new('b', 64), Payload = "{}" }); var e = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); Assert.Equal("23503", Assert.IsType<PostgresException>(e.InnerException).SqlState);
    }
    [Theory][InlineData(-1)][InlineData(0.001)][InlineData(10000000000)] public void Money_rejects_negative_inexact_or_overflow(decimal v) => Assert.Throws<ArgumentException>(() => PricingRules.Price(v));
    [Fact] public void Exact_zero_max_and_scale_are_canonical_and_invalid_period_or_duplicate_items_fail()
    {
        Assert.Equal(0m, PricingRules.Price(0)); Assert.Equal(9999999999.99m, PricingRules.Price(9999999999.99m)); var id = Guid.NewGuid(); var c = new PricingSave(Guid.NewGuid(), null, null, "Reviewed planned price", new("List", null, 100, true, At, null, [new(id, 1, new('a', 64))])); Assert.Equal(1m, Assert.Single(PricingRules.Normalize(c).Data.Lines).Price); Assert.Throws<ArgumentException>(() => PricingRules.Normalize(c with { Data = c.Data with { Lines = [null!] } })); Assert.Throws<ArgumentException>(() => PricingRules.Normalize(c with { Data = c.Data with { ValidUntil = At } })); Assert.Throws<ArgumentException>(() => PricingRules.Normalize(c with { Data = c.Data with { Lines = [c.Data.Lines[0], c.Data.Lines[0]] } }));
    }
    [Theory][InlineData("pricing.view")][InlineData("pricing.manage")]
    public async Task Pricing_overrides_can_be_managed_through_existing_user_service_with_audit(string permission)
    {
        var tenant = Guid.NewGuid(); var target = User.Create(tenant, "pricing-target@example.invalid", "Pricing manager", "hash", "store_manager");
        var users = Substitute.For<ShelfGuard.Domain.Interfaces.IUserRepository>(); var logs = Substitute.For<ShelfGuard.Domain.Interfaces.IActivityLogRepository>();
        users.GetByIdAsync(target.Id, default).Returns(target);
        var service = new ShelfGuard.Application.Features.Users.UserService(users, logs,
            Substitute.For<IPasswordHasher>(), Substitute.For<ShelfGuard.Application.Features.LegalEntities.ILegalEntityService>(),
            Substitute.For<ShelfGuard.Domain.Interfaces.IRefreshTokenRepository>(), Substitute.For<ShelfGuard.Domain.Interfaces.IUserPermissionGrantRepository>(),
            Substitute.For<ShelfGuard.Domain.Interfaces.ITenantRoleRepository>(), Substitute.For<ShelfGuard.Application.Features.Locations.ILocationService>(),
            Substitute.For<ShelfGuard.Domain.Interfaces.IUserLocationRepository>());
        var result = await service.UpdatePermissionsAsync(tenant, Guid.NewGuid(), "enterprise_admin", target.Id, new(new() { [permission] = false }));
        Assert.Null(result.Error); Assert.False(target.Permissions![permission]); await users.Received(1).SaveChangesAsync(default);
        await logs.Received(1).LogAsync(Arg.Is<ActivityLog>(l => l.Action == "user.permissions_updated" && l.TenantId == tenant), default);
    }
    [Fact] public async Task Pricing_capabilities_are_assignable_and_exposed_in_the_existing_role_catalog()
    {
        var tenant = Guid.NewGuid(); var repo = Substitute.For<ShelfGuard.Domain.Interfaces.ITenantRoleRepository>(); repo.GetByNameAsync(tenant, "Pricing", default).Returns((TenantRole?)null);
        var service = new ShelfGuard.Application.Features.TenantRoles.TenantRoleService(repo);
        var result = await service.CreateAsync(tenant, Guid.NewGuid(), new("Pricing", ["pricing.view", "pricing.manage"]));
        Assert.Null(result.Error); Assert.Contains("pricing.manage", result.Role!.Capabilities);
        Assert.Contains(ShelfGuard.Domain.Constants.TenantRoleCapabilities.Groups.SelectMany(g => g.Capabilities), c => c.Key == "pricing.view");
    }
}
