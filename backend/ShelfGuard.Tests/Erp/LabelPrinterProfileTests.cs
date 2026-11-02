using System.Data.Common;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
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
public sealed class LabelPrinterProfileTests(ErpSchemaFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private sealed class Access(WorkAccessScope s) : IWorkAccessContext { public Task<WorkAccessScope?> GetAsync(CancellationToken ct = default) => Task.FromResult<WorkAccessScope?>(s); }
    private sealed class RuntimeRole(string role) : DbConnectionInterceptor
    {
        public override async Task ConnectionOpenedAsync(DbConnection c, ConnectionEndEventData e, CancellationToken ct = default) { await using var command = c.CreateCommand(); command.CommandText = "SET ROLE " + role; await command.ExecuteNonQueryAsync(ct); }
    }
    private DbContextOptions<AppDbContext> Options() => new DbContextOptionsBuilder<AppDbContext>(TestPostgres.Options(Environment.GetEnvironmentVariable("SHELFGUARD_ERP_SCHEMA_TEST_DB")!)).AddInterceptors(new RuntimeRole(fixture.ReaderRole)).IgnoreManyServiceProvidersWarning().Options;
    private static WorkAccessScope Scope(InventoryFenceScenario s) => new(s.Tenant, s.Actor, "store_manager", false, [s.Location], new HashSet<string>(), new Dictionary<string, bool>());
    private LabelPrinterProfileStore Profiles(InventoryFenceScenario s) => new(Options(), new Access(Scope(s)), TimeProvider.System);
    private LabelJobArchiveStore Archive(InventoryFenceScenario s) => new(Options(), new Access(Scope(s)), TimeProvider.System);
    private static LabelPrinterSave Command(InventoryFenceScenario s) => new(Guid.NewGuid(), null, null, s.Location, new("Synthetic ZPL", "zpl", 203, 58, 40, true, "simulation"));
    private async Task Grant(InventoryFenceScenario s, bool value = true) { await using var db = fixture.Context(); (await db.Users.SingleAsync(u => u.Id == s.Actor)).SetPermissions(new() { ["labels.printers.manage"] = value }); await db.SaveChangesAsync(); }
    [Fact] public void Physical_transports_unknown_protocols_and_unknown_properties_are_rejected()
    {
        var d = new LabelPrinterData("Test", "zpl", 203, 58, 40, true, "simulation");
        foreach (var bad in new[] { d with { Transport = "tcp" }, d with { Protocol = "tspl" }, d with { Dpi = 600 }, d with { WidthMm = 29 }, d with { HeightMm = 297.001m } }) Assert.Throws<ArgumentException>(() => LabelPrinterRules.Normalize(bad));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<LabelPrinterData>("{\"name\":\"Test\",\"protocol\":\"zpl\",\"dpi\":203,\"widthMm\":58,\"heightMm\":40,\"isActive\":true,\"transport\":\"simulation\",\"ip\":\"127.0.0.1\"}", Json));
        Assert.NotNull(typeof(LabelPrinterProfilesController).GetMethod("Save")!.GetCustomAttributes(typeof(RequireRecentMfaAttribute), true).SingleOrDefault());
    }
    [Fact] public void V2_JS_golden_binding_parses_and_legacy_v1_hash_remains_unchanged()
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "label-job-profile.v2.json")); var job = LabelJobArchiveRules.Parse(new(Guid.NewGuid(), text)); Assert.Equal("label-job.v2", job.JobProtocol); Assert.NotNull(job.Printer);
        Assert.Equal("label-job.v1", LabelJobArchiveRules.Parse(new(Guid.NewGuid(), File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "label-job-archive.v1.json")))).JobProtocol);
    }
    [ErpPostgresFact] public async Task Management_requires_explicit_grant_and_revocation_blocks_exact_replay()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var c = Command(s); await Assert.ThrowsAsync<WorkAccessDeniedException>(() => Profiles(s).SaveAsync(c, default)); await Grant(s);
        var p = await Profiles(s).SaveAsync(c, default); Assert.False(p.Replayed); Assert.True((await Profiles(s).SaveAsync(c, default)).Replayed); await Grant(s, false); await Assert.ThrowsAsync<WorkAccessDeniedException>(() => Profiles(s).SaveAsync(c, default)); Assert.Single((await Profiles(s).ListAsync(s.Location, 1, default)).Items);
    }
    [ErpPostgresFact] public async Task Concurrent_save_replays_original_profile_and_changed_intent_conflicts()
    {
        var s = await InventoryFenceScenario.Seed(fixture); await Grant(s); var c = Command(s); var r = await Task.WhenAll(Profiles(s).SaveAsync(c, default), Profiles(s).SaveAsync(c, default)); Assert.Single(r, v => v.Replayed);
        await Assert.ThrowsAsync<PricingConflict>(() => Profiles(s).SaveAsync(c with { Data = c.Data with { Dpi = 300 } }, default));
    }
    [ErpPostgresFact] public async Task Immutable_revision_chain_rejects_stale_edits_and_preserves_deactivation_history()
    {
        var s = await InventoryFenceScenario.Seed(fixture); await Grant(s); var p = (await Profiles(s).SaveAsync(Command(s), default)).Revision; var next = new LabelPrinterSave(Guid.NewGuid(), p.ProfileKey, 1, s.Location, p.Data with { IsActive = false });
        Assert.Equal(2, (await Profiles(s).SaveAsync(next, default)).Revision.Version); await Assert.ThrowsAsync<PricingConflict>(() => Profiles(s).SaveAsync(next with { OperationId = Guid.NewGuid() }, default)); Assert.Equal(new[] { 2, 1 }, (await Profiles(s).HistoryAsync(s.Location, p.ProfileKey, 1, default)).Items.Select(v => v.Version)); Assert.True((await Profiles(s).RevisionAsync(s.Location, p.Id, default)).Data.IsActive);
    }
    [ErpPostgresFact] public async Task Foreign_tenant_and_unassigned_location_cannot_read_edit_or_move_profiles()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var f = await InventoryFenceScenario.Seed(fixture); await Grant(s); var p = (await Profiles(s).SaveAsync(Command(s), default)).Revision;
        await Assert.ThrowsAsync<WorkAccessDeniedException>(() => Profiles(f).RevisionAsync(s.Location, p.Id, default)); await Assert.ThrowsAsync<InventoryPostingNotFoundException>(() => Profiles(f).RevisionAsync(f.Location, p.Id, default));
        var other = new Location { TenantId = s.Tenant, Name = "Other store" }; await using (var db = fixture.Context()) { db.Add(other); await db.SaveChangesAsync(); }
        await Assert.ThrowsAsync<WorkAccessDeniedException>(() => Profiles(s).SaveAsync(new(Guid.NewGuid(), p.ProfileKey, 1, other.Id, p.Data), default));
    }
    private static JsonObject BoundJob(InventoryFenceScenario s, LabelPrinterView p)
    {
        var n = LabelJobArchiveTests.Job(s); n["protocol"] = "label-job.v2";
        var binding = JsonSerializer.SerializeToNode(new { revisionId = p.Id.ToString(), profileKey = p.ProfileKey.ToString(), version = p.Version, locationId = p.LocationId.ToString(), contentHash = p.ContentHash, data = p.Data, documentHash = new string('d', 64) }, Json)!.AsObject();
        binding["bindingHash"] = LabelJobArchiveRules.Hash(LabelJobArchiveRules.Canonical(JsonSerializer.SerializeToElement(binding, Json))); n["printer"] = binding;
        SignBound(n); return n;
    }
    private static void SignBound(JsonObject n)
    {
        var p = n["printer"]!.AsObject(); var copy = (JsonObject)p.DeepClone(); copy.Remove("bindingHash"); p["bindingHash"] = LabelJobArchiveRules.Hash(LabelJobArchiveRules.Canonical(JsonSerializer.SerializeToElement(copy, Json)));
        n["commandHash"] = LabelJobArchiveRules.Hash(JsonSerializer.Serialize(new { protocol = "label-job.v2", id = n["requestId"]!.GetValue<string>(), renderHash = n["render"]!["contentHash"]!.GetValue<string>(), copies = n["copies"]!.GetValue<int>(), requiresBulk = n["requiresBulk"]!.GetValue<bool>(), scenario = n["scenario"]!.GetValue<string>(), batchId = n["batchId"]!.GetValue<string>(), artifactHash = n["artifactHash"]!.GetValue<string>(), printerHash = p["bindingHash"]!.GetValue<string>() }));
    }
    [ErpPostgresFact] public async Task Profile_bound_archive_is_hidden_from_v1_clients_and_historical_binding_survives_deactivation()
    {
        var s = await InventoryFenceScenario.Seed(fixture); await Grant(s); var p = (await Profiles(s).SaveAsync(Command(s), default)).Revision; var n = BoundJob(s, p); var command = LabelJobArchiveTests.Upload(n); await Archive(s).UploadAsync(command, default);
        Assert.Empty((await Archive(s).ListAsync(s.Location, 1, false, default)).Items); await Assert.ThrowsAsync<InventoryPostingNotFoundException>(() => Archive(s).GetAsync(s.Location, LabelJobArchiveTests.Id(n), default)); Assert.Single((await Archive(s).ListAsync(s.Location, 1, false, default, 2)).Items);
        await Profiles(s).SaveAsync(new(Guid.NewGuid(), p.ProfileKey, 1, s.Location, p.Data with { IsActive = false }), default); Assert.True((await Archive(s).UploadAsync(command, default)).Replayed); Assert.NotNull((await Archive(s).GetAsync(s.Location, LabelJobArchiveTests.Id(n), default, 2)).Payload);
    }
    [ErpPostgresFact] public async Task Forged_profile_media_document_binding_or_revision_is_rejected()
    {
        var s = await InventoryFenceScenario.Seed(fixture); await Grant(s); var p = (await Profiles(s).SaveAsync(Command(s), default)).Revision; var n = BoundJob(s, p);
        n["printer"]!["data"]!["dpi"] = 300; SignBound(n); await Assert.ThrowsAsync<ArgumentException>(() => Archive(s).UploadAsync(LabelJobArchiveTests.Upload(n), default));
        n = BoundJob(s, p); n["printer"]!["revisionId"] = Guid.NewGuid().ToString(); SignBound(n); await Assert.ThrowsAsync<PricingConflict>(() => Archive(s).UploadAsync(LabelJobArchiveTests.Upload(n), default));
        n = BoundJob(s, p); n["printer"]!["documentHash"] = new string('e', 64); await Assert.ThrowsAsync<ArgumentException>(() => Archive(s).UploadAsync(LabelJobArchiveTests.Upload(n), default));
    }
    [ErpPostgresFact] public async Task Database_profile_rows_are_append_only_and_force_RLS_hides_foreign_tenant()
    {
        var s = await InventoryFenceScenario.Seed(fixture); await Grant(s); var p = (await Profiles(s).SaveAsync(Command(s), default)).Revision;
        foreach (var sql in new[] { "DELETE FROM label_printer_profile_revisions", "TRUNCATE label_printer_profile_revisions CASCADE", "UPDATE label_printer_profile_revisions SET \"Version\"=5" }) { await using var db = fixture.Context(); await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql)); }
        var f = await InventoryFenceScenario.Seed(fixture); await using (var db = new AppDbContext(Options())) { await using var tx = await db.Database.BeginTransactionAsync(); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.tenant_id',{f.Tenant.ToString()},true)"); Assert.False(await db.Set<LabelPrinterRevision>().AnyAsync(x => x.TenantId == s.Tenant)); }
    }
}
