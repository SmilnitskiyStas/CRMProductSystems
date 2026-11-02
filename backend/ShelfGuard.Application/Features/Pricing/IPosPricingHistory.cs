using System.Text.Json;

namespace ShelfGuard.Application.Features.Pricing;

public sealed record PosPricingHistoryView(Guid TransactionId, string Protocol, string Channel,
    DateTime EvaluatedAt, DateTime RecordedAt, string ContentHash, string Payload, PricingShadowReport Comparison);
public sealed record PosPricingChannel(string Channel, int Count, decimal Paid, decimal Vat);
public sealed record PosPricingChannelReport(DateTime AcceptedFrom, DateTime AcceptedUntil, int Sales,
    int Archived, int LegacyWithoutArchive, int MissingRequiredArchive, IReadOnlyList<PosPricingChannel> Channels);
public sealed record PosPricingOutcome(string Channel, string Outcome, int Count);
public sealed record PosPricingDiagnostics(int Page, int PageSize, int ArchivedInPeriod, int Evaluated,
    bool HasMore, IReadOnlyList<PosPricingOutcome> Outcomes);
public interface IPosPricingHistory
{
    Task<PosPricingHistoryView> SnapshotAsync(Guid transactionId, CancellationToken ct);
    Task<PosPricingChannelReport> ChannelsAsync(DateTimeOffset from, DateTimeOffset until, Guid? locationId, CancellationToken ct);
    Task<PosPricingDiagnostics> DiagnosticsAsync(DateTimeOffset from, DateTimeOffset until, Guid? locationId, int page, CancellationToken ct);
}
