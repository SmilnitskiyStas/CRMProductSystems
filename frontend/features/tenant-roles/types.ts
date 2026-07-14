// TenantRole — custom capability-template roles (ADR-020, TASK-345/346/348).
// Backend source of truth: backend/ShelfGuard.Application/Features/TenantRoles/Dtos/TenantRoleDtos.cs
// and backend/ShelfGuard.Domain/Constants/TenantRoleCapabilities.cs.

export interface TenantRoleDto {
  id: string;
  name: string;
  /** Capability keys, format "module.action" (e.g. "users.manage"). */
  capabilities: string[];
  /** false once archived (DELETE /api/tenant-roles/:id) — never hard-deleted, users may still reference it. */
  isActive: boolean;
  createdAt: string;
  updatedAt?: string | null;
}

export interface CreateTenantRoleRequest {
  name: string;
  capabilities: string[];
}

export interface UpdateTenantRoleRequest {
  name: string;
  capabilities: string[];
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

/** Body for POST /api/users/:id/tenant-role — null clears the assignment. */
export interface AssignTenantRoleRequest {
  tenantRoleId: string | null;
}
