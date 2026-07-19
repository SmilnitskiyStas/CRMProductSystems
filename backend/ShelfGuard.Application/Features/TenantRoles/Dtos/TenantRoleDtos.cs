namespace ShelfGuard.Application.Features.TenantRoles.Dtos;

public sealed record TenantRoleDto(
    Guid Id,
    string Name,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> AllowedTabs,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public sealed record CreateTenantRoleRequest(
    string Name,
    List<string> Capabilities,
    List<string>? AllowedTabs = null
);

public sealed record UpdateTenantRoleRequest(
    string Name,
    List<string> Capabilities,
    List<string>? AllowedTabs = null
);

/// <summary>Mirrors <see cref="ShelfGuard.Domain.Constants.TenantRoleCapabilityDefinition"/> — see
/// MarketplaceService.GetItemCategories/SupplierItemCategoryDto for the same Domain-constant
/// to Application-DTO mapping pattern (ADR-017 §4).</summary>
public sealed record TenantRoleCapabilityDto(string Key, string LabelUa);

public sealed record TenantRoleCapabilityGroupDto(string Specialty, IReadOnlyList<TenantRoleCapabilityDto> Capabilities);

/// <summary>Mirrors <see cref="ShelfGuard.Domain.Constants.TenantRoleTabDefinition"/> — same
/// Domain-constant to Application-DTO mapping pattern as <see cref="TenantRoleCapabilityDto"/>.
/// Backs GET /api/tenant-roles/tabs (TASK-391b).</summary>
public sealed record TenantRoleTabDto(string Key, string LabelUa);
