namespace ShelfGuard.Application.Features.Provider.Dtos;

/// <summary>Represents an admin user belonging to a tenant.</summary>
public record TenantUserDto(
    Guid     Id,
    string   FullName,
    string   Email,
    string   Role,
    DateTime CreatedAt);
