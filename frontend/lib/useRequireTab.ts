"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { useMe } from "@/features/auth/hooks/useAuth";

/**
 * Route guard for TenantRole sidebar-tab visibility (TASK-391c, Feature 1; made
 * authoritative/exclusive in TASK-397 to match Sidebar.tsx's tabsSet override — see that
 * file's group-items filter for the sibling logic this mirrors).
 *
 * `TenantRole.AllowedTabs` is a UI-only visibility signal — no backend endpoint enforces
 * it yet (Tier 2, deliberately deferred; see backend/ShelfGuard.Domain/Constants/
 * TenantRoleTabs.cs). Callers pass `alreadyAllowed` computed the same way Sidebar.tsx
 * decides whether to show the matching NavItem (role check, permission check, or "no
 * restriction" — whatever currently governs that route).
 *
 * Effective access:
 * - Non-empty `tabs` claim (the user's effective TenantRole has "Видимі розділи"
 *   configured) → the tabs claim is AUTHORITATIVE and completely REPLACES
 *   `alreadyAllowed`, in both directions: it can grant access `alreadyAllowed` wouldn't,
 *   and — this is the actual behavior change from TASK-391c's original OR shape — it can
 *   also BLOCK access `alreadyAllowed` would otherwise grant, when this page's tab key
 *   isn't in it. A non-empty tabsSet that excludes a page's tab key must block direct
 *   navigation too, not just hide the sidebar link, or the two would disagree.
 * - Empty/absent `tabs` claim (no TenantRoleId, or a TenantRole that never had its tabs
 *   configured — the default, and the common case for existing capability-only TenantRole
 *   users) → falls back to `alreadyAllowed` exactly as before this override existed.
 *
 * Redirects to /dashboard when access is denied. Waits for `useMe()` to resolve before
 * judging (undefined `me` never redirects) so the loading flash on a legitimately allowed
 * page never bounces the user.
 *
 * Returns the combined `effectiveAccess` boolean so a page that already renders its own
 * "access denied" UI (e.g. analytics) can reuse the same value instead of recomputing it.
 */
export function useRequireTab(tabKey: string, alreadyAllowed: boolean): boolean {
  const { data: me, isLoading } = useMe();
  const router = useRouter();

  const hasTabsClaim = Array.isArray(me?.tabs) && me.tabs.length > 0;
  const effectiveAccess = hasTabsClaim ? me!.tabs!.includes(tabKey) : alreadyAllowed;

  useEffect(() => {
    if (isLoading || !me) return;
    if (!effectiveAccess) router.replace("/dashboard");
  }, [isLoading, me, effectiveAccess, router]);

  return effectiveAccess;
}
