using System.Text.Json;
using Microsoft.Extensions.Logging;
using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.Locations.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Locations;

public sealed class ShelfService : IShelfService
{
    public const long MaxImageBytes = 5 * 1024 * 1024; // Claude API hard limit per image
    private const int MaxItemsPerShelf = 50;
    private const int MaxFacings = 99;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ILocationRepository _locations;
    private readonly IShelfRepository _repo;
    private readonly IShelfPhotoAdvisor _advisor;
    private readonly IShelfInsightService _insights;
    private readonly IShelfRestockAdvisor _restock;
    private readonly ILogger<ShelfService> _log;

    public const long MaxFloorPlanBytes = 8 * 1024 * 1024;

    /// <summary>Root for floor-plan background images; overridable for tests.</summary>
    public string FloorPlanRoot { get; init; } = Path.Combine(PrivateUploadPaths.Root, "floor-plans");

    private const int MaxReferenceImages = 12;
    private const int MaxImagesPerItem = 3;
    private const long MaxReferenceImageBytes = 1_500_000;
    private const long MaxReferenceTotalBytes = 8_000_000;

    /// <summary>Folder holding catalog item images (<c>Item.ImageUrl</c> = /uploads/items/…); overridable for tests.</summary>
    public string ItemImagesRoot { get; init; } = Path.Combine("wwwroot", "uploads", "items");

    /// <summary>Root for saved photos; overridable for tests.</summary>
    public string UploadRoot { get; init; } = Path.Combine(PrivateUploadPaths.Root, "shelf-scans");

    public ShelfService(
        ILocationRepository locations, IShelfRepository repo, IShelfPhotoAdvisor advisor,
        IShelfInsightService insights, IShelfRestockAdvisor restock, ILogger<ShelfService> log)
    {
        _insights = insights;
        _restock = restock;
        _locations = locations;
        _repo = repo;
        _advisor = advisor;
        _log = log;
    }

    // ── Planogram ──────────────────────────────────────────────────────────

    public async Task<(List<ShelfItemDto>? Items, string? Error)> GetShelfItemsAsync(
        Guid tenantId, Guid locationId, Guid zoneId, Guid? shelfId, CancellationToken ct = default)
    {
        var zone = await GuardZoneAsync(tenantId, locationId, zoneId, ct);
        if (zone is null) return (null, "Zone not found.");

        var rows = await _repo.GetShelfItemsAsync(zoneId, shelfId, ct);
        return (rows.Select(ToDto).ToList(), null);
    }

    public async Task<(List<ShelfItemDto>? Items, string? Error)> SetShelfItemsAsync(
        Guid tenantId, Guid locationId, Guid zoneId, SetShelfItemsRequest request, CancellationToken ct = default)
    {
        var zone = await GuardZoneAsync(tenantId, locationId, zoneId, ct);
        if (zone is null) return (null, "Zone not found.");

        if (request.ShelfId == Guid.Empty) return (null, "ShelfId is required.");
        var input = request.Items ?? [];
        if (input.Count > MaxItemsPerShelf) return (null, $"At most {MaxItemsPerShelf} items per shelf section.");
        if (input.Any(i => i.ExpectedFacings < 1 || i.ExpectedFacings > MaxFacings))
            return (null, $"ExpectedFacings must be between 1 and {MaxFacings}.");
        if (input.GroupBy(i => i.ItemId).Any(g => g.Count() > 1))
            return (null, "Duplicate items in request.");

        var existing = await _repo.GetExistingItemIdsAsync(input.Select(i => i.ItemId), ct);
        if (input.Any(i => !existing.Contains(i.ItemId))) return (null, "Item not found.");

        var rows = input.Select(i => new ShelfItem
        {
            TenantId = tenantId,
            LocationId = locationId,
            ZoneId = zoneId,
            ShelfId = request.ShelfId,
            ItemId = i.ItemId,
            ExpectedFacings = i.ExpectedFacings,
        }).ToList();

        await _repo.ReplaceShelfItemsAsync(zoneId, request.ShelfId, rows, ct);

        // Keep the catalog "zone tag" in sync (additive only; removal is left to the catalog UI).
        foreach (var i in input)
        {
            if (!await _repo.ZoneTagExistsAsync(i.ItemId, zoneId, ct))
                await _repo.AddZoneTagAsync(new ItemZoneAssignment { TenantId = tenantId, ItemId = i.ItemId, ZoneId = zoneId }, ct);
        }

        await _repo.SaveChangesAsync(ct);

        var saved = await _repo.GetShelfItemsAsync(zoneId, request.ShelfId, ct);
        return (saved.Select(ToDto).ToList(), null);
    }

    // ── Scans ──────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, string> AllowedTypes = new()
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
    };

    public async Task<(ShelfScanDto? Scan, string? Error, int? StatusCode)> CreateScanAsync(
        Guid tenantId, Guid? userId, Guid locationId, Guid zoneId, Guid? shelfId,
        Stream image, long length, string fileName, CancellationToken ct = default)
    {
        var zone = await GuardZoneAsync(tenantId, locationId, zoneId, ct);
        if (zone is null) return (null, "Zone not found.", 404);

        var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        if (!AllowedTypes.TryGetValue(ext, out var mediaType))
            return (null, "Unsupported image format. Use jpg, png or webp.", 400);
        if (length <= 0) return (null, "No file uploaded.", 400);
        if (length > MaxImageBytes) return (null, "File too large. Max 5 MB.", 400);

        using var ms = new MemoryStream();
        await image.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        if (bytes.Length > MaxImageBytes) return (null, "File too large. Max 5 MB.", 400);
        if (!MatchesSignature(bytes, mediaType)) return (null, "File content does not match its image type.", 400);

        var planogram = await _repo.GetShelfItemsAsync(zoneId, shelfId, ct);
        if (planogram.Count == 0)
            return (null, "No products are assigned to this shelf. Add products to the planogram first.", 422);

        if (!await _advisor.IsConfiguredAsync(ct))
            return (null, "AI-агент не налаштований. Зверніться до вашого провайдера.", 503);

        var expected = await BuildExpectedAsync(planogram, ct);

        ShelfPhotoAnalysis analysis;
        try
        {
            analysis = await _advisor.AnalyzeAsync(bytes, mediaType, expected, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Shelf photo analysis failed for zone {ZoneId}", zoneId);
            await TryPersistFailedScanAsync(tenantId, userId, locationId, zoneId, shelfId, bytes, ext, ct);
            return (null, "AI analysis failed. Try again later.", 502);
        }

        var scanId = Guid.NewGuid();
        var fileOnDisk = await SaveScanImageAsync(tenantId, scanId, ext, bytes, ct);

        var items = analysis.Items
            .Select(v => new ShelfScanItemDto(v.ItemId, v.Name, v.Status, v.EstimatedCount, v.Confidence, v.Note))
            .ToList();
        var summary = analysis.Summary;

        // Enrichment is best-effort: stock/sales lookup or the text summary failing must never fail the scan.
        try
        {
            var insights = await _insights.BuildAsync(locationId, zoneId, planogram, ct);
            items = ShelfRecommendationRules.Enrich(items, insights);
            summary = await TrySummarizeAsync(items, insights, summary, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Shelf scan enrichment failed for zone {ZoneId}", zoneId);
        }
        var missing = items.Count(i => i.Status == "missing");
        var low = items.Count(i => i.Status == "low");

        var scan = new ShelfScan
        {
            Id = scanId,
            TenantId = tenantId,
            LocationId = locationId,
            ZoneId = zoneId,
            ShelfId = shelfId,
            ImagePath = $"/uploads/shelf-scans/{tenantId}/{fileOnDisk}",
            Status = ShelfScan.StatusCompleted,
            ResultJson = JsonSerializer.Serialize(new ShelfScanResult(summary, items), Json),
            MissingCount = missing,
            LowCount = low,
            AlertPending = missing + low > 0,
            Model = analysis.Model,
            TokensUsed = analysis.TokensUsed,
            CreatedBy = userId,
        };

        await _repo.AddScanAsync(scan, ct);
        await _repo.SaveChangesAsync(ct);

        return (ToDto(scan), null, null);
    }

    private async Task<string> SaveScanImageAsync(Guid tenantId, Guid scanId, string ext, byte[] bytes, CancellationToken ct)
    {
        var dir = Path.Combine(UploadRoot, tenantId.ToString());
        Directory.CreateDirectory(dir);
        var fileOnDisk = $"{scanId}{ext}";
        await File.WriteAllBytesAsync(Path.Combine(dir, fileOnDisk), bytes, ct);
        return fileOnDisk;
    }

    /// <summary>Keeps a trace (status failed + message) of a vision failure; never throws.</summary>
    private async Task TryPersistFailedScanAsync(
        Guid tenantId, Guid? userId, Guid locationId, Guid zoneId, Guid? shelfId, byte[] bytes, string ext, CancellationToken ct)
    {
        try
        {
            var scanId = Guid.NewGuid();
            var file = await SaveScanImageAsync(tenantId, scanId, ext, bytes, ct);
            await _repo.AddScanAsync(new ShelfScan
            {
                Id = scanId, TenantId = tenantId, LocationId = locationId, ZoneId = zoneId, ShelfId = shelfId,
                ImagePath = $"/uploads/shelf-scans/{tenantId}/{file}",
                Status = ShelfScan.StatusFailed,
                ErrorMessage = "AI analysis failed.",
                CreatedBy = userId,
            }, ct);
            await _repo.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Could not persist failed shelf scan for zone {ZoneId}", zoneId);
        }
    }

    private async Task<string?> TrySummarizeAsync(
        List<ShelfScanItemDto> items, List<ShelfItemInsightDto> insights, string? visionSummary, CancellationToken ct)
    {
        var facings = insights.ToDictionary(i => i.ItemId, i => i.ExpectedFacings);
        var lines = items.Where(ShelfRecommendationRules.NeedsAction)
            .Where(i => i.Recommendation is not null)
            .Select(i => new ShelfRestockLine(
                i.Name, i.Status, facings.GetValueOrDefault(i.ItemId), i.EstimatedCount,
                i.OnShelfQty ?? 0, i.StoreQty ?? 0, i.WarehouseQty ?? 0, i.Sold7d ?? 0,
                i.Recommendation!.Action, i.Recommendation.SuggestedQty, i.Recommendation.Priority))
            .ToList();
        if (lines.Count == 0) return visionSummary;

        try
        {
            var text = await _restock.SummarizeAsync(lines, ct);
            return string.IsNullOrWhiteSpace(text) ? visionSummary : text;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Shelf restock summary failed; keeping the vision summary");
            return visionSummary;
        }
    }

    // ── Floor-plan background ──────────────────────────────────────────────

    public async Task<(string? Url, string? Error, int? StatusCode)> SaveFloorPlanBackgroundAsync(
        Guid tenantId, Guid locationId, Stream image, long length, string fileName, CancellationToken ct = default)
    {
        var location = await _locations.GetByIdAsync(locationId, ct);
        if (location is null || location.TenantId != tenantId) return (null, "Location not found.", 404);

        var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        if (!AllowedTypes.TryGetValue(ext, out var mediaType))
            return (null, "Unsupported image format. Use jpg, png or webp.", 400);
        if (length <= 0) return (null, "No file uploaded.", 400);
        if (length > MaxFloorPlanBytes) return (null, "File too large. Max 8 MB.", 400);

        using var ms = new MemoryStream();
        await image.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        if (bytes.Length > MaxFloorPlanBytes) return (null, "File too large. Max 8 MB.", 400);
        if (!MatchesSignature(bytes, mediaType)) return (null, "File content does not match its image type.", 400);

        var dir = Path.Combine(FloorPlanRoot, tenantId.ToString());
        Directory.CreateDirectory(dir);
        var fileOnDisk = $"{locationId}_{Guid.NewGuid()}{ext}";
        await File.WriteAllBytesAsync(Path.Combine(dir, fileOnDisk), bytes, ct);
        return ($"/uploads/floor-plans/{tenantId}/{fileOnDisk}", null, null);
    }

    /// <summary>
    /// Distinct items of the planogram with up to <see cref="MaxImagesPerItem"/> reference photos each
    /// (primary first). Loaded breadth-first - every item's primary photo before any second angle -
    /// under the global caps <see cref="MaxReferenceImages"/> / <see cref="MaxReferenceTotalBytes"/>.
    /// </summary>
    private async Task<List<ShelfPhotoExpectedItem>> BuildExpectedAsync(List<ShelfItem> planogram, CancellationToken ct)
    {
        var distinct = planogram
            .GroupBy(p => p.ItemId)
            .Select(g => (Row: g.First(), Facings: g.Sum(x => x.ExpectedFacings)))
            .ToList();

        var catalog = await _repo.GetItemImagesAsync(distinct.Select(d => d.Row.ItemId), ct) ?? [];
        var byItem = catalog.GroupBy(i => i.ItemId).ToDictionary(
            g => g.Key,
            g => g.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.SortOrder).ThenBy(i => i.CreatedAt)
                  .Take(MaxImagesPerItem)
                  .Select(i => (Url: (string?)i.ImageUrl, Label: i.Label))
                  .ToList());

        // Candidates per item: catalog images, or the legacy Item.ImageUrl when none exist.
        List<(string? Url, string? Label)> CandidatesOf(ShelfItem row) =>
            byItem.TryGetValue(row.ItemId, out var l)
                ? l
                : string.IsNullOrWhiteSpace(row.Item?.ImageUrl) ? [] : [(row.Item!.ImageUrl, null)];

        var ordered = distinct
            .Select(d => (d.Row, d.Facings, Candidates: CandidatesOf(d.Row)))
            .OrderByDescending(d => d.Candidates.Count > 0)
            .ToList();

        var loaded = ordered.ToDictionary(d => d.Row.ItemId, _ => new List<ShelfReferenceImage>());
        var count = 0;
        long total = 0;
        for (var round = 0; round < MaxImagesPerItem && count < MaxReferenceImages && total < MaxReferenceTotalBytes; round++)
        {
            foreach (var d in ordered)
            {
                if (round >= d.Candidates.Count) continue;
                if (count >= MaxReferenceImages || total >= MaxReferenceTotalBytes) break;
                var (url, label) = d.Candidates[round];
                var reference = await TryLoadReferenceAsync(url, label, ct);
                if (reference is null || total + reference.Data.Length > MaxReferenceTotalBytes) continue;
                loaded[d.Row.ItemId].Add(reference);
                count++;
                total += reference.Data.Length;
            }
        }

        return ordered.Select(d => new ShelfPhotoExpectedItem(
            d.Row.ItemId, d.Row.Item?.Name ?? string.Empty, d.Row.Item?.Barcodes.FirstOrDefault(), d.Facings,
            loaded[d.Row.ItemId])).ToList();
    }

    private async Task<ShelfReferenceImage?> TryLoadReferenceAsync(string? imageUrl, string? label, CancellationToken ct)
    {
        const string prefix = "/uploads/items/";
        if (string.IsNullOrWhiteSpace(imageUrl) || !imageUrl.StartsWith(prefix, StringComparison.Ordinal))
            return null;
        try
        {
            // "<file>" (legacy) or "<tenantId>/<file>"; no traversal, no deeper nesting.
            var parts = imageUrl[prefix.Length..].Split('/');
            if (parts.Length > 2 || parts.Any(p => p.Length == 0 || p == "." || p == ".." || p.Contains('\\')))
                return null;
            var name = parts[^1];
            if (name != Path.GetFileName(name)) return null;
            if (!AllowedTypes.TryGetValue(Path.GetExtension(name).ToLowerInvariant(), out var mediaType)) return null;

            var root = Path.GetFullPath(ItemImagesRoot);
            var full = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return null;

            var info = new FileInfo(full);
            if (!info.Exists || info.Length == 0 || info.Length > MaxReferenceImageBytes) return null;

            var bytes = await File.ReadAllBytesAsync(full, ct);
            return MatchesSignature(bytes, mediaType) ? new ShelfReferenceImage(bytes, mediaType, label) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _log.LogWarning(ex, "Could not read reference image {ImageUrl}", imageUrl);
            return null;
        }
    }

    public async Task<(List<ShelfSectionDto>? Sections, string? Error)> GetShelvesAsync(
        Guid tenantId, Guid locationId, Guid zoneId, CancellationToken ct = default)
    {
        var zone = await GuardZoneAsync(tenantId, locationId, zoneId, ct);
        if (zone is null) return (null, "Zone not found.");

        var rows = await _repo.GetShelfItemsAsync(zoneId, null, ct);
        var byShelf = rows.ToLookup(r => r.ShelfId);
        var sections = new List<ShelfSectionDto>();

        if (!string.IsNullOrWhiteSpace(zone.Position))
        {
            try
            {
                using var doc = JsonDocument.Parse(zone.Position);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in arr.EnumerateArray())
                    {
                        if (el.ValueKind != JsonValueKind.Object
                            || !el.TryGetProperty("shelfId", out var sid)
                            || !Guid.TryParse(sid.GetString(), out var shelfId)) continue;
                        var label = el.TryGetProperty("label", out var l) && l.ValueKind == JsonValueKind.String
                            ? l.GetString() ?? string.Empty : string.Empty;
                        sections.Add(new ShelfSectionDto(shelfId, label, byShelf[shelfId].Select(ToDto).ToList()));
                    }
                }
            }
            catch (JsonException) { /* malformed plan: return no sections */ }
        }

        return (sections, null);
    }

    public async Task<(List<ShelfScanDto>? Scans, string? Error)> GetScansAsync(
        Guid tenantId, Guid locationId, Guid zoneId, int limit, CancellationToken ct = default)
    {
        var zone = await GuardZoneAsync(tenantId, locationId, zoneId, ct);
        if (zone is null) return (null, "Zone not found.");

        var scans = await _repo.GetScansAsync(zoneId, Math.Clamp(limit, 1, 100), ct);
        return (scans.Select(ToDto).ToList(), null);
    }

    public async Task<(ShelfScanDto? Scan, string? Error)> GetScanAsync(
        Guid tenantId, Guid locationId, Guid zoneId, Guid scanId, CancellationToken ct = default)
    {
        var zone = await GuardZoneAsync(tenantId, locationId, zoneId, ct);
        if (zone is null) return (null, "Zone not found.");

        var scan = await _repo.GetScanAsync(zoneId, scanId, ct);
        return scan is null ? (null, "Scan not found.") : (ToDto(scan), null);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private async Task<LocationZone?> GuardZoneAsync(Guid tenantId, Guid locationId, Guid zoneId, CancellationToken ct)
    {
        var zone = await _locations.GetZoneWithLocationAsync(zoneId, ct);
        return zone is { Location: not null } && zone.LocationId == locationId && zone.Location.TenantId == tenantId
            ? zone : null;
    }

    private static bool MatchesSignature(byte[] b, string mediaType) => mediaType switch
    {
        "image/jpeg" => b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF,
        "image/png" => b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47,
        "image/webp" => b.Length > 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F'
                        && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P',
        _ => false,
    };

    private static ShelfItemDto ToDto(ShelfItem s) => new(
        s.Id, s.ShelfId, s.ItemId, s.Item?.Name ?? string.Empty, s.Item?.Barcodes.FirstOrDefault(), s.ExpectedFacings);

    private static ShelfScanDto ToDto(ShelfScan s)
    {
        ShelfScanResult? result = null;
        if (!string.IsNullOrEmpty(s.ResultJson))
        {
            try { result = JsonSerializer.Deserialize<ShelfScanResult>(s.ResultJson, Json); }
            catch (JsonException) { /* corrupt row: show scan without items */ }
        }

        return new ShelfScanDto(
            s.Id, s.LocationId, s.ZoneId, s.ShelfId, s.ImagePath, s.Status, s.MissingCount, s.LowCount,
            s.AlertPending, result?.Summary, result?.Items ?? [], s.Model, s.ErrorMessage, s.CreatedBy, s.CreatedAt);
    }
}
