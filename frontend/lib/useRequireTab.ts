"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { useMe } from "@/features/auth/hooks/useAuth";

/**
 * Route guard for TenantRole sidebar-tab visibility (TASK-391c, Feature 1).
 *
 * `TenantRole.AllowedTabs` is a UI-only visibility signal — no backend endpoint enforces
 * it yet (Tier 2, deliberately deferred; see backend/ShelfGuard.Domain/Constants/
 * TenantRoleTabs.cs). This hook must never be the ONLY thing standing between a user and
 * a page they already reach today via the ordinary role/capability system, so callers
 * pass `alreadyAllowed` computed the same way Sidebar.tsx decides whether to show the
 * matching NavItem (role check, permission check, or "no restriction" — whatever
 * currently governs that route). Effective access = alreadyAllowed OR the tabs claim,
 * the same additive-OR shape Sidebar.tsx uses for effectivePermissions/
 * supplierEffectivePermissions/tabsSet.
 *
 * A user with no `tabs` claim at all (no TenantRoleId — most users today, this is an
 * opt-in per-role feature) gets zero additional effect here: `alreadyAllowed` alone
 * decides their access, exactly as before this feature existed.
 *
 * Redirects to /dashboard when neither grants access. Waits for `useMe()` to resolve
 * before judging (undefined `me` never redirects) so the loading flash on a legitimately
 * allowed page never bounces the user.
 *
 * Returns the combined `effectiveAccess` boolean so a page that already renders its own
 * "access denied" UI (e.g. analytics) can reuse the same value instead of recomputing it.
 */
export function useRequireTab(tabKey: string, alreadyAllowed: boolean): boolean {
  const { data: me, isLoading } = useMe();
  const router = useRouter();

  const hasTabAccess = Array.isArray(me?.tabs) && me.tabs.includes(tabKey);
  const effectiveAccess = alreadyAllowed || hasTabAccess;

  useEffect(() => {
    if (isLoading || !me) return;
    if (!effectiveAccess) router.replace("/dashboard");
  }, [isLoading, me, effectiveAccess, router]);

  return effectiveAccess;
}
