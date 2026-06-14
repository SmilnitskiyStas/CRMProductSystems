"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { StatsCards } from "@/features/dashboard/components/StatsCards";
import { AttentionTable } from "@/features/dashboard/components/AttentionTable";
import { QuickActions } from "@/features/dashboard/components/QuickActions";
import { StoreMap } from "@/features/dashboard/components/StoreMap";
import {
  useAttentionItems,
  useDashboardStats,
  useStoreZones,
} from "@/features/dashboard/hooks/useDashboard";
import { useMe } from "@/features/auth/hooks/useAuth";

export default function DashboardPage() {
  const router = useRouter();
  const { data: me } = useMe();
  const { data: stats, isLoading: statsLoading } = useDashboardStats();
  const { data: attentionItems, isLoading: attentionLoading } = useAttentionItems();
  const { data: zones, isLoading: zonesLoading } = useStoreZones();

  useEffect(() => {
    if (me?.role === "provider") router.replace("/provider");
  }, [me, router]);

  return (
    <div style={{ padding: "28px 32px", display: "flex", flexDirection: "column", gap: 20 }}>
      {/* Page title */}
      <div>
        <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>Дашборд</h1>
        <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6, marginBottom: 0 }}>
          Огляд магазину — терміни придатності та залишки
        </p>
      </div>

      {/* Stats cards */}
      <StatsCards stats={stats} isLoading={statsLoading} />

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
