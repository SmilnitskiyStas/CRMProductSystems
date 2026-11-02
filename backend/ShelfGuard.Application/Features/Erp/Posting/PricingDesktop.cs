using System.Globalization;
namespace ShelfGuard.Application.Features.Erp.Posting;

public sealed record PricingPosScope(Guid LocationId, string Name, bool OnlineEnabled, bool OfflineEnabled, bool Ready);
public sealed record PricingAccess(bool CanManage, bool CanPreview, string Mode, IReadOnlyList<PricingPosScope>? PosScopes = null);
public sealed record PricingLine(Guid ItemId, decimal Price, string ItemFingerprint);
public sealed record PricingData(string Name, Guid? LocationId, int Priority, bool IsActive, DateTimeOffset ValidFrom, DateTimeOffset? ValidUntil, IReadOnlyList<PricingLine> Lines);
public sealed record PricingSave(Guid OperationId, Guid? ListKey, string? ExpectedFingerprint, string Reason, PricingData Data);
public sealed record PricingItemView(Guid ItemId, string Name, string Unit, decimal Price, decimal VatRate, string ItemFingerprint);
public sealed record PricingListView(Guid Id, Guid ListKey, int Version, Guid DocumentId, string Fingerprint, string Name, string Currency, Guid? LocationId, string? LocationName, int Priority, bool IsActive, DateTime ValidFrom, DateTime? ValidUntil, IReadOnlyList<PricingItemView> Lines);
public sealed record PricingResult(Guid Id, Guid ListKey, int Version, Guid DocumentId, bool Replayed);
public sealed record PricingPreview(Guid OperationId, Guid LocationId, Guid ItemId, DateTimeOffset At);
public sealed record PricingOffer(Guid SnapshotId, Guid TenantId, Guid LocationId, Guid ItemId, string ItemName, string Unit, string Currency, DateTime At, DateTime CreatedAt, decimal? PlannedPrice, decimal? LegacyBasePrice, decimal PlannedVatRate, decimal LegacyVatRate, Guid? PriceListId, Guid? ListKey, int? Version, string Source, string Policy, DateTime? NextBoundary, bool Differs, IReadOnlyList<string> Warnings);
public sealed record PricingSnapshotView(PricingOffer Offer, string ContentHash, bool Replayed);

public interface IPricingDesktop
{
    Task<PricingAccess> AccessAsync(CancellationToken ct);
    Task<ProductionDesktopPage<PricingListView>> ListsAsync(int page, CancellationToken ct);
    Task<PricingListView> ListAsync(Guid key, CancellationToken ct);
    Task<PricingResult> SaveAsync(PricingSave command, CancellationToken ct);
    Task<PricingSnapshotView> PreviewAsync(PricingPreview command, CancellationToken ct);
    Task<PricingSnapshotView> SnapshotAsync(Guid id, CancellationToken ct);
}

public static class PricingRules
{
    public const string Protocol = "pricing.desktop.v1";
    public const string Policy = "planned-base.v1";
    public static decimal Price(decimal v)
    {
        if (v < 0 || v > 9999999999.99m || decimal.Round(v, 2) != v) throw new ArgumentException("Price must be an exact nonnegative numeric(12,2).");
        return decimal.Parse(v.ToString("0.00", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }
    public static string Fingerprint(string? v) => v is { Length: 64 } && v.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') ? v : throw new ArgumentException("Missing fingerprint.");
    public static PricingSave Normalize(PricingSave c)
    {
        if (c.OperationId == Guid.Empty || c.ListKey == Guid.Empty || c.Data == null || c.Data.LocationId == Guid.Empty || c.Data.Priority is < 0 or > 10000 || c.Data.Lines == null || c.Data.Lines.Count is < 1 or > 100 || c.Data.Lines.Any(l => l == null)) throw new ArgumentException("Invalid pricing command.");
        var d = c.Data;
        if (string.IsNullOrWhiteSpace(d.Name) || d.Name.Trim().Length > 200 || d.Name.Any(char.IsControl) || string.IsNullOrWhiteSpace(c.Reason) || c.Reason.Trim().Length is < 15 or > 500 || c.Reason.Any(char.IsControl)) throw new ArgumentException("Invalid description.");
        // Seconds are the shared desktop/API precision; avoid platform-dependent subsecond rounding.
        var from = d.ValidFrom.ToUniversalTime(); var until = d.ValidUntil?.ToUniversalTime();
        if (from.Year is < 2020 or > 2100 || from.Ticks % TimeSpan.TicksPerSecond != 0 || until is { } end && (end <= from || end.Year > 2100 || end.Ticks % TimeSpan.TicksPerSecond != 0)) throw new ArgumentException("Invalid half-open validity period.");
        var lines = d.Lines.OrderBy(l => l.ItemId).Select(l => l.ItemId == Guid.Empty ? throw new ArgumentException("Invalid item.") : l with { Price = Price(l.Price), ItemFingerprint = Fingerprint(l.ItemFingerprint) }).ToArray();
        if (lines.DistinctBy(l => l.ItemId).Count() != lines.Length) throw new ArgumentException("Duplicate item.");
        if (c.ListKey == null && c.ExpectedFingerprint != null) throw new ArgumentException("New list cannot have an expected version.");
        return c with { ExpectedFingerprint = c.ListKey == null ? null : Fingerprint(c.ExpectedFingerprint), Reason = c.Reason.Trim(), Data = d with { Name = d.Name.Trim(), ValidFrom = from, ValidUntil = until, Lines = lines } };
    }
    public static PricingPreview Normalize(PricingPreview c)
    {
        if (c.OperationId == Guid.Empty || c.LocationId == Guid.Empty || c.ItemId == Guid.Empty || c.At.Year is < 2020 or > 2100 || c.At.Ticks % TimeSpan.TicksPerSecond != 0) throw new ArgumentException("Invalid preview context.");
        return c with { At = c.At.ToUniversalTime() };
    }
}
public sealed class PricingConflict(string code, string message) : Exception(message) { public string Code { get; } = code; }
