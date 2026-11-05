using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ShelfGuard.Application.Features.Erp.Backfill;

/// <summary>Quantitative preflight only. It neither certifies missing history nor materializes ERP rows.</summary>
public sealed class InventoryDryRunAnalyzer
{
    public const string Version = "inventory-preflight.v2";
    public const string IdentityVersion = "inventory-backfill-identity.v1";
    private const decimal Max = 99_999_999_999_999.9999m;

    public InventoryDryRunReport Analyze(InventorySnapshot snapshot)
    {
        ValidateSnapshot(snapshot);
        var global = new List<DryRunIssue>();
        var batches = snapshot.Batches.ToDictionary(x => x.Id);
        var items = snapshot.Items.ToDictionary(x => x.Id);
        var zones = snapshot.Zones.Select(x => x.Id).ToHashSet();
        var movements = snapshot.Movements.Where(x => x.BatchId.HasValue).ToLookup(x => x.BatchId!.Value);
        var ledger = snapshot.Ledger.ToLookup(x => x.BatchId);
        var projections = snapshot.Projections.ToLookup(x => x.BatchId);
        foreach (var movement in snapshot.Movements.Where(x => !x.BatchId.HasValue || !batches.ContainsKey(x.BatchId.Value)))
            global.Add(new("movement_batch_unresolved", movement.Id, "Movement has no visible batch in this scope; direction alone cannot allocate it."));
        foreach (var row in snapshot.Ledger.Where(x => !batches.ContainsKey(x.BatchId)))
            global.Add(new("ledger_batch_unresolved", row.Id, "Ledger entry has no visible legacy batch in this scope."));
        foreach (var row in snapshot.Projections.Where(x => !batches.ContainsKey(x.BatchId)))
            global.Add(new("projection_batch_unresolved", row.Id, "Projection has no visible legacy batch in this scope."));
        foreach (var row in snapshot.Events.Where(x => x.BatchId.HasValue && !batches.ContainsKey(x.BatchId.Value)))
            global.Add(new("event_batch_unresolved", row.Id, "Quantity event references a missing batch."));
        foreach (var row in snapshot.Production.Where(x => !batches.ContainsKey(x.BatchId)))
            global.Add(new("production_batch_unresolved", row.Id, "Durable consumption references a missing batch."));
        foreach (var row in snapshot.Sales.Where(x => !items.ContainsKey(x.ItemId)))
            global.Add(new("sale_item_not_in_scope", row.Id, "Sale item is missing or outside the tenant scope."));

        var unsupported = UnsupportedEvents(snapshot, global);
        var saleItems = snapshot.Sales.Select(x => x.ItemId).ToHashSet();
        var productionBatches = snapshot.Production.Select(x => x.BatchId).ToHashSet();
        var eventBatches = unsupported.Where(x => x.BatchId.HasValue).Select(x => x.BatchId!.Value).ToHashSet();
        var eventItems = unsupported.Where(x => x.BatchId is null).Select(x => x.ItemId).ToHashSet();
        var unresolvedItems = snapshot.Movements.Where(x => x.BatchId is null || !batches.ContainsKey(x.BatchId.Value)).Select(x => x.ItemId)
            .Concat(snapshot.Production.Where(x => !batches.ContainsKey(x.BatchId)).Select(x => x.ItemId)).ToHashSet();
        var reports = new List<BatchDryRun>();
        foreach (var batch in Ordered(snapshot.Batches, x => x.Id))
        {
            var issues = new List<DryRunIssue>();
            bool valid = true;
            if (!items.ContainsKey(batch.ItemId)) { valid = false; issues.Add(new("item_not_in_scope", batch.Id, "Batch item is missing or outside the tenant scope.")); }
            if (batch.ZoneId.HasValue && !zones.Contains(batch.ZoneId.Value)) { valid = false; issues.Add(new("zone_not_in_scope", batch.Id, "Batch zone does not belong to this location.")); }
            if (!Quantity(batch.Quantity) || !Quantity(batch.QuantityInitial)) { valid = false; issues.Add(new("invalid_batch_quantity", batch.Id, "Negative or incompatible quantity cannot seed an ERP projection.")); }
            var ownMovements = movements[batch.Id].ToList();
            if (saleItems.Contains(batch.ItemId))
                issues.Add(new("pos_allocation_requires_review", batch.Id, "POS primary-batch pointers do not prove the complete FEFO allocation. Receipts and expiring events are not added together."));
            if (productionBatches.Contains(batch.Id))
                issues.Add(new("production_consumption_requires_review", batch.Id, "Durable production consumption exists but is not duplicated with its stock_event mirror."));
            if (eventBatches.Contains(batch.Id) || eventItems.Contains(null) || eventItems.Contains(batch.ItemId))
                issues.Add(new("unallocated_quantity_event", batch.Id, "A quantity event is not covered by a verified movement mirror; no allocation is inferred."));
            if (unresolvedItems.Contains(batch.ItemId))
                issues.Add(new("unresolved_source_allocation", batch.Id, "Other quantity evidence for this item has no resolved batch allocation."));

            var closing = Reconstruct(batch, ownMovements, issues);
            var history = valid && closing.HasValue && issues.Count == 0 ? "movement_history_candidate" : "unverifiable";
            var entries = ledger[batch.Id].ToList();
            if (entries.Any(x => x.ItemId != batch.ItemId || x.LocationId != batch.LocationId))
            { valid = false; issues.Add(new("ledger_scope_mismatch", batch.Id, "Ledger batch/item/location identity differs from the captured batch.")); }
            decimal? ledgerQuantity = entries.Count == 0 ? null : entries.Sum(x => x.Delta);
            var ledgerStatus = ledgerQuantity is null ? "not_backfilled" : ledgerQuantity == batch.Quantity ? "matched" : "discrepancy";
            if (ledgerQuantity.HasValue && ledgerQuantity != batch.Quantity)
                issues.Add(new("ledger_balance_discrepancy", batch.Id, "Existing ledger sum differs from the captured physical quantity; a second opening is forbidden."));
            var projection = projections[batch.Id].ToList();
            if (projection.Count > 1 || projection.Any(x => x.ItemId != batch.ItemId || x.LocationId != batch.LocationId))
            { valid = false; issues.Add(new("projection_scope_mismatch", batch.Id, "Projection identity is not unique or differs from the batch.")); }
            var projected = projection.FirstOrDefault();
            if (projected is not null && (ledgerQuantity is null || projected.Quantity != ledgerQuantity
                || projected.ReservedQuantity < 0 || projected.ReservedQuantity > projected.Quantity))
                issues.Add(new("projection_discrepancy", projected.Id, "Projection is not supported by the ledger sum or has an invalid reserve."));
            if (projected is not null && (ledgerQuantity is null || !Quantity(projected.Quantity)
                || !Quantity(projected.ReservedQuantity) || projected.ReservedQuantity > projected.Quantity)) valid = false;
            if (!valid) history = "unverifiable";
            var recommendation = !valid ? "blocked" : entries.Count > 0 ? "reconcile_existing_ledger"
                : batch.Quantity == 0 ? "zero_balance_no_posting" : history == "movement_history_candidate"
                    ? "review_history_candidate" : "review_control_opening";
            var opening = valid && entries.Count == 0 && batch.Quantity > 0
                ? Identity(snapshot.TenantId, "legacy.control_balance", batch.Id) : null;
            reports.Add(new(batch.Id, batch.ItemId, batch.LocationId, items.GetValueOrDefault(batch.ItemId)?.Name, items.GetValueOrDefault(batch.ItemId)?.Unit,
                batch.Quantity, history == "movement_history_candidate" ? closing : null, history,
                entries.Count, ledgerQuantity, ledgerQuantity is null ? null : batch.Quantity - ledgerQuantity,
                ledgerStatus, projected?.Quantity, projected?.ReservedQuantity, recommendation, opening,
                history == "movement_history_candidate"
                    ? Ordered(ownMovements, x => x.Id).Select(m => new MovementIdentity(m.Id, Identity(snapshot.TenantId, "legacy.stock_movements", m.Id))).ToList() : [],
                Sort(issues)));
        }
        return new(1, Version, snapshot.TenantId, snapshot.LocationId, snapshot.CapturedAt, snapshot.DatabaseSnapshot,
            Fingerprint(snapshot), false, reports, Sort(global),
            new(reports.Count, reports.Count(x => x.HistoryStatus == "movement_history_candidate"),
                reports.Count(x => x.ControlOpeningIdentity is not null), reports.Count(x => x.LedgerStatus == "matched"),
                reports.Count(x => x.LedgerStatus == "discrepancy"), reports.Count(x => x.Recommendation == "blocked")));
    }

    private static decimal? Reconstruct(SnapshotBatch batch, List<SnapshotMovement> rows, List<DryRunIssue> issues)
    {
        if (rows.Count == 0) { issues.Add(new("no_movement_history", batch.Id, "QuantityInitial alone does not prove an opening movement.")); return null; }
        foreach (var row in rows)
        {
            if (row.ItemId != batch.ItemId || row.Before is null || row.After is null || !Quantity(row.Before.Value)
                || !Quantity(row.After.Value) || row.Quantity <= 0 || !Quantity(row.Quantity)
                || row.Quantity != decimal.Abs(row.After.Value - row.Before.Value)
                || (row.After > row.Before ? row.ToLocationId != batch.LocationId : row.FromLocationId != batch.LocationId)
                || row.Type is not ("receipt" or "transfer" or "write_off" or "adjustment"))
            { issues.Add(new("unsupported_movement", row.Id, "Movement lacks consistent snapshots, direction, quantity or supported semantics.")); return null; }
        }
        var roots = rows.Where(x => x.Type is "receipt" or "transfer" && x.Before == 0 && x.After == batch.QuantityInitial
            && x.ReferenceType != "stock_transfer_cancellation").ToList();
        if (roots.Count != 1 || roots[0].CreatedAt != rows.Min(x => x.CreatedAt)
            || roots[0].After <= 0 || (batch.SourceId.HasValue && roots[0].ReferenceId != batch.SourceId))
        { issues.Add(new("opening_not_proven", batch.Id, "A unique initial receipt/transfer is not proven; cancellation is never an opening.")); return null; }
        decimal current = 0;
        foreach (var group in rows.GroupBy(x => x.CreatedAt).OrderBy(x => x.Key))
        {
            var pending = group.ToList();
            while (pending.Count > 0)
            {
                var next = pending.Where(x => x.Before == current).ToList();
                if (next.Count != 1) { issues.Add(new("movement_chain_gap_or_ambiguity", batch.Id, "Snapshots do not form an unambiguous chronological chain; GUID order is not used as causality.")); return null; }
                current = next[0].After!.Value; pending.Remove(next[0]);
            }
        }
        if (current != batch.Quantity) { issues.Add(new("history_balance_discrepancy", batch.Id, "Reconstructed movement closing differs from the captured quantity.")); return null; }
        return current;
    }

    private static List<SnapshotEvent> UnsupportedEvents(InventorySnapshot snapshot, List<DryRunIssue> issues)
    {
        var result = new List<SnapshotEvent>();
        Guid Ingestion(SnapshotEvent row)
        {
            try
            {
                using var json = JsonDocument.Parse(row.Metadata ?? "{}");
                if (row.Type == "sensor" && json.RootElement.ValueKind == JsonValueKind.Object
                    && json.RootElement.TryGetProperty("ingestionId", out var id) && id.TryGetGuid(out var value)) return value;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { }
            return Guid.Empty;
        }
        var eventKeys = snapshot.Events.ToDictionary(x => x.Id, Ingestion);
        var keyCounts = eventKeys.Values.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var mirrored = snapshot.Movements.Where(x => x.ReferenceType == "iot_sensor" && x.ReferenceId.HasValue).ToLookup(x => x.ReferenceId!.Value);
        foreach (var row in snapshot.Events.Where(x => x.Delta is not null and not 0))
        {
            var ingestion = eventKeys[row.Id];
            var mirrors = ingestion == Guid.Empty ? [] : mirrored[ingestion].ToList();
            if (mirrors.Count > 0 && mirrors.All(x => x.ItemId == row.ItemId && x.Before.HasValue && x.After.HasValue
                    && x.FromLocationId == snapshot.LocationId)
                && mirrors.Sum(x => x.After!.Value - x.Before!.Value) == row.Delta
                && keyCounts[ingestion] == 1 && (row.LocationId is null || row.LocationId == snapshot.LocationId)) continue;
            result.Add(row);
            if (row.BatchId is null) issues.Add(new("unallocated_quantity_event", row.Id, "Quantity event has no batch-level allocation or verified IoT movement mirror."));
        }
        return result;
    }

    public static ProposedIdentity Identity(Guid tenant, string source, Guid id)
    {
        if (tenant == Guid.Empty || id == Guid.Empty || source is not ("legacy.control_balance" or "legacy.stock_movements"))
            throw new ArgumentException("Invalid deterministic source identity.");
        Guid Key(string role)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"AgruSystems|{IdentityVersion}|{tenant:N}|{source}|{id:N}|1|{role}"));
            // Canonical hex preserves network byte order; explicit version/variant bits keep IDs interoperable.
            bytes[6] = (byte)((bytes[6] & 0x0f) | 0x80);
            bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
            return Guid.ParseExact(Convert.ToHexString(bytes.AsSpan(0, 16)), "N");
        }
        return new(source, id.ToString("N"), 1, Key("document-key"), Key("document"), Key("line"), Key("posting"), Key("entry"), Key("message"));
    }

    private static string Fingerprint(InventorySnapshot source)
    {
        var canonical = source with { CapturedAt = default, DatabaseSnapshot = "",
            Items = Ordered(source.Items, x => x.Id), Zones = Ordered(source.Zones, x => x.Id), Batches = Ordered(source.Batches, x => x.Id),
            Movements = Ordered(source.Movements, x => x.Id), Events = Ordered(source.Events, x => x.Id), Sales = Ordered(source.Sales, x => x.Id),
            Production = Ordered(source.Production, x => x.Id), Ledger = Ordered(source.Ledger, x => x.Id), Projections = Ordered(source.Projections, x => x.Id) };
        // Hash the same canonical UTF-8 JSON without allocating a second full history
        // buffer. Large, explicitly bounded maintenance snapshots can contain many sales.
        using var hash = SHA256.Create();
        using (var stream = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write))
        {
            JsonSerializer.Serialize(stream, canonical, InventoryDryRunJson.Options());
            stream.FlushFinalBlock();
            return Convert.ToHexString(hash.Hash!).ToLowerInvariant();
        }
    }

    private static List<T> Ordered<T>(IEnumerable<T> rows, Func<T, Guid> id) => rows.OrderBy(x => id(x).ToString("N"), StringComparer.Ordinal).ToList();
    private static List<DryRunIssue> Sort(IEnumerable<DryRunIssue> issues) => issues.OrderBy(x => x.Code, StringComparer.Ordinal)
        .ThenBy(x => x.SourceId?.ToString("N"), StringComparer.Ordinal).ToList();
    private static bool Quantity(decimal number) => number >= 0 && number <= Max && decimal.Round(number, 4) == number;

    private static void ValidateSnapshot(InventorySnapshot s)
    {
        if (s.TenantId == Guid.Empty || s.LocationId == Guid.Empty || s.CapturedAt.Kind != DateTimeKind.Utc)
            throw new ArgumentException("An explicit tenant/location and UTC snapshot are required.");
        void Check<T>(IEnumerable<T> rows, Func<T, Guid> id, Func<T, bool> scope)
        {
            if (rows.Any(x => id(x) == Guid.Empty || !scope(x)) || rows.Select(id).Distinct().Count() != rows.Count())
                throw new ArgumentException("Snapshot has duplicate identities or crosses the requested scope.");
        }
        Check(s.Items, x => x.Id, x => x.TenantId == s.TenantId);
        Check(s.Zones, x => x.Id, x => x.LocationId == s.LocationId);
        var batchIds = s.Batches.Select(x => x.Id).ToHashSet();
        Check(s.Batches, x => x.Id, x => x.TenantId == s.TenantId && x.LocationId == s.LocationId && x.AddedAt.Kind == DateTimeKind.Utc);
        Check(s.Movements, x => x.Id, x => x.TenantId == s.TenantId && x.CreatedAt.Kind == DateTimeKind.Utc);
        Check(s.Events, x => x.Id, x => x.TenantId == s.TenantId && x.CreatedAt.Kind == DateTimeKind.Utc
            && (x.LocationId is null || x.LocationId == s.LocationId || x.BatchId.HasValue && batchIds.Contains(x.BatchId.Value)));
        Check(s.Sales, x => x.Id, x => x.TenantId == s.TenantId && x.LocationId == s.LocationId);
        Check(s.Production, x => x.Id, x => x.TenantId == s.TenantId && x.LocationId == s.LocationId);
        Check(s.Ledger, x => x.Id, x => x.TenantId == s.TenantId && x.LocationId == s.LocationId);
        Check(s.Projections, x => x.Id, x => x.TenantId == s.TenantId && x.LocationId == s.LocationId);
    }
}
