using System.Text.RegularExpressions;
namespace ShelfGuard.Application.Features.Erp.Posting;

public sealed record LabelElement(string Id, string Field, string Text, decimal X, decimal Y, decimal Width, decimal Height, decimal FontMm, bool Bold);
public sealed record LabelLayout(string Name, decimal WidthMm, decimal HeightMm, int Dpi, IReadOnlyList<LabelElement> Elements);
public sealed record LabelTemplateSave(Guid OperationId, Guid? TemplateKey, int? ExpectedVersion, LabelLayout Data);
public sealed record LabelAssignmentSave(Guid OperationId, Guid LocationId, Guid? ExpectedAssignmentId, Guid? RevisionId);
public sealed record LabelTemplateView(Guid Id, Guid TemplateKey, int Version, Guid CreatedBy, DateTime CreatedAt, string ContentHash, LabelLayout Data);
public sealed record LabelAssignmentView(Guid Id, Guid LocationId, int Version, Guid? RevisionId, Guid CreatedBy, DateTime CreatedAt);
public sealed record LabelTemplateResult(LabelTemplateView Revision, bool Replayed);
public sealed record LabelAssignmentResult(LabelAssignmentView Assignment, bool Replayed);
public sealed record LabelCapabilities(bool CanPreview, bool CanEditTemplates, bool CanAssign, bool DevelopmentOnly = true, bool CanPrint = false, bool CanBulk = false, bool CanAuditJobs = false, bool JobArchiveAvailable = false, bool PrinterProfilesAvailable = false, bool CanManagePrinters = false, int JobArchiveVersion = 1);
public interface ILabelTemplates
{
    Task<LabelCapabilities> AccessAsync(CancellationToken ct);
    Task<LabelTemplateView> RevisionAsync(Guid id, CancellationToken ct);
    Task<ProductionDesktopPage<LabelTemplateView>> ListAsync(int page, CancellationToken ct);
    Task<ProductionDesktopPage<LabelTemplateView>> HistoryAsync(Guid key, int page, CancellationToken ct);
    Task<LabelTemplateResult> SaveAsync(LabelTemplateSave command, CancellationToken ct);
    Task<LabelAssignmentView?> AssignmentAsync(Guid location, CancellationToken ct);
    Task<LabelAssignmentResult> AssignAsync(LabelAssignmentSave command, CancellationToken ct);
}
public static class LabelTemplateRules
{
    public const string Protocol = "labels.templates.v1";
    public static LabelLayout Normalize(LabelLayout d)
    {
        static string Text(string? s, int max) => !string.IsNullOrWhiteSpace(s) && s.Length <= max && !s.Any(char.IsControl) ? s.Trim() : throw new ArgumentException("Invalid label text.");
        static decimal Mm(decimal v, decimal min, decimal max) => v >= min && v <= max && decimal.Round(v, 2) == v ? v : throw new ArgumentException("Invalid label dimension.");
        if (d == null || d.Dpi is not (203 or 300) || d.Elements == null || d.Elements.Count is < 1 or > 12) throw new ArgumentException("Invalid label.");
        var w = Mm(d.WidthMm, 30, 210); var h = Mm(d.HeightMm, 20, 297);
        var elements = d.Elements.Select(e => {
            if (e == null || e.Id == null || !Regex.IsMatch(e.Id, "^[a-z][a-z0-9_]{0,24}$", RegexOptions.CultureInvariant) || e.Field is not ("name" or "price" or "unit" or "text")) throw new ArgumentException("Invalid field.");
            Mm(e.X, 2, w - 2); Mm(e.Y, 5, h - 5); Mm(e.Width, 1, w - 4); Mm(e.Height, 1, h - 10); Mm(e.FontMm, 1, 12);
            if (e.X + e.Width > w - 2 || e.Y + e.Height > h - 5 || e.FontMm > e.Height) throw new ArgumentException("Field outside label.");
            return e with { Text = e.Field == "text" ? Text(e.Text, 100) : "" };
        }).ToArray();
        if (elements.DistinctBy(e => e.Id).Count() != elements.Length) throw new ArgumentException("Duplicate field.");
        return d with { Name = Text(d.Name, 120), Elements = elements };
    }
    public static LabelTemplateSave Normalize(LabelTemplateSave c)
    {
        if (c.OperationId == Guid.Empty || c.TemplateKey == Guid.Empty || (c.TemplateKey == null ? c.ExpectedVersion != null : c.ExpectedVersion is not (>= 1 and < 10000))) throw new ArgumentException("Invalid version.");
        return c with { Data = Normalize(c.Data) };
    }
    public static LabelAssignmentSave Normalize(LabelAssignmentSave c)
    {
        if (c.OperationId == Guid.Empty || c.LocationId == Guid.Empty || c.ExpectedAssignmentId == Guid.Empty || c.RevisionId == Guid.Empty) throw new ArgumentException("Invalid assignment.");
        return c;
    }
}
