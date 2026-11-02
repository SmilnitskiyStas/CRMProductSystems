using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShelfGuard.Application.Features.Erp.Backfill;

public sealed record InventoryDryRunReport(int SchemaVersion, string AnalyzerVersion, Guid TenantId, Guid LocationId,
    DateTime CapturedAt, string DatabaseSnapshot, string SourceFingerprint, bool ApplySupported,
    IReadOnlyList<BatchDryRun> Batches, IReadOnlyList<DryRunIssue> Issues, DryRunSummary Summary);
public sealed record DryRunSummary(int Batches, int HistoryCandidates, int ControlOpeningCandidates,
    int LedgerMatches, int LedgerDiscrepancies, int InvalidBatches);
public sealed record BatchDryRun(Guid BatchId, Guid ItemId, Guid LocationId, string? CurrentItemName, string? CurrentUnit,
    decimal CurrentQuantity, decimal? ReconstructedQuantity, string HistoryStatus,
    int ExistingLedgerEntries, decimal? LedgerQuantity, decimal? Difference, string LedgerStatus,
    decimal? ProjectionQuantity, decimal? ReservedQuantity, string Recommendation,
    ProposedIdentity? ControlOpeningIdentity, IReadOnlyList<MovementIdentity> MovementIdentities,
    IReadOnlyList<DryRunIssue> Issues);
public sealed record DryRunIssue(string Code, Guid? SourceId, string Detail);
public sealed record ProposedIdentity(string SourceSystem, string SourceId, int Version, Guid DocumentKey,
    Guid DocumentId, Guid LineId, Guid PostingId, Guid EntryId, Guid MessageId);
public sealed record MovementIdentity(Guid MovementId, ProposedIdentity Target);

public static class InventoryDryRunJson
{
    public static JsonSerializerOptions Options(bool indented = false)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = indented };
        options.Converters.Add(new ExactDecimal());
        return options;
    }

    private sealed class ExactDecimal : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String
                ? decimal.Parse(reader.GetString()!, NumberStyles.Float, CultureInfo.InvariantCulture) : reader.GetDecimal();
        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString("G29", CultureInfo.InvariantCulture));
    }
}
