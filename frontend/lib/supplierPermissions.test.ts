import { describe, expect, it } from "vitest";
import {
  ALL_SUPPLIER_PERMISSIONS,
  resolveSupplierPermissions,
} from "./supplierPermissions";

// Same merge pattern as providerPermissions.ts but for supplier cabinet staff
// (gates BUG-019's nav items — a bug this exact logic was involved in).
// Zero coverage before this (audit Block 13).

describe("resolveSupplierPermissions", () => {
  it("returns the base set unchanged when there is no override", () => {
    const base = ["catalog_management", "task_board"];
    expect(resolveSupplierPermissions(base)).toEqual(base);
  });

  it("grants an extra permission via a true override", () => {
    const result = resolveSupplierPermissions(["task_board"], { client_management: true });
    expect(result).toContain("client_management");
    expect(result).toContain("task_board");
  });

  it("revokes a base permission via a false override", () => {
    const result = resolveSupplierPermissions(["task_board", "staff_management"], {
      staff_management: false,
    });
    expect(result).toEqual(["task_board"]);
  });

  it("an empty base plus one granted override yields exactly that permission", () => {
    const result = resolveSupplierPermissions([], { profile_management: true });
    expect(result).toEqual(["profile_management"]);
  });

  it("always returns a subset of ALL_SUPPLIER_PERMISSIONS", () => {
    const result = resolveSupplierPermissions(ALL_SUPPLIER_PERMISSIONS, {
      catalog_management: false,
    });
    expect(result.every((p) => ALL_SUPPLIER_PERMISSIONS.includes(p))).toBe(true);
    expect(result).not.toContain("catalog_management");
  });
});
