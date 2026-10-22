"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { StatsCards } from "@/features/dashboard/components/StatsCards";
import { AttentionTable } from "@/features/dashboard/components/AttentionTable";
import { QuickActions } from "@/features/dashboard/components/QuickActions";
import { StoreMap } from "@/features/dashboard/components/StoreMap";
import { WeeklyKpiCards } from "@/features/dashboard/components/WeeklyKpiCards";
import { OrdersOverview } from "@/features/dashboard/components/OrdersOverview";
import {
  useAttentionItems,
  useDashboardStats,
  useExpirySummaryCompare,
  useStoreZones,
  useWeeklyKpi,
} from "@/features/dashboard/hooks/useDashboard";
import { useMe } from "@/features/auth/hooks/useAuth";
import { AT_LEAST_STORE_MANAGER, PROVIDER_TEAM, hasRole, type AppRole } from "@/lib/roles";
import { PageContainer } from "@/components/ui/PageContainer";
import { PageHeader } from "@/components/ui/PageHeader";

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
    <PageContainer>
      {/* Page title */}
      <PageHeader title={t("title")} subtitle={t("subtitle")} />

      {/* Stats cards */}
      <StatsCards stats={stats} isLoading={statsLoading} compare={expiryCompare} />

      {/* Weekly KPI cards */}
      <WeeklyKpiCards data={weeklyKpi} isLoading={weeklyKpiLoading} />

      {/* Purchase orders: on order / on the way / delayed (store managers and above) */}
      {hasRole(me?.role, AT_LEAST_STORE_MANAGER) && <OrdersOverview />}

      {/* Main content: table + quick actions */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "1fr 280px",
          gap: 20,
        }}
      >
        <AttentionTable items={attentionItems} isLoading={attentionLoading} stats={stats} />
        <QuickActions items={attentionItems} isLoading={attentionLoading} stats={stats} />
      </div>

      {/* Store map */}
      <StoreMap zones={zones} isLoading={zonesLoading} />
    </PageContainer>
  );
}
