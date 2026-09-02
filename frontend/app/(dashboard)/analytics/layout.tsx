"use client";

import { ModuleGate } from "@/features/modules/components/ModuleGate";

/**
 * TASK-674: /analytics and /analytics/pos are the "Аналітика" reports section, gated by the
 * provider-controlled "analytics" module. The dashboard-home and Events-calendar endpoints that
 * happen to live on AnalyticsController are NOT under this route and stay ungated.
 */
export default function AnalyticsLayout({ children }: { children: React.ReactNode }) {
  return <ModuleGate moduleKey="analytics">{children}</ModuleGate>;
}
