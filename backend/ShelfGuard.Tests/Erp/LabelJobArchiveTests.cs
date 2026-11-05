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
using ShelfGuard.Tests.Infrastructure;
using Xunit;
namespace ShelfGuard.Tests.Erp;
[Collection("ERP schema acceptance")]
public sealed class LabelJobArchiveTests(ErpSchemaFixture fixture)
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private sealed class Access(WorkAccessScope s) : IWorkAccessContext { public Task<WorkAccessScope?> GetAsync(CancellationToken ct = default) => Task.FromResult<WorkAccessScope?>(s); }
    private sealed class RuntimeRole(string role) : DbConnectionInterceptor
    {
        public override async Task ConnectionOpenedAsync(DbConnection c, ConnectionEndEventData e, CancellationToken ct = default) { await using var command = c.CreateCommand(); command.CommandText = "SET ROLE " + role; await command.ExecuteNonQueryAsync(ct); }
    }
    private DbContextOptions<AppDbContext> Options() => new DbContextOptionsBuilder<AppDbContext>(TestPostgres.Options(Environment.GetEnvironmentVariable("SHELFGUARD_ERP_SCHEMA_TEST_DB")!)).AddInterceptors(new RuntimeRole(fixture.ReaderRole)).IgnoreManyServiceProvidersWarning().Options;
    private static WorkAccessScope Scope(InventoryFenceScenario s) => new(s.Tenant, s.Actor, "store_manager", false, [s.Location], new HashSet<string>(), new Dictionary<string, bool>());
    private LabelJobArchiveStore Store(InventoryFenceScenario s) => new(Options(), new Access(Scope(s)), TimeProvider.System);
    private static string Golden() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "label-job-archive.v1.json"));
    private static JsonObject Node() => JsonNode.Parse(Golden())!.AsObject();
    internal static string Raw(JsonNode n) => n.ToJsonString(Wire);
    internal static Guid Id(JsonObject n) => Guid.Parse(n["requestId"]!.GetValue<string>());
    internal static LabelJobUpload Upload(JsonObject n, Guid? op = null) => new(op ?? Guid.NewGuid(), Raw(n));
    internal static void Sign(JsonObject n)
    {
        var render = n["render"]!; var context = render["context"]!; var template = context["template"]!;
        if (template["kind"]!.GetValue<string>() == "local") template["contentHash"] = LabelJobArchiveRules.Hash(Raw(template["data"]!));
        render["contentHash"] = LabelJobArchiveRules.Hash(Raw(context));
        n["commandHash"] = LabelJobArchiveRules.Hash(JsonSerializer.Serialize(new { protocol = "label-job.v1", id = n["requestId"]!.GetValue<string>(), renderHash = render["contentHash"]!.GetValue<string>(), copies = n["copies"]!.GetValue<int>(), requiresBulk = n["requiresBulk"]!.GetValue<bool>(), scenario = n["scenario"]!.GetValue<string>(), batchId = n["batchId"]!.GetValue<string>(), artifactHash = n["artifactHash"]!.GetValue<string>() }));
        if (n["receipt"] is { } receipt) { receipt["commandHash"] = n["commandHash"]!.DeepClone(); receipt["artifactHash"] = n["artifactHash"]!.DeepClone(); receipt["copies"] = n["copies"]!.DeepClone(); receipt["requestId"] = n["requestId"]!.DeepClone(); }
    }
    internal static JsonObject Job(InventoryFenceScenario s)
    {
        var n = Node(); n["requestId"] = Guid.NewGuid().ToString(); var c = n["render"]!["context"]!; c["tenantId"] = s.Tenant.ToString(); c["actorId"] = s.Actor.ToString(); c["locationId"] = s.Location.ToString(); Sign(n); return n;
    }
    internal static void Event(JsonObject n, string status, int attempt, string? detail = null)
    {
        var events = n["events"]!.AsArray(); events.Add(new JsonObject { ["seq"] = events.Count + 1, ["status"] = status, ["at"] = $"2026-11-01T10:{events.Count:00}:00.000Z", ["attempt"] = attempt, ["detail"] = detail ?? (status == "running" ? "simulator_dispatch" : status == "unknown" ? "adapter_result_unknown" : status == "succeeded" ? "simulator_accepted" : "simulator_offline") }); n["status"] = status; n["attempts"] = attempt;
        if (status == "succeeded") n["receipt"] = new JsonObject { ["requestId"] = n["requestId"]!.DeepClone(), ["status"] = "accepted", ["adapter"] = "label-simulator.v1", ["commandHash"] = n["commandHash"]!.DeepClone(), ["artifactHash"] = n["artifactHash"]!.DeepClone(), ["copies"] = n["copies"]!.DeepClone(), ["acceptedAt"] = "2026-11-01T10:02:00.000Z" };
    }
    [Fact] public void Original_JS_golden_hashes_and_Unicode_parse_without_reserialization()
    {
        var j = LabelJobArchiveRules.Parse(new(Guid.NewGuid(), Golden())); Assert.Equal("Тестовий макет Ї Є Ґ", j.Layout.Name); Assert.Single(j.Events); Assert.Equal("queued", j.Events[0].Status);
    }
    [Fact] public void Noncanonical_encoding_cannot_poison_a_Desktop_readable_archive()
    {
        Assert.Throws<ArgumentException>(() => LabelJobArchiveRules.Parse(new(Guid.NewGuid(), " " + Golden())));
        Assert.Throws<ArgumentException>(() => LabelJobArchiveRules.Parse(new(Guid.NewGuid(), Golden().Replace("Тестовий", "\\u0422естовий"))));
        Assert.Throws<ArgumentException>(() => LabelJobArchiveRules.Parse(new(Guid.NewGuid(), Golden().Replace("\"copies\":1", "\"copies\":1.0"))));
        var n = Node(); n["render"]!["context"]!["template"]!["data"]!["name"] = "  Unnormalized  "; Sign(n); Assert.Throws<ArgumentException>(() => LabelJobArchiveRules.Parse(Upload(n)));
    }
    [Theory] [InlineData("commandHash")] [InlineData("artifactHash")] [InlineData("copies")] [InlineData("requestId")]
    public void Changed_unsigned_intent_is_rejected(string field)
    {
        var n = Node(); n[field] = field == "copies" ? JsonValue.Create(2) : JsonValue.Create(field == "requestId" ? Guid.NewGuid().ToString() : new string('b', 64)); Assert.Throws<ArgumentException>(() => LabelJobArchiveRules.Parse(Upload(n)));
    }
    [Fact] public void Unknown_fields_duplicate_properties_bad_transition_and_missing_receipt_are_rejected()
    {
        var n = Node(); n["printerAddress"] = "127.0.0.1"; Assert.Throws<ArgumentException>(() => LabelJobArchiveRules.Parse(Upload(n)));
        Assert.Throws<ArgumentException>(() => LabelJobArchiveRules.Parse(new(Guid.NewGuid(), Golden().Replace("\"protocol\":\"label-job.v1\"", "\"protocol\":\"label-job.v1\",\"protocol\":\"label-job.v1\""))));
        n = Node(); Event(n, "succeeded", 1); Assert.Throws<ArgumentException>(() => LabelJobArchiveRules.Parse(Upload(n)));
        n = Node(); Event(n, "running", 1); Event(n, "succeeded", 1); n.Remove("receipt"); Assert.Throws<ArgumentException>(() => LabelJobArchiveRules.Parse(Upload(n)));
        Assert.Throws<ArgumentException>(() => LabelJobArchiveRules.Parse(new(Guid.NewGuid(), new string('x', 24001))));
    }
    [ErpPostgresFact] public async Task Concurrent_exact_upload_replays_one_transaction_and_records_server_received_time()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var c = Upload(Job(s)); var result = await Task.WhenAll(Store(s).UploadAsync(c, default), Store(s).UploadAsync(c, default)); Assert.Single(result, r => r.Replayed);
        await using var db = fixture.Context(); Assert.Equal(1, await db.Set<LabelArchivedJob>().CountAsync(j => j.TenantId == s.Tenant)); Assert.Equal(1, await db.Set<LabelArchivedJobUpload>().CountAsync(j => j.TenantId == s.Tenant)); Assert.Equal(result[0].ReceivedAt, result[1].ReceivedAt);
    }
    [ErpPostgresFact] public async Task Checkpoints_append_events_preserve_old_prefix_and_never_rewind_server_head()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var n = Job(s); var first = Upload(n); await Store(s).UploadAsync(first, default); Event(n, "running", 1); Event(n, "unknown", 1); await Store(s).UploadAsync(Upload(n), default); Event(n, "succeeded", 1, "receipt_reconciled"); var last = Upload(n); await Store(s).UploadAsync(last, default);
        var old = await Store(s).UploadAsync(first with { OperationId = Guid.NewGuid() }, default); Assert.Equal(1, old.AcceptedEvents); Assert.Equal(4, old.ServerEvents); Assert.Equal("succeeded", old.ServerStatus);
        Assert.True((await Store(s).UploadAsync(first, default)).Replayed); var view = await Store(s).GetAsync(s.Location, Id(n), default); Assert.Equal(4, view.EventCount); Assert.Equal(Raw(n), view.Payload); Assert.Equal("desktop_client_reported", view.Origin); Assert.True(view.DevelopmentOnly);
        await using var db = fixture.Context(); Assert.Equal(4, await db.Set<LabelArchivedJobEvent>().CountAsync(e => e.TenantId == s.Tenant)); Assert.Single(await db.Set<LabelArchivedJobReceipt>().Where(r => r.TenantId == s.Tenant).ToArrayAsync());
    }
    [ErpPostgresFact] public async Task Changed_operation_id_payload_or_immutable_intent_conflicts_without_partial_writes()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var n = Job(s); var c = Upload(n); await Store(s).UploadAsync(c, default); Event(n, "running", 1);
        Assert.Equal("label_job_operation_conflict", (await Assert.ThrowsAsync<PricingConflict>(() => Store(s).UploadAsync(Upload(n, c.OperationId), default))).Code);
        n["copies"] = 2; n["requiresBulk"] = true; Sign(n); await using (var db = fixture.Context()) { (await db.Users.SingleAsync(u => u.Id == s.Actor)).SetPermissions(new() { ["labels.bulk"] = true }); await db.SaveChangesAsync(); }
        Assert.Equal("label_job_intent_conflict", (await Assert.ThrowsAsync<PricingConflict>(() => Store(s).UploadAsync(Upload(n), default))).Code); Assert.Equal(1, (await Store(s).GetAsync(s.Location, Id(n), default)).EventCount);
    }
    [ErpPostgresFact] public async Task Concurrent_divergent_history_has_one_winner_and_rejects_the_other_prefix()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var n = Job(s); await Store(s).UploadAsync(Upload(n), default); var a = (JsonObject)n.DeepClone(); var b = (JsonObject)n.DeepClone(); Event(a, "running", 1); Event(b, "cancelled", 0, "user_cancelled");
        async Task<bool> Attempt(JsonObject job) { try { await Store(s).UploadAsync(Upload(job), default); return true; } catch (PricingConflict) { return false; } }
        Assert.Single(await Task.WhenAll(Attempt(a), Attempt(b)), v => v); Assert.Equal(2, (await Store(s).GetAsync(s.Location, Id(n), default)).EventCount);
    }
    [ErpPostgresFact] public async Task Tenant_actor_location_and_explicit_audit_scope_are_enforced()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var foreign = await InventoryFenceScenario.Seed(fixture); var n = Job(s); await Store(s).UploadAsync(Upload(n), default);
        await Assert.ThrowsAsync<WorkAccessDeniedException>(() => Store(foreign).UploadAsync(Upload(n), default)); await Assert.ThrowsAsync<WorkAccessDeniedException>(() => Store(foreign).GetAsync(s.Location, Id(n), default)); await Assert.ThrowsAsync<WorkAccessDeniedException>(() => Store(s).ListAsync(s.Location, 1, true, default));
        var other = User.Create(s.Tenant, Guid.NewGuid()+"@test.local", "Other reporter", "hash", "store_manager"); await using (var db = fixture.Context()) { db.Add(other); db.Add(UserLocation.Create(s.Tenant, other.Id, s.Location, null)); await db.SaveChangesAsync(); }
        var os = s with { Actor = other.Id }; Assert.Empty((await Store(os).ListAsync(s.Location, 1, false, default)).Items); await Assert.ThrowsAsync<InventoryPostingNotFoundException>(() => Store(os).GetAsync(s.Location, Id(n), default));
        await using (var db = fixture.Context()) { (await db.Users.SingleAsync(u => u.Id == other.Id)).SetPermissions(new() { ["labels.audit"] = true }); await db.SaveChangesAsync(); }
        Assert.Single((await Store(os).ListAsync(s.Location, 1, true, default)).Items); Assert.Equal(s.Actor, (await Store(os).GetAsync(s.Location, Id(n), default)).CreatedBy);
        var another = new Location { TenantId = s.Tenant, Name = "Not assigned" }; await using (var db = fixture.Context()) { db.Add(another); await db.SaveChangesAsync(); } await Assert.ThrowsAsync<WorkAccessDeniedException>(() => Store(os).ListAsync(another.Id, 1, true, default));
    }
    [ErpPostgresFact] public async Task Fresh_print_bulk_and_audit_revocation_also_apply_to_replays()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var n = Job(s); n["requiresBulk"] = true; n["copies"] = 2; Sign(n); var c = Upload(n); await Assert.ThrowsAsync<WorkAccessDeniedException>(() => Store(s).UploadAsync(c, default));
        await using (var db = fixture.Context()) { (await db.Users.SingleAsync(u => u.Id == s.Actor)).SetPermissions(new() { ["labels.bulk"] = true, ["labels.audit"] = true }); await db.SaveChangesAsync(); }
        await Store(s).UploadAsync(c, default); Assert.Single((await Store(s).ListAsync(s.Location, 1, true, default)).Items);
        await using (var db = fixture.Context()) { (await db.Users.SingleAsync(u => u.Id == s.Actor)).SetPermissions(new() { ["labels.bulk"] = true, ["labels.print"] = false, ["labels.audit"] = false }); await db.SaveChangesAsync(); }
        await Assert.ThrowsAsync<WorkAccessDeniedException>(() => Store(s).UploadAsync(c, default)); await Assert.ThrowsAsync<WorkAccessDeniedException>(() => Store(s).ListAsync(s.Location, 1, true, default)); Assert.Single((await Store(s).ListAsync(s.Location, 1, false, default)).Items);
    }
    [ErpPostgresFact] public async Task Server_template_must_match_immutable_revision_layout_and_hash()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var n = Job(s); var parsed = LabelJobArchiveRules.Parse(Upload(n)); var templates = new LabelTemplateStore(Options(), new Access(Scope(s) with { TenantWide = true }), TimeProvider.System); var t = (await templates.SaveAsync(new(Guid.NewGuid(), null, null, parsed.Layout), default)).Revision;
        var node = n["render"]!["context"]!["template"]!; node["kind"] = "server"; node["revisionId"] = t.Id.ToString(); node["templateKey"] = t.TemplateKey.ToString(); node["contentHash"] = t.ContentHash; Sign(n); await Store(s).UploadAsync(Upload(n), default);
        n["requestId"] = Guid.NewGuid().ToString(); node["data"]!["name"] = "Forged layout"; Sign(n); await Assert.ThrowsAsync<PricingConflict>(() => Store(s).UploadAsync(Upload(n), default));
    }
    [ErpPostgresFact] public async Task Price_binding_must_equal_own_archived_snapshot_and_live_price_denial_hides_it()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var pricing = new PricingDesktopStore(Options(), new Access(Scope(s)), TimeProvider.System);
        var item = await new CatalogDesktopStore(Options(), new Access(Scope(s)), TimeProvider.System).ItemAsync(s.Item, default);
        var at = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        await pricing.SaveAsync(new(Guid.NewGuid(), null, null, "Reviewed synthetic archive price", new("Archive source", s.Location, 1, true, at.AddDays(-1), null, [new(s.Item, 28.75m, item.Fingerprint)])), default);
        var snapshot = await pricing.PreviewAsync(new(Guid.NewGuid(), s.Location, s.Item, at), default);
        var node = JsonSerializer.SerializeToNode(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web) { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.WriteAsString })!;
        var offer = node["offer"]!; node["replayed"] = true; offer["version"] = snapshot.Offer.Version;
        offer["plannedPrice"] = snapshot.Offer.PlannedPrice!.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        offer["plannedVatRate"] = snapshot.Offer.PlannedVatRate.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        offer["legacyVatRate"] = snapshot.Offer.LegacyVatRate.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        offer["at"] = snapshot.Offer.At.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
        if (snapshot.Offer.LegacyBasePrice is decimal legacy) offer["legacyBasePrice"] = legacy.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        if (snapshot.Offer.NextBoundary is DateTime boundary) offer["nextBoundary"] = boundary.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
        var n = Job(s); n["render"]!["context"]!["price"] = node; Sign(n); var command = Upload(n); await Store(s).UploadAsync(command, default);
        var tampered = (JsonObject)n.DeepClone(); tampered["requestId"] = Guid.NewGuid().ToString(); tampered["render"]!["context"]!["price"]!["offer"]!["plannedPrice"] = "0.01"; Sign(tampered);
        Assert.Equal("label_job_price_source", (await Assert.ThrowsAsync<PricingConflict>(() => Store(s).UploadAsync(Upload(tampered), default))).Code);
        var numeric = (JsonObject)n.DeepClone(); numeric["requestId"] = Guid.NewGuid().ToString(); numeric["render"]!["context"]!["price"]!["offer"]!["plannedPrice"] = 28.75m; Sign(numeric);
        await Assert.ThrowsAsync<PricingConflict>(() => Store(s).UploadAsync(Upload(numeric), default));
        var other = User.Create(s.Tenant, Guid.NewGuid()+"@test.local", "Foreign price owner", "hash", "store_manager");
        await using (var db = fixture.Context()) { db.Add(other); db.Add(UserLocation.Create(s.Tenant, other.Id, s.Location, null)); await db.SaveChangesAsync(); }
        var os = s with { Actor = other.Id }; var foreign = (JsonObject)n.DeepClone(); foreign["requestId"] = Guid.NewGuid().ToString(); foreign["render"]!["context"]!["actorId"] = other.Id.ToString(); Sign(foreign);
        await Assert.ThrowsAsync<PricingConflict>(() => Store(os).UploadAsync(Upload(foreign), default));
        await using (var db = fixture.Context()) { (await db.Users.SingleAsync(u => u.Id == s.Actor)).SetPermissions(new() { ["pricing.view"] = false }); await db.SaveChangesAsync(); }
        Assert.Empty((await Store(s).ListAsync(s.Location, 1, false, default)).Items); await Assert.ThrowsAsync<InventoryPostingNotFoundException>(() => Store(s).GetAsync(s.Location, Id(n), default)); await Assert.ThrowsAsync<WorkAccessDeniedException>(() => Store(s).UploadAsync(command, default));
    }
    [ErpPostgresFact] public async Task Inactive_store_retains_readable_audit_but_cannot_receive_new_reports()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var n = Job(s); var c = Upload(n); await Store(s).UploadAsync(c, default);
        await using (var db = fixture.Context()) { (await db.Locations.SingleAsync(l => l.Id == s.Location)).IsActive = false; await db.SaveChangesAsync(); }
        Assert.Equal(Id(n), (await Store(s).GetAsync(s.Location, Id(n), default)).Id); await Assert.ThrowsAsync<WorkAccessDeniedException>(() => Store(s).UploadAsync(c, default));
    }
    [ErpPostgresFact] public async Task Database_forbids_update_delete_truncate_and_cross_tenant_RLS_reads()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var n = Job(s); Event(n, "running", 1); Event(n, "succeeded", 1); await Store(s).UploadAsync(Upload(n), default);
        foreach (var table in new[] { "label_test_jobs", "label_test_job_events", "label_test_job_receipts", "label_test_job_uploads" })
        {
            foreach (var sql in new[] { $"DELETE FROM {table}", $"TRUNCATE {table} CASCADE" }) { await using var db = fixture.Context(); await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql)); }
        }
        await using (var db = fixture.Context()) { await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE label_test_job_events SET \"Attempt\"=0")); }
        var foreign = await InventoryFenceScenario.Seed(fixture); await using (var db = new AppDbContext(Options())) { await using var tx = await db.Database.BeginTransactionAsync(); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.tenant_id',{foreign.Tenant.ToString()},true)"); Assert.False(await db.Set<LabelArchivedJob>().AnyAsync(j => j.TenantId == s.Tenant)); Assert.False(await db.Set<LabelArchivedJobEvent>().AnyAsync(j => j.TenantId == s.Tenant)); Assert.False(await db.Set<LabelArchivedJobReceipt>().AnyAsync(j => j.TenantId == s.Tenant)); Assert.False(await db.Set<LabelArchivedJobUpload>().AnyAsync(j => j.TenantId == s.Tenant)); }
    }
    [ErpPostgresFact] public async Task Database_rejects_success_without_receipt_and_nonsequential_event_append()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var n = Job(s); await Store(s).UploadAsync(Upload(n), default); Event(n, "running", 1); await Store(s).UploadAsync(Upload(n), default); Event(n, "succeeded", 1); var parsed = LabelJobArchiveRules.Parse(Upload(n)); var e = parsed.Events[^1];
        await using var db = fixture.Context(); await using var tx = await db.Database.BeginTransactionAsync(); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.user_id',{s.Actor.ToString()},true)");
        db.Add(new LabelArchivedJobEvent { TenantId = s.Tenant, JobId = Id(n), Seq = 3, Status = "succeeded", Attempt = 1, OccurredAt = e.OccurredAt, ReceivedAt = DateTime.UtcNow, ReportedBy = s.Actor, ContentHash = e.ContentHash, Payload = e.Payload, CheckpointPayload = Raw(n) }); await db.SaveChangesAsync(); await Assert.ThrowsAsync<PostgresException>(() => tx.CommitAsync());
    }
    [ErpPostgresFact] public async Task Pagination_and_reports_do_not_modify_stock_sales_or_inventory_outbox()
    {
        var s = await InventoryFenceScenario.Seed(fixture); int before; await using (var db = fixture.Context()) { before = await db.OutboxEvents.CountAsync(o => o.TenantId == s.Tenant); }
        for (var i = 0; i < 21; i++) await Store(s).UploadAsync(Upload(Job(s)), default);
        var page = await Store(s).ListAsync(s.Location, 1, false, default); Assert.Equal(21, page.TotalCount); Assert.Equal(20, page.Items.Count); Assert.Single((await Store(s).ListAsync(s.Location, 2, false, default)).Items);
        await using (var db = fixture.Context()) { Assert.Equal(3.25m, (await db.ProductStocks.SingleAsync(p => p.Id == s.Batch)).Quantity); Assert.Equal(before, await db.OutboxEvents.CountAsync(o => o.TenantId == s.Tenant)); }
    }
}
