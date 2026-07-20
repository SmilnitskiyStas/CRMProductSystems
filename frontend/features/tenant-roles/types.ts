// TenantRole — custom capability-template roles (ADR-020, TASK-345/346/348).
// Backend source of truth: backend/ShelfGuard.Application/Features/TenantRoles/Dtos/TenantRoleDtos.cs
// and backend/ShelfGuard.Domain/Constants/TenantRoleCapabilities.cs.

export interface TenantRoleDto {
  id: string;
  name: string;
  /** Capability keys, format "module.action" (e.g. "users.manage"). */
  capabilities: string[];
  /** Sidebar tab keys (e.g. "workforce", "analytics") — TASK-391/391b, a separate axis
   *  from `capabilities`: controls whether a whole nav section is visible, not what
   *  actions the user can perform. UI-only signal, no Tier 2 backend enforcement yet. */
  allowedTabs: string[];
  /** false once archived (DELETE /api/tenant-roles/:id) — never hard-deleted, users may still reference it. */
  isActive: boolean;
  createdAt: string;
  updatedAt?: string | null;
}

export interface CreateTenantRoleRequest {
  name: string;
  capabilities: string[];
  allowedTabs: string[];
}

export interface UpdateTenantRoleRequest {
  name: string;
  capabilities: string[];
  allowedTabs: string[];
}

/** A single grantable capability — label is backend-sourced Ukrainian text, never hardcode it here. */
export interface TenantRoleCapabilityDto {
  key: string;
  labelUa: string;
}

/** A specialty group of capabilities (e.g. "HR", "Бухгалтер / Фінансист", "Закупка").
 *  Backend (GET /api/tenant-roles/capabilities) is the source of truth for both the
 *  grouping and the group names — never hardcode groups on the frontend (ADR-020 point 9). */
export interface TenantRoleCapabilityGroup {
  specialty: string;
  capabilities: TenantRoleCapabilityDto[];
}

/** A single grantable sidebar tab — label is backend-sourced Ukrainian text, never
 *  hardcode it here. A leaf entry inside a `TenantTabGroupDto["items"]` — either a
 *  group-level backward-compat key (e.g. "operations", when it equals the parent's own
 *  `groupKey`) or an item-level per-page key (e.g. "/inventory", TASK-398/399). Mirrors
 *  backend TenantRoleTabDto. */
export interface TenantTabDto {
  key: string;
  labelUa: string;
}

/** A section of the hierarchical sidebar-tab catalog (TASK-398) — mirrors backend
 *  TenantRoleTabGroupDto. Backs GET /api/tenant-roles/tabs (supersedes TASK-391b's flat
 *  `TenantTabDto[]` shape). `groupKey` is null only for the standalone Dashboard section
 *  (Dashboard is a single top-level NavItem in Sidebar.tsx, not a NavGroup); otherwise it
 *  is itself an independently-grantable, coarse "whole group" key — same value as the
 *  matching `NavGroup.key` in Sidebar.tsx — in addition to each finer-grained key in
 *  `items`. Never hardcode `groupLabelUa`/labels here — always backend-sourced. */
export interface TenantTabGroupDto {
  groupKey: string | null;
  groupLabelUa: string;
  items: TenantTabDto[];
}

/** Body for POST /api/users/:id/tenant-role — null clears the assignment. */
export interface AssignTenantRoleRequest {
  tenantRoleId: string | null;
}
