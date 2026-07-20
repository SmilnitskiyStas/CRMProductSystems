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
/// A single leaf entry within a <see cref="TenantRoleTabGroupDto"/> — either a group-level
/// backward-compat key (e.g. "operations") when it IS the group's own <c>GroupKey</c>, or an
/// item-level per-page key (e.g. "/inventory", TASK-398) when nested in that group's <c>Items</c>.</summary>
public sealed record TenantRoleTabDto(string Key, string LabelUa);

/// <summary>A section of the hierarchical tab catalog (TASK-398) — mirrors
/// <see cref="ShelfGuard.Domain.Constants.TenantRoleTabGroupDefinition"/>. <see cref="GroupKey"/>
/// is null only for the standalone Dashboard section (Dashboard is a single top-level NavItem in
/// Sidebar.tsx, not a NavGroup); otherwise it is itself an independently-grantable, coarse
/// "whole group" tab key — same value as the corresponding
/// <see cref="ShelfGuard.Domain.Constants.TenantRoleTabs"/> group constant — in addition to each
/// finer-grained key in <see cref="Items"/>. Backs
/// GET /api/tenant-roles/tabs (TASK-398, hierarchy replacing the TASK-391b flat
/// <c>TenantRoleTabDto[]</c> shape) so the frontend "Видимі розділи" editor can group per-page
/// checkboxes under their section without hardcoding the grouping.</summary>
public sealed record TenantRoleTabGroupDto(
    string? GroupKey,
    string GroupLabelUa,
    IReadOnlyList<TenantRoleTabDto> Items);
