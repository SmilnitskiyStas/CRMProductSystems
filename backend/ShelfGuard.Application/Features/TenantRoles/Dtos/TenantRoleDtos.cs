namespace ShelfGuard.Application.Features.TenantRoles.Dtos;

public sealed record TenantRoleDto(
    Guid Id,
    string Name,
    IReadOnlyList<string> Capabilities,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public sealed record CreateTenantRoleRequest(
    string Name,
    List<string> Capabilities
);

public sealed record UpdateTenantRoleRequest(
    string Name,
    List<string> Capabilities
);

/// <summary>Mirrors <see cref="ShelfGuard.Domain.Constants.TenantRoleCapabilityDefinition"/> — see
/// MarketplaceService.GetItemCategories/SupplierItemCategoryDto for the same Domain-constant
/// to Application-DTO mapping pattern (ADR-017 §4).</summary>
public sealed record TenantRoleCapabilityDto(string Key, string LabelUa);

public sealed record TenantRoleCapabilityGroupDto(string Specialty, IReadOnlyList<TenantRoleCapabilityDto> Capabilities);
