namespace ShelfGuard.Application.Features.Catalog.Dtos;

/// <summary>
/// Full provider-facing view of a global <c>platform_categories</c> row (B2). Unlike the flat
/// tenant-facing <see cref="CategoryDto"/>, this carries the business-type tags, sort order,
/// active flag and a platform-wide item count — everything the provider category admin UI edits.
/// </summary>
public sealed record PlatformCategoryDto(
    Guid Id, string Name, Guid? ParentId, string[] BusinessTypes,
    int SortOrder, bool IsActive, int ItemCount);

public sealed record CreatePlatformCategoryRequest(
    string Name, Guid? ParentId, string[] BusinessTypes, int? SortOrder);

public sealed record UpdatePlatformCategoryRequest(
    string Name, Guid? ParentId, string[] BusinessTypes, int SortOrder, bool IsActive);
