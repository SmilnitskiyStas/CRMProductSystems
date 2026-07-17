"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { StatsCards } from "@/features/dashboard/components/StatsCards";
import { AttentionTable } from "@/features/dashboard/components/AttentionTable";
import { QuickActions } from "@/features/dashboard/components/QuickActions";
import { StoreMap } from "@/features/dashboard/components/StoreMap";
import { WeeklyKpiCards } from "@/features/dashboard/components/WeeklyKpiCards";
import {
  useAttentionItems,
  useDashboardStats,
  useExpirySummaryCompare,
  useStoreZones,
  useWeeklyKpi,
} from "@/features/dashboard/hooks/useDashboard";
import { useMe } from "@/features/auth/hooks/useAuth";
import { PROVIDER_TEAM, type AppRole } from "@/lib/roles";

export default function DashboardPage() {
  const router = useRouter();
  const t = useTranslations("Dashboard.dashboard.page");
  const { data: me } = useMe();
  const { data: stats, isLoading: statsLoading } = useDashboardStats();
  const { data: attentionItems, isLoading: attentionLoading } = useAttentionItems();
  const { data: zones, isLoading: zonesLoading } = useStoreZones();
  const { data: expiryCompare } = useExpirySummaryCompare();
  const { data: weeklyKpi, isLoading: weeklyKpiLoading } = useWeeklyKpi();

  useEffect(() => {
    if (me && PROVIDER_TEAM.has(me.role as AppRole)) router.replace("/provider");
  }, [me, router]);

  return (
    <div style={{ padding: "28px 32px", display: "flex", flexDirection: "column", gap: 20 }}>
      {/* Page title */}
      <div>
        <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>{t("title")}</h1>
        <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6, marginBottom: 0 }}>
          {t("subtitle")}
        </p>
      </div>

      {/* Stats cards */}
      <StatsCards stats={stats} isLoading={statsLoading} compare={expiryCompare} />

      {/* Weekly KPI cards */}
      <WeeklyKpiCards data={weeklyKpi} isLoading={weeklyKpiLoading} />

      {/* Main content: table + quick actions */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "1fr 280px",
          gap: 20,
          alignItems: "start",
        }}
      >
        <AttentionTable items={attentionItems} isLoading={attentionLoading} />
        <QuickActions items={attentionItems} isLoading={attentionLoading} />
      </div>

      {/* Store map */}
      <StoreMap zones={zones} isLoading={zonesLoading} />

    </div>
  );
}
