using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Catalog;

public sealed class ItemImageService : IItemImageService
{
    public const int MaxImagesPerItem = 8;
    public const long MaxImageBytes = 5 * 1024 * 1024;
    public const int MaxLabelLength = 50;
    public const string UrlPrefix = "/uploads/items/";

    private static readonly HashSet<string> Allowed = [".jpg", ".jpeg", ".png", ".webp", ".gif"];

    private readonly IItemRepository _items;
    private readonly IItemImageRepository _images;

    /// <summary>Folder behind <see cref="UrlPrefix"/>; overridable for tests.</summary>
    public string ItemImagesRoot { get; init; } = Path.Combine("wwwroot", "uploads", "items");

    public ItemImageService(IItemRepository items, IItemImageRepository images)
    {
        _items = items;
        _images = images;
    }

    private async Task<Item?> GuardItemAsync(Guid tenantId, Guid itemId, CancellationToken ct)
    {
        var item = await _items.GetByIdAsync(itemId, ct);
        return item is not null && item.TenantId == tenantId ? item : null;
    }

    public async Task<(List<ItemImageDto>? Images, string? Error)> ListAsync(Guid tenantId, Guid itemId, CancellationToken ct = default)
    {
        if (await GuardItemAsync(tenantId, itemId, ct) is null) return (null, "Product not found.");
        var rows = await _images.GetByItemAsync(itemId, ct);
        return (rows.Select(ToDto).ToList(), null);
    }

    public async Task<(ItemImageDto? Image, string? Error, int? StatusCode)> AddAsync(
        Guid tenantId, Guid itemId, Stream file, long length, string fileName, string? label,
        bool makePrimary = false, CancellationToken ct = default)
    {
        var item = await GuardItemAsync(tenantId, itemId, ct);
        if (item is null) return (null, "Product not found.", 404);

        var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        if (!Allowed.Contains(ext)) return (null, "Unsupported image format.", 400);
        if (length <= 0) return (null, "No file uploaded.", 400);
        if (length > MaxImageBytes) return (null, "File too large. Max 5 MB.", 400);

        var cleanLabel = label?.Trim();
        if (string.IsNullOrEmpty(cleanLabel)) cleanLabel = null;
        if (cleanLabel is { Length: > MaxLabelLength }) return (null, $"Label is too long (max {MaxLabelLength}).", 400);

        var existing = await _images.GetByItemAsync(itemId, ct);
        if (existing.Count >= MaxImagesPerItem) return (null, $"At most {MaxImagesPerItem} images per product.", 400);

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        if (bytes.Length == 0 || bytes.Length > MaxImageBytes) return (null, "File too large. Max 5 MB.", 400);
        if (!MatchesSignature(bytes, ext)) return (null, "File content does not match its image type.", 400);

        var dir = Path.Combine(ItemImagesRoot, tenantId.ToString());
        Directory.CreateDirectory(dir);
        var onDisk = $"{Guid.NewGuid()}{ext}";
        await File.WriteAllBytesAsync(Path.Combine(dir, onDisk), bytes, ct);

        var isPrimary = existing.Count == 0 || makePrimary;
        if (isPrimary && existing.Any(i => i.IsPrimary))
        {
            foreach (var i in existing.Where(i => i.IsPrimary)) i.IsPrimary = false;
            await _images.SaveChangesAsync(ct); // free the unique-primary slot first
        }

        var image = new ItemImage
        {
            TenantId = tenantId,
            ItemId = itemId,
            ImageUrl = $"{UrlPrefix}{tenantId}/{onDisk}",
            Label = cleanLabel,
            SortOrder = existing.Count == 0 ? 0 : existing.Max(i => i.SortOrder) + 1,
            IsPrimary = isPrimary,
        };
        await _images.AddAsync(image, ct);
        if (isPrimary)
        {
            item.ImageUrl = image.ImageUrl;
            _items.Update(item);
        }
        await _images.SaveChangesAsync(ct);

        return (ToDto(image), null, 201);
    }

    public async Task<(ItemImageDto? Image, string? Error, int? StatusCode)> UpdateAsync(
        Guid tenantId, Guid itemId, Guid imageId, UpdateItemImageRequest request, CancellationToken ct = default)
    {
        var item = await GuardItemAsync(tenantId, itemId, ct);
        if (item is null) return (null, "Product not found.", 404);

        var all = await _images.GetByItemAsync(itemId, ct);
        var image = all.FirstOrDefault(i => i.Id == imageId);
        if (image is null) return (null, "Image not found.", 404);

        if (request.IsPrimary == false && image.IsPrimary)
            return (null, "Choose another image as primary instead.", 400);

        var newLabel = image.Label;
        if (request.Label is not null)
        {
            var l = request.Label.Trim();
            if (l.Length > MaxLabelLength) return (null, $"Label is too long (max {MaxLabelLength}).", 400);
            newLabel = l.Length == 0 ? null : l;
        }

        image.Label = newLabel;
        if (request.SortOrder is { } so) image.SortOrder = Math.Max(0, so);
        await _images.SaveChangesAsync(ct);

        if (request.IsPrimary == true && !image.IsPrimary)
        {
            foreach (var i in all.Where(i => i.IsPrimary)) i.IsPrimary = false;
            await _images.SaveChangesAsync(ct); // unset the old primary before setting the new one
            image.IsPrimary = true;
            item.ImageUrl = image.ImageUrl;
            _items.Update(item);
            await _images.SaveChangesAsync(ct);
        }

        return (ToDto(image), null, null);
    }

    public async Task<(bool Ok, string? Error)> DeleteAsync(Guid tenantId, Guid itemId, Guid imageId, CancellationToken ct = default)
    {
        var item = await GuardItemAsync(tenantId, itemId, ct);
        if (item is null) return (false, "Product not found.");
        var all = await _images.GetByItemAsync(itemId, ct);
        var image = all.FirstOrDefault(i => i.Id == imageId);
        if (image is null) return (false, "Image not found.");
        await RemoveAsync(item, all, image, ct);
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> DeletePrimaryAsync(Guid tenantId, Guid itemId, CancellationToken ct = default)
    {
        var item = await GuardItemAsync(tenantId, itemId, ct);
        if (item is null) return (false, "Product not found.");
        var all = await _images.GetByItemAsync(itemId, ct);
        var primary = all.FirstOrDefault(i => i.IsPrimary);
        if (primary is null)
        {
            if (item.ImageUrl is not null)
            {
                item.ImageUrl = null;
                _items.Update(item);
                await _images.SaveChangesAsync(ct);
            }
            return (true, null);
        }
        await RemoveAsync(item, all, primary, ct);
        return (true, null);
    }

    private async Task RemoveAsync(Item item, List<ItemImage> all, ItemImage image, CancellationToken ct)
    {
        _images.Remove(image);
        await _images.SaveChangesAsync(ct); // row gone before promoting (unique primary index)

        if (image.IsPrimary)
        {
            var next = all.Where(i => i.Id != image.Id).OrderBy(i => i.SortOrder).ThenBy(i => i.CreatedAt).FirstOrDefault();
            if (next is not null) next.IsPrimary = true;
            item.ImageUrl = next?.ImageUrl;
            _items.Update(item);
            await _images.SaveChangesAsync(ct);
        }

        TryDeleteFile(image.ImageUrl);
    }

    private void TryDeleteFile(string url)
    {
        try
        {
            if (!url.StartsWith(UrlPrefix, StringComparison.Ordinal)) return;
            var rel = url[UrlPrefix.Length..];
            if (rel.Contains("..") || rel.Contains('\\')) return;
            var root = Path.GetFullPath(ItemImagesRoot);
            var full = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return;
            if (File.Exists(full)) File.Delete(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // best-effort cleanup; an orphan file is harmless
        }
    }

    private static bool MatchesSignature(byte[] b, string ext) => ext switch
    {
        ".jpg" or ".jpeg" => b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF,
        ".png" => b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47,
        ".webp" => b.Length > 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F'
                   && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P',
        ".gif" => b.Length > 6 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F' && b[3] == '8',
        _ => false,
    };

    private static ItemImageDto ToDto(ItemImage i) => new(i.Id, i.ImageUrl, i.Label, i.SortOrder, i.IsPrimary, i.CreatedAt);
}
