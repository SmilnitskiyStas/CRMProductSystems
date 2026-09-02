"use client";

import { ModuleGate } from "@/features/modules/components/ModuleGate";

/**
 * TASK-674: every route under /consumer-app (bonus program, tiers, banners, promotions,
 * catalog, app analytics, App Builder, versions) is the "Застосунок" section, gated by the
 * provider-controlled "mobile_app" module. Backend endpoints carry their own
 * [RequireModule("mobile_app")]; this is the direct-URL UX gate (the Sidebar group is hidden
 * by NavGroup.moduleKey).
 */
export default function ConsumerAppLayout({ children }: { children: React.ReactNode }) {
  return <ModuleGate moduleKey="mobile_app">{children}</ModuleGate>;
}
