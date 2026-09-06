import { describe, expect, it } from "vitest";
import {
  AppRoles,
  AT_LEAST_ENTERPRISE_ADMIN,
  CAN_ACCESS_POS,
  canManageLegalEntities,
  canViewIntegrations,
  hasRole,
} from "./roles";

// RBAC gating logic — every sidebar item, route guard, and permission check in
// the app funnels through hasRole()/the role sets here (mirrors backend
// AppPolicies). Zero coverage before this (audit Block 13).

describe("hasRole", () => {
  it("returns true when the role is in the allowed set", () => {
    expect(hasRole(AppRoles.Cashier, CAN_ACCESS_POS)).toBe(true);
  });

  it("returns false when the role is not in the allowed set", () => {
    expect(hasRole(AppRoles.Merchandiser, CAN_ACCESS_POS)).toBe(false);
  });

  it("returns false for undefined role (logged-out / not-yet-loaded state)", () => {
    expect(hasRole(undefined, CAN_ACCESS_POS)).toBe(false);
  });

  it("returns false for an unknown/garbage role string", () => {
    expect(hasRole("not_a_real_role", CAN_ACCESS_POS)).toBe(false);
  });
});

describe("canManageLegalEntities", () => {
  it("allows enterprise_admin unconditionally, even with no permission overrides", () => {
    expect(canManageLegalEntities(AppRoles.EnterpriseAdmin, null)).toBe(true);
  });

  it("allows provider unconditionally", () => {
    expect(canManageLegalEntities(AppRoles.Provider, undefined)).toBe(true);
  });

  it("denies a lower role with no override", () => {
    expect(canManageLegalEntities(AppRoles.StoreManager, null)).toBe(false);
  });

  it("allows a lower role when granted the explicit permission override", () => {
    expect(
      canManageLegalEntities(AppRoles.StoreManager, { "legal_entities.manage": true }),
    ).toBe(true);
  });

  it("denies a lower role when the override is present but false", () => {
    expect(
      canManageLegalEntities(AppRoles.StoreManager, { "legal_entities.manage": false }),
    ).toBe(false);
  });

  it("is unaffected by unrelated permission keys", () => {
    expect(
      canManageLegalEntities(AppRoles.StoreManager, { "some.other.permission": true }),
    ).toBe(false);
  });
});

describe("canViewIntegrations (Settings → Інтеграції tab gate)", () => {
  it("allows store_manager and above unconditionally", () => {
    expect(canViewIntegrations(AppRoles.StoreManager, null)).toBe(true);
    expect(canViewIntegrations(AppRoles.EnterpriseAdmin, undefined)).toBe(true);
  });

  it("denies a lower role with no capability", () => {
    expect(canViewIntegrations(AppRoles.Cashier, null)).toBe(false);
    expect(canViewIntegrations(AppRoles.Storekeeper, [])).toBe(false);
  });

  it("allows a lower role holding the integrations.view capability", () => {
    expect(canViewIntegrations(AppRoles.Storekeeper, ["integrations.view"])).toBe(true);
  });

  it("is unaffected by unrelated capability keys", () => {
    expect(canViewIntegrations(AppRoles.Cashier, ["analytics.view"])).toBe(false);
  });

  it("denies the not-yet-loaded state (undefined role, no capabilities)", () => {
    expect(canViewIntegrations(undefined, null)).toBe(false);
    expect(canViewIntegrations(undefined, undefined)).toBe(false);
  });
});

describe("role set sanity (guards against accidental edits widening access)", () => {
  it("AT_LEAST_ENTERPRISE_ADMIN is exactly {provider, enterprise_admin}", () => {
    expect([...AT_LEAST_ENTERPRISE_ADMIN].sort()).toEqual(
      [AppRoles.Provider, AppRoles.EnterpriseAdmin].sort(),
    );
  });

  it("supplier_admin can never access tenant-staff-only sets like CAN_ACCESS_POS", () => {
    expect(hasRole(AppRoles.SupplierAdmin, CAN_ACCESS_POS)).toBe(false);
  });
});
