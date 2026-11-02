using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Entities.Erp;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Infrastructure.Authorization;
using ShelfGuard.Api.Controllers;
using ShelfGuard.Tests.Infrastructure;
using Xunit;
namespace ShelfGuard.Tests.Erp;

[Collection("ERP schema acceptance")]
public sealed class LabelTemplateTests(ErpSchemaFixture fixture)
{
    private sealed class Access(WorkAccessScope scope) : IWorkAccessContext { public Task<WorkAccessScope?> GetAsync(CancellationToken ct = default) => Task.FromResult<WorkAccessScope?>(scope); }
    private sealed class RuntimeRole(string role) : DbConnectionInterceptor
    {
        public override async Task ConnectionOpenedAsync(DbConnection c, ConnectionEndEventData e, CancellationToken ct = default) { await using var command = c.CreateCommand(); command.CommandText = "SET ROLE " + role; await command.ExecuteNonQueryAsync(ct); }
    }
    private DbContextOptions<AppDbContext> Options() => new DbContextOptionsBuilder<AppDbContext>(TestPostgres.Options(Environment.GetEnvironmentVariable("SHELFGUARD_ERP_SCHEMA_TEST_DB")!)).AddInterceptors(new RuntimeRole(fixture.ReaderRole)).IgnoreManyServiceProvidersWarning().Options;
    private LabelTemplateStore Store(InventoryFenceScenario s, bool wide = true) => new(Options(), new Access(new(s.Tenant, s.Actor, "store_manager", wide, [s.Location], new HashSet<string>(), new Dictionary<string, bool>())), TimeProvider.System);
    private static LabelLayout Layout() => new("Test template", 58, 40, 203, [new("name", "name", "", 2, 5, 54, 10, 3, true), new("price", "price", "", 2, 15, 54, 15, 8, true)]);
    private static LabelTemplateSave Command() => new(Guid.NewGuid(), null, null, Layout());

    [Fact] public void Rules_preserve_normalized_layout_and_keep_only_supported_fields()
    {
        var c = Command(); Assert.Equal("Test template", LabelTemplateRules.Normalize(c with { Data = c.Data with { Name = " Test template " } }).Data.Name);
        Assert.Equal("", LabelTemplateRules.Normalize(Layout() with { Elements = [Layout().Elements[0] with { Text = "discarded" }] }).Elements[0].Text);
    }
    [Theory] [InlineData(29, 40)] [InlineData(58, 19)] [InlineData(210.001, 40)] [InlineData(58, 298)]
    public void Rules_reject_outside_dimensions(double w, double h) => Assert.Throws<ArgumentException>(() => LabelTemplateRules.Normalize(Layout() with { WidthMm = (decimal)w, HeightMm = (decimal)h }));
    [Fact] public void Rules_reject_bad_geometry_duplicate_ids_and_raw_content()
    {
        foreach (var e in new[] { Layout().Elements[0] with { X = 40 }, Layout().Elements[0] with { Y = 1 }, Layout().Elements[0] with { FontMm = 11 }, Layout().Elements[0] with { Field = "svg" }, Layout().Elements[0] with { Id = "<script>" } })
            Assert.Throws<ArgumentException>(() => LabelTemplateRules.Normalize(Layout() with { Elements = [e] }));
        Assert.Throws<ArgumentException>(() => LabelTemplateRules.Normalize(Layout() with { Elements = [Layout().Elements[0], Layout().Elements[0]] }));
        Assert.Throws<ArgumentException>(() => LabelTemplateRules.Normalize(Command() with { ExpectedVersion = 1 }));
        Assert.Throws<ArgumentException>(() => LabelTemplateRules.Normalize(new LabelAssignmentSave(Guid.Empty, Guid.NewGuid(), null, null)));
    }
    [Fact] public void Both_write_endpoints_require_recent_mfa_and_controller_requires_inventory()
    {
        Assert.NotNull(typeof(LabelTemplatesController).GetMethod("Save")!.GetCustomAttributes(typeof(RequireRecentMfaAttribute), true).SingleOrDefault());
        Assert.NotNull(typeof(LabelTemplatesController).GetMethod("Assign")!.GetCustomAttributes(typeof(RequireRecentMfaAttribute), true).SingleOrDefault());
        Assert.NotNull(typeof(LabelTemplatesController).GetCustomAttributes(typeof(RequireModuleAttribute), true).SingleOrDefault());
    }
    [ErpPostgresFact] public async Task Concurrent_create_replays_exact_revision_and_changed_intent_conflicts()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var c = Command(); var results = await Task.WhenAll(Store(s).SaveAsync(c, default), Store(s).SaveAsync(c, default));
        Assert.Single(results, r => r.Replayed); Assert.Equal(results[0].Revision.Id, results[1].Revision.Id);
        Assert.Equal("label_operation_conflict", (await Assert.ThrowsAsync<PricingConflict>(() => Store(s).SaveAsync(c with { Data = c.Data with { Name = "Changed" } }, default))).Code);
    }
    [ErpPostgresFact] public async Task Concurrent_edit_has_one_winner_and_preserves_history()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var first = await Store(s).SaveAsync(Command(), default);
        var next = Command() with { TemplateKey = first.Revision.TemplateKey, ExpectedVersion = 1 };
        async Task<bool> Attempt(LabelTemplateSave c) { try { await Store(s).SaveAsync(c, default); return true; } catch (PricingConflict) { return false; } }
        var results = await Task.WhenAll(Attempt(next), Attempt(next with { OperationId = Guid.NewGuid() })); Assert.Single(results, b => b);
        var history = await Store(s).HistoryAsync(first.Revision.TemplateKey, 1, default); Assert.Equal(new[] { 2, 1 }, history.Items.Select(r => r.Version));
        Assert.Equal(2, Assert.Single((await Store(s).ListAsync(1, default)).Items).Version);
        Assert.Equal(1, (await Store(s).RevisionAsync(first.Revision.Id, default)).Version);
    }
    [ErpPostgresFact] public async Task Assignment_pins_revision_until_explicit_replace_or_remove_and_replays()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var first = await Store(s).SaveAsync(Command(), default); var c = new LabelAssignmentSave(Guid.NewGuid(), s.Location, null, first.Revision.Id);
        var assigned = await Store(s, false).AssignAsync(c, default);
        await Store(s).SaveAsync(Command() with { TemplateKey = first.Revision.TemplateKey, ExpectedVersion = 1 }, default);
        Assert.Equal(first.Revision.Id, (await Store(s).AssignmentAsync(s.Location, default))!.RevisionId);
        Assert.True((await Store(s, false).AssignAsync(c, default)).Replayed);
        await Assert.ThrowsAsync<PricingConflict>(() => Store(s).AssignAsync(c with { OperationId = Guid.NewGuid() }, default));
        var removed = await Store(s).AssignAsync(new(Guid.NewGuid(), s.Location, assigned.Assignment.Id, null), default); Assert.Null(removed.Assignment.RevisionId); Assert.Equal(2, removed.Assignment.Version);
    }
    [ErpPostgresFact] public async Task Store_scope_can_assign_but_cannot_edit_library_or_other_store()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var other = await InventoryFenceScenario.Seed(fixture); var r = await Store(s).SaveAsync(Command(), default);
        Assert.False((await Store(s, false).AccessAsync(default)).CanEditTemplates);
        await Assert.ThrowsAsync<WorkAccessDeniedException>(() => Store(s, false).SaveAsync(Command(), default));
        await Assert.ThrowsAsync<WorkAccessDeniedException>(() => Store(s, false).AssignAsync(new(Guid.NewGuid(), other.Location, null, r.Revision.Id), default));
        await Assert.ThrowsAsync<WorkAccessDeniedException>(() => Store(s, false).AssignmentAsync(other.Location, default));
    }
    [ErpPostgresFact] public async Task Live_labels_permissions_are_independent_from_pricing_and_revocation_blocks_replay()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var c = Command(); await Store(s).SaveAsync(c, default);
        await using (var db = fixture.Context()) { (await db.Users.SingleAsync(u => u.Id == s.Actor)).SetPermissions(new() { ["pricing.manage"] = false, ["labels.templates.manage"] = false }); await db.SaveChangesAsync(); }
        Assert.False((await Store(s).AccessAsync(default)).CanEditTemplates); await Assert.ThrowsAsync<WorkAccessDeniedException>(() => Store(s).SaveAsync(c, default));
        await using (var db = fixture.Context()) { (await db.Users.SingleAsync(u => u.Id == s.Actor)).SetPermissions(new() { ["pricing.manage"] = false, ["labels.templates.manage"] = true }); await db.SaveChangesAsync(); }
        Assert.True((await Store(s).AccessAsync(default)).CanEditTemplates); Assert.True((await Store(s).SaveAsync(c, default)).Replayed);
        await using (var db = fixture.Context()) { (await db.Users.SingleAsync(u => u.Id == s.Actor)).SetPermissions(new() { ["labels.preview"] = false }); await db.SaveChangesAsync(); }
        await Assert.ThrowsAsync<WorkAccessDeniedException>(() => Store(s).ListAsync(1, default));
    }
    [ErpPostgresFact] public async Task Simulator_print_is_independent_and_bulk_requires_explicit_live_grant()
    {
        var s = await InventoryFenceScenario.Seed(fixture);
        var initial = await Store(s).AccessAsync(default); Assert.True(initial.CanPrint); Assert.False(initial.CanBulk);
        await using (var db = fixture.Context()) { (await db.Users.SingleAsync(u => u.Id == s.Actor)).SetPermissions(new() { ["labels.templates.manage"] = false, ["labels.print"] = true, ["labels.bulk"] = true }); await db.SaveChangesAsync(); }
        var enabled = await Store(s).AccessAsync(default); Assert.True(enabled.CanPrint); Assert.True(enabled.CanBulk); Assert.False(enabled.CanEditTemplates);
        await using (var db = fixture.Context()) { (await db.Users.SingleAsync(u => u.Id == s.Actor)).SetPermissions(new() { ["labels.print"] = false, ["labels.bulk"] = true }); await db.SaveChangesAsync(); }
        var denied = await Store(s).AccessAsync(default); Assert.False(denied.CanPrint); Assert.False(denied.CanBulk); Assert.True(denied.CanPreview);
    }
    [ErpPostgresFact] public async Task Bulk_role_capability_is_live_and_explicit_user_denial_overrides_it()
    {
        var s = await InventoryFenceScenario.Seed(fixture);
        var role = TenantRole.Create(s.Tenant, "Label test bulk", ["labels.bulk"], s.Actor);
        await using (var db = fixture.Context()) { db.Add(role); (await db.Users.SingleAsync(u => u.Id == s.Actor)).SetTenantRole(role.Id); await db.SaveChangesAsync(); }
        Assert.True((await Store(s).AccessAsync(default)).CanBulk);
        await using (var db = fixture.Context()) { (await db.Users.SingleAsync(u => u.Id == s.Actor)).SetPermissions(new() { ["labels.bulk"] = false }); await db.SaveChangesAsync(); }
        Assert.False((await Store(s).AccessAsync(default)).CanBulk);
    }
    [ErpPostgresFact] public async Task Foreign_tenant_revisions_are_hidden_and_composite_fk_rejects_cross_tenant_assignment()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var other = await InventoryFenceScenario.Seed(fixture); var r = await Store(other).SaveAsync(Command(), default);
        Assert.Empty((await Store(s).HistoryAsync(r.Revision.TemplateKey, 1, default)).Items);
        await Assert.ThrowsAsync<InventoryPostingNotFoundException>(() => Store(s).RevisionAsync(r.Revision.Id, default));
        await Assert.ThrowsAsync<InventoryPostingNotFoundException>(() => Store(s).AssignAsync(new(Guid.NewGuid(), s.Location, null, r.Revision.Id), default));
        await using (var db = new AppDbContext(Options())) { await using var tx = await db.Database.BeginTransactionAsync(); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.tenant_id',{s.Tenant.ToString()},true)"); Assert.False(await db.Set<LabelTemplateRevision>().AnyAsync(x => x.TenantId == other.Tenant)); }
        await using (var db = fixture.Context()) { db.Add(new LabelStoreAssignment { Id = Guid.NewGuid(), TenantId = s.Tenant, LocationId = s.Location, Version = 1, RevisionId = r.Revision.Id, CreatedBy = s.Actor, CreatedAt = DateTime.UtcNow, RequestHash = new('a', 64) }); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); }
    }
    [ErpPostgresFact] public async Task Database_guards_block_mutation_truncate_and_skipped_versions_without_stock_writes()
    {
        var s = await InventoryFenceScenario.Seed(fixture); int outboxBefore;
        await using (var db = fixture.Context()) { outboxBefore = await db.OutboxEvents.CountAsync(x => x.TenantId == s.Tenant); }
        var r = await Store(s).SaveAsync(Command(), default); var a = await Store(s).AssignAsync(new(Guid.NewGuid(), s.Location, null, r.Revision.Id), default);
        foreach (var sql in new[] { $"UPDATE label_template_revisions SET \"Payload\"='null' WHERE \"Id\"='{r.Revision.Id}'", $"DELETE FROM label_store_assignments WHERE \"Id\"='{a.Assignment.Id}'", "TRUNCATE label_template_revisions CASCADE", "TRUNCATE label_store_assignments" })
        { await using var db = fixture.Context(); await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql)); }
        await using (var db = fixture.Context()) { var original = await db.Set<LabelTemplateRevision>().AsNoTracking().SingleAsync(x => x.Id == r.Revision.Id); original.Id = Guid.NewGuid(); original.Version = 4; db.Add(original); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); }
        await using (var db = fixture.Context()) { Assert.Equal(3.25m, (await db.ProductStocks.SingleAsync(x => x.Id == s.Batch)).Quantity); Assert.Equal(outboxBefore, await db.OutboxEvents.CountAsync(x => x.TenantId == s.Tenant)); Assert.Equal(1, await db.Set<LabelTemplateRevision>().CountAsync(x => x.TenantId == s.Tenant)); }
    }
}
