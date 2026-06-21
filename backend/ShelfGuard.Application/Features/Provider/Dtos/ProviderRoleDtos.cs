namespace ShelfGuard.Application.Features.Provider.Dtos;

public record ProviderRoleDto(
    Guid     Id,
    string   DisplayName,
    string   BaseRole,
    string[] Permissions,
    bool     IsSystem
);

public record CreateProviderRoleRequest(
    string   DisplayName,
    string   BaseRole,
    string[] Permissions
);

public record UpdateProviderRoleRequest(
    string   DisplayName,
    string   BaseRole,
    string[] Permissions
);
