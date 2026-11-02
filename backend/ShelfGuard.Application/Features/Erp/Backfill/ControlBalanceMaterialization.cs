using System.Security.Cryptography;
using System.Text.Json;

namespace ShelfGuard.Application.Features.Erp.Backfill;

/// <summary>Explicitly selected control openings only; never accepts quantities or IDs from a dry-run JSON.</summary>
public sealed record ControlBalanceRequest(Guid TenantId, Guid LocationId, Guid RunId, string SourceFingerprint,
    IReadOnlyList<Guid> BatchIds, string Reason, string AcceptanceReference, int MaxRows = 20_000, Guid? OperatorId = null,
    int? MaxSaleRows = null);
public sealed record ControlBalanceResult(Guid TenantId, Guid LocationId, Guid RunId, string RequestHash,
    string SourceFingerprint, DateTime AsOf, bool Replayed, IReadOnlyList<ControlBalanceRow> Batches);
public sealed record ControlBalanceRow(Guid BatchId, Guid DocumentId, Guid PostingId, Guid EntryId,
    Guid MessageId, decimal Quantity);
public sealed record ControlOpeningPlan(SnapshotBatch Batch, SnapshotItem Item, ProposedIdentity Identity);

public interface IControlBalanceMaterializer
{
    Task<ControlBalanceResult> MaterializeAsync(ControlBalanceRequest request, CancellationToken ct = default);
}

public sealed class MaterializationConflictException(string message) : InvalidOperationException(message);

public static class ControlBalancePlanner
{
    public const string Version = "control-balance-materialization.v2";

    public static void Validate(ControlBalanceRequest request)
    {
        if (request.TenantId == Guid.Empty || request.LocationId == Guid.Empty || request.RunId == Guid.Empty
            || request.BatchIds is not { Count: > 0 and <= 1000 } || request.BatchIds.Any(x => x == Guid.Empty)
            || request.BatchIds.Distinct().Count() != request.BatchIds.Count
            || request.SourceFingerprint is not { Length: 64 } || request.SourceFingerprint.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 500
            || string.IsNullOrWhiteSpace(request.AcceptanceReference) || request.AcceptanceReference.Length > 300
            || request.MaxRows is < 1 or > 100_000 || request.OperatorId == Guid.Empty
            || request.MaxSaleRows is { } salesLimit && (salesLimit < request.MaxRows || salesLimit > 500_000))
            throw new ArgumentException("Explicit scope, run, fingerprint, unique batch selection, reason and staging acceptance reference are required.");
    }

    public static string RequestHash(ControlBalanceRequest request)
    {
        Validate(request);
        var canonical = new { protocol = Version, analyzer = InventoryDryRunAnalyzer.Version,
            identity = InventoryDryRunAnalyzer.IdentityVersion, request.TenantId, request.LocationId, request.RunId,
            request.SourceFingerprint, batches = request.BatchIds.OrderBy(x => x.ToString("N"), StringComparer.Ordinal),
            request.Reason, request.AcceptanceReference, request.OperatorId };
        // Existing v2 requests retain exactly their historical hash. The larger, explicit
        // sales bound is maintenance-only and part of the new request identity.
        var bytes = request.MaxSaleRows is null ? JsonSerializer.SerializeToUtf8Bytes(canonical)
            : JsonSerializer.SerializeToUtf8Bytes(new { protocol = "control-balance-materialization.v3", request = canonical, request.MaxSaleRows });
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public static IReadOnlyList<ControlOpeningPlan> Plan(ControlBalanceRequest request, InventorySnapshot snapshot)
    {
        Validate(request);
        if (snapshot.TenantId != request.TenantId || snapshot.LocationId != request.LocationId)
            throw new MaterializationConflictException("Snapshot scope differs from the request.");
        var report = new InventoryDryRunAnalyzer().Analyze(snapshot);
        if (report.SourceFingerprint != request.SourceFingerprint)
            throw new MaterializationConflictException("Sources changed; generate and review a new dry-run.");
        var plans = new List<ControlOpeningPlan>();
        foreach (var id in request.BatchIds.OrderBy(x => x.ToString("N"), StringComparer.Ordinal))
        {
            var row = report.Batches.SingleOrDefault(x => x.BatchId == id);
            var batch = snapshot.Batches.SingleOrDefault(x => x.Id == id);
            if (row?.ControlOpeningIdentity is null || batch is null || row.ExistingLedgerEntries != 0
                || row.ProjectionQuantity.HasValue || row.CurrentQuantity <= 0)
                throw new MaterializationConflictException("Selected batch cannot receive a control opening; no partial selection is applied.");
            var item = snapshot.Items.Single(x => x.Id == batch.ItemId);
            if (string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 512 || string.IsNullOrWhiteSpace(item.Unit)
                || item.Unit.Length > 24 || batch.BatchNumber?.Length > 100)
                throw new MaterializationConflictException("Selected batch has incompatible snapshot labels or unit.");
            plans.Add(new(batch, item, row.ControlOpeningIdentity));
        }
        return plans;
    }
}
