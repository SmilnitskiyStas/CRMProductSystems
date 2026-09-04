namespace ShelfGuard.Application.Features.Catalog.Dtos;

/// <summary>
/// Item-attribute defaults the product form pre-fills when a merchandiser picks this category
/// (Slice 2). Every field is optional — <c>null</c> means "no suggestion for this field".
/// </summary>
public sealed record CategoryDefaults(
    decimal? VatRate,
    string? PerishabilityClass,
    string? ManagementType,
    string? ItemType,
    int? ShelfLifeDays)
{
    public static readonly CategoryDefaults Empty = new(null, null, null, null, null);
}

/// <summary>
/// Full provider-facing view of a global <c>platform_categories</c> row (B2). Unlike the flat
/// tenant-facing <see cref="CategoryDto"/>, this carries the business-type tags, sort order,
/// active flag and a platform-wide item count — everything the provider category admin UI edits.
/// </summary>
public sealed record PlatformCategoryDto(
    Guid Id, string Name, Guid? ParentId, string[] BusinessTypes,
    int SortOrder, bool IsActive, int ItemCount, CategoryDefaults Defaults);

public sealed record CreatePlatformCategoryRequest(
    string Name, Guid? ParentId, string[] BusinessTypes, int? SortOrder, CategoryDefaults? Defaults = null);

public sealed record UpdatePlatformCategoryRequest(
    string Name, Guid? ParentId, string[] BusinessTypes, int SortOrder, bool IsActive, CategoryDefaults? Defaults = null);
