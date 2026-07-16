import { describe, expect, it } from "vitest";
import { ALL_PERMISSIONS, resolvePermissions, SYSTEM_ROLE_PERMISSIONS } from "./providerPermissions";

// Per-user permission override merge logic for the provider team (Sidebar gating,
// team management). Zero coverage before this (audit Block 13).

describe("resolvePermissions", () => {
  it("returns the base set unchanged when there is no override", () => {
    const base = SYSTEM_ROLE_PERMISSIONS.provider_agent;
    expect(resolvePermissions(base)).toEqual(base);
    expect(resolvePermissions(base, null)).toEqual(base);
  });

  it("grants an extra permission via a true override", () => {
    const base = SYSTEM_ROLE_PERMISSIONS.provider_agent; // no "analytics" by default
    const result = resolvePermissions(base, { analytics: true });
    expect(result).toContain("analytics");
  });

  it("revokes a base permission via a false override", () => {
    const base = SYSTEM_ROLE_PERMISSIONS.provider_agent; // includes "live_chat"
    const result = resolvePermissions(base, { live_chat: false });
    expect(result).not.toContain("live_chat");
  });

  it("always returns a subset of ALL_PERMISSIONS, in ALL_PERMISSIONS order", () => {
    const result = resolvePermissions([], { admin_panel: true, marketplace: true });
    expect(result.every((p) => ALL_PERMISSIONS.includes(p))).toBe(true);
    expect(result).toEqual(ALL_PERMISSIONS.filter((p) => result.includes(p)));
  });

  it("ignores an override key that isn't a known permission (no crash, no leak)", () => {
    const base = SYSTEM_ROLE_PERMISSIONS.provider_agent;
    const result = resolvePermissions(base, { not_a_real_permission: true } as Record<
      string,
      boolean
    >);
    expect(result).toEqual(base);
  });
});
