namespace ShelfGuard.Application.Features.Catalog.Dtos;

/// <summary>Flat lookup row for the catalog filter dropdown (TASK-632). No pagination, no parent/tree shape.</summary>
public sealed record CategoryDto(Guid Id, string Name, Guid? ParentId = null);
