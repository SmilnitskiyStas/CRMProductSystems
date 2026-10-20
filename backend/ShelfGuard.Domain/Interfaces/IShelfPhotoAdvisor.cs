namespace ShelfGuard.Domain.Interfaces;

/// <summary>Reference photo of a catalog item (bytes + image/jpeg|png|webp), optionally labelled (front/side/back).</summary>
public sealed record ShelfReferenceImage(byte[] Data, string MediaType, string? Label = null);

/// <summary>One planogram entry the AI must look for on the photo.</summary>
/// <param name="References">Up to 3 catalog reference photos (primary first); items without any are matched by name/barcode only.</param>
public sealed record ShelfPhotoExpectedItem(
    Guid ItemId, string Name, string? Barcode, int ExpectedFacings, IReadOnlyList<ShelfReferenceImage>? References = null);

/// <param name="Status"><c>present</c> | <c>low</c> | <c>missing</c></param>
public sealed record ShelfPhotoItemVerdict(
    Guid ItemId, string Name, string Status, int EstimatedCount, double Confidence, string? Note);

public sealed record ShelfPhotoAnalysis(
    List<ShelfPhotoItemVerdict> Items, string? Summary, string Model, int TokensUsed);

/// <summary>
/// Vision advisor comparing a shelf photo with the zone planogram. Implemented in
/// <c>Infrastructure/AI/ShelfPhotoAdvisor</c> (AI-isolation rule); called only on explicit user action.
/// </summary>
public interface IShelfPhotoAdvisor
{
    Task<bool> IsConfiguredAsync(CancellationToken ct = default);

    Task<ShelfPhotoAnalysis> AnalyzeAsync(
        byte[] image, string mediaType, IReadOnlyList<ShelfPhotoExpectedItem> expected, CancellationToken ct = default);
}
