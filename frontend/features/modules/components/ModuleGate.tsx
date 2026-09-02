"use client";

import type { ReactNode } from "react";
import { useTranslations } from "next-intl";
import { useMe } from "@/features/auth/hooks/useAuth";
import { useModules } from "@/features/modules/hooks/useModules";
import type { ModuleKey } from "@/features/modules/types";

/**
 * Page-level module gate (TASK-674). Wrap a route subtree (via its `layout.tsx`) so every page
 * under it is hidden behind a tenant module being active — mirrors the Sidebar `NavGroup.moduleKey`
 * gate for the direct-URL case, and matches the inline pattern in `marketing-analytics/page.tsx`.
 *
 * - `provider` (bare, no tenant context) bypasses — same as `RequireModuleFilter` and the Sidebar.
 * - While `useMe()` / `useModules()` are still loading, children render (no lock-screen flash) —
 *   same convention as the auto-service / marketplace / marketing-analytics pages.
 * - Backend endpoints carry their own `[RequireModule]`, so this is UX only, not the security
 *   boundary.
 */
export function ModuleGate({ moduleKey, children }: { moduleKey: ModuleKey; children: ReactNode }) {
  const t = useTranslations("Dashboard.modules.gate");
  const { data: me } = useMe();
  const isProvider = me?.role === "provider";
  const { data: modulesData } = useModules(!!me && !isProvider);

  const active = isProvider || !modulesData || modulesData.modules.includes(moduleKey);

  if (active) return <>{children}</>;

  return (
    <div
      style={{
        padding: "80px 32px",
        textAlign: "center",
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        gap: 16,
      }}
    >
      <div style={{ fontSize: 40 }}>🔒</div>
      <h2 style={{ color: "#E8EDF5", fontSize: 20, fontWeight: 700, margin: 0 }}>{t("title")}</h2>
      <p style={{ color: "#4B5563", fontSize: 14, maxWidth: 440, margin: 0 }}>{t("body")}</p>
    </div>
  );
}
