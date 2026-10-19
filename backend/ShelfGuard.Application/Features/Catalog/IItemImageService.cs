namespace ShelfGuard.Application.Features.Catalog;

public sealed record ItemImageDto(Guid Id, string Url, string? Label, int SortOrder, bool IsPrimary, DateTime CreatedAt);

/// <summary>All fields optional; Label = "" clears it. IsPrimary=false on the current primary is rejected.</summary>
public sealed record UpdateItemImageRequest(string? Label, bool? IsPrimary, int? SortOrder);

public interface IItemImageService
{
    Task<(List<ItemImageDto>? Images, string? Error)> ListAsync(Guid tenantId, Guid itemId, CancellationToken ct = default);

    /// <param name="makePrimary">Legacy single-image endpoint: the uploaded image becomes primary even if others exist.</param>
    Task<(ItemImageDto? Image, string? Error, int? StatusCode)> AddAsync(
        Guid tenantId, Guid itemId, Stream file, long length, string fileName, string? label,
        bool makePrimary = false, CancellationToken ct = default);

    Task<(ItemImageDto? Image, string? Error, int? StatusCode)> UpdateAsync(
        Guid tenantId, Guid itemId, Guid imageId, UpdateItemImageRequest request, CancellationToken ct = default);

    Task<(bool Ok, string? Error)> DeleteAsync(Guid tenantId, Guid itemId, Guid imageId, CancellationToken ct = default);

    /// <summary>Legacy DELETE /items/{id}/image: removes the primary image and promotes the next.</summary>
    Task<(bool Ok, string? Error)> DeletePrimaryAsync(Guid tenantId, Guid itemId, CancellationToken ct = default);
}
