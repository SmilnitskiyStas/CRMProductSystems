using System.Text.Json;
using System.Text.Json.Serialization;
namespace ShelfGuard.Application.Features.Erp.Posting;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LabelPrinterData(string Name, string Protocol, int Dpi, decimal WidthMm, decimal HeightMm, bool IsActive, string Transport);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LabelPrinterSave(Guid OperationId, Guid? ProfileKey, int? ExpectedVersion, Guid LocationId, LabelPrinterData Data);
public sealed record LabelPrinterView(Guid Id, Guid ProfileKey, int Version, Guid LocationId, Guid CreatedBy, DateTime CreatedAt, string ContentHash, LabelPrinterData Data);
public sealed record LabelPrinterResult(LabelPrinterView Revision, bool Replayed);
public interface ILabelPrinterProfiles
{
    Task<ProductionDesktopPage<LabelPrinterView>> ListAsync(Guid locationId, int page, CancellationToken ct);
    Task<ProductionDesktopPage<LabelPrinterView>> HistoryAsync(Guid locationId, Guid key, int page, CancellationToken ct);
    Task<LabelPrinterView> RevisionAsync(Guid locationId, Guid id, CancellationToken ct);
    Task<LabelPrinterResult> SaveAsync(LabelPrinterSave command, CancellationToken ct);
}
public static class LabelPrinterRules
{
    public const string Protocol = "labels.printers.v1";
    public static LabelPrinterData Normalize(LabelPrinterData d)
    {
        static bool Mm(decimal v, decimal min, decimal max) => v >= min && v <= max && decimal.Round(v, 2) == v;
        if (d == null || string.IsNullOrWhiteSpace(d.Name) || d.Name.Length > 120 || d.Name.Any(char.IsControl) || d.Protocol is not ("pdf" or "zpl") || d.Transport != "simulation" || d.Dpi is not (203 or 300) || !Mm(d.WidthMm, 30, 210) || !Mm(d.HeightMm, 20, 297)) throw new ArgumentException("Invalid simulation printer profile.");
        return d with { Name = d.Name.Trim() };
    }
    public static LabelPrinterSave Normalize(LabelPrinterSave c)
    {
        if (c.OperationId == Guid.Empty || c.LocationId == Guid.Empty || c.ProfileKey == Guid.Empty || (c.ProfileKey == null ? c.ExpectedVersion != null : c.ExpectedVersion is not (>= 1 and < 10000))) throw new ArgumentException("Invalid printer revision.");
        return c with { Data = Normalize(c.Data) };
    }
    public static void Compatible(LabelPrinterData p, LabelLayout layout)
    {
        p = Normalize(p);
        if (!p.IsActive || p.Dpi != layout.Dpi || p.WidthMm != layout.WidthMm || p.HeightMm != layout.HeightMm) throw new ArgumentException("Printer DPI/media differs from the frozen layout.");
    }
    public static Guid ValidateBinding(JsonElement p, Guid location, LabelLayout layout)
    {
        var names = p.EnumerateObject().Select(x => x.Name).ToArray(); string[] required = ["revisionId", "profileKey", "version", "locationId", "contentHash", "data", "documentHash", "bindingHash"];
        if (names.Length != required.Length || names.Distinct().Count() != names.Length || names.Except(required).Any()) throw new ArgumentException("Invalid printer binding.");
        string S(string key) => p.GetProperty(key).GetString() ?? throw new ArgumentException("Missing printer reference.");
        Guid G(string key) => Guid.TryParseExact(S(key), "D", out var id) && id != Guid.Empty ? id : throw new ArgumentException("Invalid printer reference.");
        var id = G("revisionId"); G("profileKey"); if (G("locationId") != location || !p.GetProperty("version").TryGetInt32(out var version) || version is < 1 or > 10000) throw new ArgumentException("Invalid printer scope.");
        PricingRules.Fingerprint(S("contentHash")); PricingRules.Fingerprint(S("documentHash")); PricingRules.Fingerprint(S("bindingHash"));
        var data = Normalize(p.GetProperty("data").Deserialize<LabelPrinterData>(new JsonSerializerOptions(JsonSerializerDefaults.Web) { NumberHandling = JsonNumberHandling.Strict, PropertyNameCaseInsensitive = false })!); Compatible(data, layout);
        if (LabelJobArchiveRules.Canonical(JsonSerializer.SerializeToElement(data, new JsonSerializerOptions(JsonSerializerDefaults.Web) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })) != p.GetProperty("data").GetRawText()) throw new ArgumentException("Noncanonical printer data.");
        var canonical = LabelJobArchiveRules.Canonical(JsonSerializer.SerializeToElement(new { revisionId = S("revisionId"), profileKey = S("profileKey"), version, locationId = S("locationId"), contentHash = S("contentHash"), data, documentHash = S("documentHash") }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        if (LabelJobArchiveRules.Hash(canonical) != S("bindingHash")) throw new ArgumentException("Printer binding hash differs."); return id;
    }
}
