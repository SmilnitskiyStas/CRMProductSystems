using System.Globalization;
using System.Text.Json;
using ShelfGuard.Application.Features.Erp.Backfill;
using Xunit;

namespace ShelfGuard.Tests.Erp;

public sealed class InventoryDryRunAnalyzerTests
{
    [Fact]
    public void Streaming_fingerprint_preserves_the_existing_canonical_JSON_hash_and_includes_the_last_sale()
    {
        var s = Snapshot() with { Movements = [] };
        s = s with { Sales = Enumerable.Range(1, 3).Select(n => new SnapshotSale(Guid.Parse($"00000000-0000-0000-0000-{n:000000000000}"), Tenant, Guid.NewGuid(), Item, Location, Batch, n)).ToArray() };
        var canonical = s with { CapturedAt = default, DatabaseSnapshot = "" };
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(canonical, InventoryDryRunJson.Options()))).ToLowerInvariant();
        Assert.Equal(expected, Run(s).SourceFingerprint);
        var changed = s with { Sales = [.. s.Sales.Take(2), s.Sales[2] with { Quantity = 9 }] };
        Assert.NotEqual(expected, Run(changed).SourceFingerprint);
        Assert.Equal("unverifiable", Assert.Single(Run(s).Batches).HistoryStatus);
    }

    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Location = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Item = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Batch = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Source = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly DateTime At = new(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc);

    private static InventorySnapshot Snapshot() => new(Tenant, Location, At.AddHours(1), "synthetic-snapshot",
        [new(Item, Tenant, "Current name, not historical")], [],
        [new(Batch, Tenant, Item, Location, null, 3m, 5m, At, "receipt", Source, "captured-lot", new(2026, 12, 1))],
        [new(Guid.Parse("66666666-6666-6666-6666-666666666666"), Tenant, Item, Batch, "receipt", null, Location, 5, 0, 5, "stock_receipt", Source, At),
         new(Guid.Parse("77777777-7777-7777-7777-777777777777"), Tenant, Item, Batch, "write_off", Location, null, 2, 5, 3, "write_off", Guid.NewGuid(), At.AddMinutes(1))],
        [], [], [], [], []);
    private static InventoryDryRunReport Run(InventorySnapshot snapshot) => new InventoryDryRunAnalyzer().Analyze(snapshot);

    [Fact]
    public void Snapshot_chain_is_only_a_candidate_and_never_enables_apply()
    {
        var report = Run(Snapshot()); var row = Assert.Single(report.Batches);
        Assert.False(report.ApplySupported);
        Assert.Equal("movement_history_candidate", row.HistoryStatus);
        Assert.Equal(3m, row.ReconstructedQuantity);
        Assert.Equal("not_backfilled", row.LedgerStatus); Assert.Null(row.LedgerQuantity); Assert.Null(row.Difference);
        Assert.Equal(2, row.MovementIdentities.Count); Assert.NotNull(row.ControlOpeningIdentity);
    }

    [Fact]
    public void Missing_history_proposes_only_a_current_control_opening_not_a_fake_historical_balance()
    {
        var row = Assert.Single(Run(Snapshot() with { Movements = [] }).Batches);
        Assert.Equal("unverifiable", row.HistoryStatus); Assert.Null(row.ReconstructedQuantity);
        Assert.Equal("review_control_opening", row.Recommendation);
        Assert.Empty(row.MovementIdentities);
        Assert.Equal("legacy.control_balance", row.ControlOpeningIdentity!.SourceSystem);
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("direction")]
    [InlineData("quantity")]
    [InlineData("gap")]
    [InlineData("duplicate_opening")]
    [InlineData("closing")]
    public void Broken_or_incomplete_movement_evidence_cannot_be_certified(string error)
    {
        var snapshot = Snapshot(); var rows = snapshot.Movements.ToList();
        if (error == "partial") rows[1] = rows[1] with { Before = null };
        if (error == "direction") rows[1] = rows[1] with { FromLocationId = null, ToLocationId = Location };
        if (error == "quantity") rows[1] = rows[1] with { Quantity = 100 };
        if (error == "gap") rows[1] = rows[1] with { Before = 4, After = 2 };
        if (error == "duplicate_opening") rows.Add(rows[0] with { Id = Guid.NewGuid() });
        if (error == "closing") snapshot = snapshot with { Batches = [snapshot.Batches[0] with { Quantity = 4 }] };
        var row = Assert.Single(Run(snapshot with { Movements = rows }).Batches);
        Assert.Equal("unverifiable", row.HistoryStatus); Assert.Null(row.ReconstructedQuantity);
    }

    [Fact]
    public void Transfer_cancellation_is_not_an_initial_receipt()
    {
        var snapshot = Snapshot();
        var cancel = snapshot.Movements[0] with { Type = "transfer", ReferenceType = "stock_transfer_cancellation" };
        var row = Assert.Single(Run(snapshot with { Movements = [cancel], Batches = [snapshot.Batches[0] with { Quantity = 5 }] }).Batches);
        Assert.Equal("unverifiable", row.HistoryStatus);
        Assert.Contains(row.Issues, x => x.Code == "opening_not_proven");
    }

    [Fact]
    public void Tied_timestamps_use_unique_before_after_edges_not_arbitrary_GUID_sorting()
    {
        var snapshot = Snapshot();
        var tied = snapshot.Movements.Select(x => x with { CreatedAt = At }).Reverse().ToArray();
        Assert.Equal("movement_history_candidate", Assert.Single(Run(snapshot with { Movements = tied }).Batches).HistoryStatus);
        var ambiguous = tied.Append(tied[0] with { Id = Guid.NewGuid() }).ToArray();
        Assert.Equal("unverifiable", Assert.Single(Run(snapshot with { Movements = ambiguous }).Batches).HistoryStatus);
    }

    [Fact]
    public void POS_primary_batch_does_not_prove_full_FEFO_allocation_even_when_arithmetic_matches()
    {
        var snapshot = Snapshot() with { Sales = [new(Guid.NewGuid(), Tenant, Guid.NewGuid(), Item, Location, Batch, 2)] };
        var row = Assert.Single(Run(snapshot).Batches);
        Assert.Equal("unverifiable", row.HistoryStatus); Assert.Null(row.ReconstructedQuantity);
        Assert.Contains(row.Issues, x => x.Code == "pos_allocation_requires_review");
    }

    [Fact]
    public void Production_record_and_its_expiring_event_are_evidence_not_two_consumptions()
    {
        var snapshot = Snapshot() with { Production = [new(Guid.NewGuid(), Tenant, Guid.NewGuid(), Item, Location, Batch, 2)],
            Events = [new(Guid.NewGuid(), Tenant, Item, Batch, Location, "production_consumption", -2, "{}", At)] };
        var row = Assert.Single(Run(snapshot).Batches);
        Assert.Equal(3m, row.CurrentQuantity); Assert.Null(row.ReconstructedQuantity);
        Assert.Contains(row.Issues, x => x.Code == "production_consumption_requires_review");
    }

    [Fact]
    public void Verified_IoT_summary_is_not_counted_again_but_duplicate_or_mismatched_summaries_block_history()
    {
        var snapshot = Snapshot(); var ingestion = Guid.NewGuid();
        var movement = snapshot.Movements[1] with { Type = "adjustment", ReferenceType = "iot_sensor", ReferenceId = ingestion };
        var sensor = new SnapshotEvent(Guid.NewGuid(), Tenant, Item, null, Location, "sensor", -2,
            $"{{\"ingestionId\":\"{ingestion}\"}}", At.AddMinutes(1));
        snapshot = snapshot with { Movements = [snapshot.Movements[0], movement], Events = [sensor] };
        Assert.Equal("movement_history_candidate", Assert.Single(Run(snapshot).Batches).HistoryStatus);
        var duplicate = sensor with { Id = Guid.NewGuid(), Metadata = $"{{ \"ingestionId\" : \"{ingestion}\" }}" };
        Assert.Equal("unverifiable", Assert.Single(Run(snapshot with { Events = [sensor, duplicate] }).Batches).HistoryStatus);
        Assert.Equal("unverifiable", Assert.Single(Run(snapshot with { Events = [sensor with { Delta = -3 }] }).Batches).HistoryStatus);
    }

    [Fact]
    public void Existing_ledger_is_reconciled_and_never_receives_a_second_opening_even_if_net_zero()
    {
        var snapshot = Snapshot() with { Ledger = [new(Guid.NewGuid(), Tenant, Item, Location, Batch, 5), new(Guid.NewGuid(), Tenant, Item, Location, Batch, -5)] };
        var row = Assert.Single(Run(snapshot).Batches);
        Assert.Equal(0m, row.LedgerQuantity); Assert.Equal(3m, row.Difference);
        Assert.Equal("discrepancy", row.LedgerStatus); Assert.Null(row.ControlOpeningIdentity);
        Assert.Equal("reconcile_existing_ledger", row.Recommendation);
    }

    [Fact]
    public void Projection_is_checked_against_ledger_instead_of_becoming_the_source_of_truth()
    {
        var snapshot = Snapshot() with { Ledger = [new(Guid.NewGuid(), Tenant, Item, Location, Batch, 3)],
            Projections = [new(Guid.NewGuid(), Tenant, Item, Location, Batch, 100, 0)] };
        var row = Assert.Single(Run(snapshot).Batches);
        Assert.Equal("matched", row.LedgerStatus); Assert.Contains(row.Issues, x => x.Code == "projection_discrepancy");
    }

    [Theory]
    [InlineData("negative")]
    [InlineData("precision")]
    [InlineData("item")]
    [InlineData("zone")]
    public void Invalid_batch_cannot_propose_a_control_opening(string reason)
    {
        var snapshot = Snapshot();
        snapshot = reason switch
        {
            "negative" => snapshot with { Batches = [snapshot.Batches[0] with { Quantity = -1 }] },
            "precision" => snapshot with { Batches = [snapshot.Batches[0] with { Quantity = 0.00001m }] },
            "item" => snapshot with { Items = [] },
            _ => snapshot with { Batches = [snapshot.Batches[0] with { ZoneId = Guid.NewGuid() }] },
        };
        var row = Assert.Single(Run(snapshot).Batches);
        Assert.Equal("blocked", row.Recommendation); Assert.Null(row.ControlOpeningIdentity);
    }

    [Fact]
    public void Zero_balance_does_not_invent_a_zero_delta_ERP_posting()
    {
        var snapshot = Snapshot() with { Batches = [Snapshot().Batches[0] with { Quantity = 0 }], Movements = [] };
        var row = Assert.Single(Run(snapshot).Batches);
        Assert.Equal("zero_balance_no_posting", row.Recommendation); Assert.Null(row.ControlOpeningIdentity);
    }

    [Fact]
    public void Orphan_sources_are_reported_instead_of_silently_joined_away()
    {
        var snapshot = Snapshot(); var orphan = snapshot.Movements[1] with { Id = Guid.NewGuid(), BatchId = Guid.NewGuid() };
        var report = Run(snapshot with { Movements = snapshot.Movements.Append(orphan).ToArray() });
        Assert.Contains(report.Issues, x => x.Code == "movement_batch_unresolved" && x.SourceId == orphan.Id);
    }

    [Fact]
    public void Fingerprint_and_identity_are_stable_across_order_capture_time_culture_and_decimal_scale()
    {
        var snapshot = Snapshot(); var first = Run(snapshot);
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("uk-UA");
            var reordered = snapshot with { CapturedAt = At.AddDays(1), DatabaseSnapshot = "another",
                Movements = snapshot.Movements.Reverse().ToArray(), Batches = [snapshot.Batches[0] with { Quantity = 3.0000m }] };
            var second = Run(reordered);
            Assert.Equal(first.SourceFingerprint, second.SourceFingerprint);
            Assert.Equal(first.Batches[0].ControlOpeningIdentity, second.Batches[0].ControlOpeningIdentity);
        }
        finally { CultureInfo.CurrentCulture = culture; }
        Assert.NotEqual(first.SourceFingerprint, Run(snapshot with { Batches = [snapshot.Batches[0] with { Quantity = 4 }] }).SourceFingerprint);
        Assert.NotEqual(InventoryDryRunAnalyzer.Identity(Tenant, "legacy.control_balance", Batch),
            InventoryDryRunAnalyzer.Identity(Guid.NewGuid(), "legacy.control_balance", Batch));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(first, InventoryDryRunJson.Options()));
        Assert.Equal(JsonValueKind.String, json.RootElement.GetProperty("batches")[0].GetProperty("currentQuantity").ValueKind);
    }

    [Fact]
    public void Identity_protocol_has_a_fixed_anchor_and_consistent_version_variant_bits()
    {
        var identity = InventoryDryRunAnalyzer.Identity(Tenant, "legacy.control_balance", Batch);
        Assert.Equal(Guid.Parse("172168ad-93db-8200-b155-abb3d4f17fd1"), identity.DocumentKey);
        foreach (var id in new[] { identity.DocumentKey, identity.DocumentId, identity.LineId, identity.PostingId, identity.EntryId, identity.MessageId })
        {
            var hex = id.ToString("N");
            Assert.Equal('8', hex[12]); Assert.Contains(hex[16], "89ab");
        }
    }

    [Fact]
    public void Foreign_scope_or_duplicate_primary_keys_reject_the_whole_snapshot()
    {
        var snapshot = Snapshot();
        Assert.Throws<ArgumentException>(() => Run(snapshot with { Batches = [snapshot.Batches[0] with { TenantId = Guid.NewGuid() }] }));
        Assert.Throws<ArgumentException>(() => Run(snapshot with { Movements = [snapshot.Movements[0], snapshot.Movements[0]] }));
    }
}
