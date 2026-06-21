"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useEffect } from "react";
import { Users, BarChart2 } from "lucide-react";
import { Shield } from "lucide-react";
import { useMe } from "@/features/auth/hooks/useAuth";
import { PROVIDER_TEAM } from "@/lib/roles";
import type { AppRole } from "@/lib/roles";
import { TeamTab } from "@/features/provider/components/TeamTab";
import { StatsTab } from "@/features/provider/components/StatsTab";
import { RolesSection } from "@/features/provider/components/RolesSection";

type Tab = "team" | "stats" | "roles";

export default function ProviderTeamPage() {
  const router = useRouter();
  const { data: me, isLoading } = useMe();
  const [activeTab, setActiveTab] = useState<Tab>("team");

  useEffect(() => {
    if (!isLoading && me && !PROVIDER_TEAM.has((me.role ?? "") as AppRole)) {
      router.replace("/dashboard");
    }
  }, [me, isLoading, router]);

  if (isLoading) {
    return (
      <div style={{ display: "flex", alignItems: "center", justifyContent: "center", height: "60vh" }}>
        <div style={{ color: "#4B5563", fontSize: 14 }}>Завантаження…</div>
      </div>
    );
  }

  if (!me || !PROVIDER_TEAM.has((me.role ?? "") as AppRole)) return null;

  const tabStyle = (key: Tab): React.CSSProperties => ({
    display: "flex", alignItems: "center", gap: 6,
    padding: "8px 16px", borderRadius: 8, fontSize: 13, cursor: "pointer",
    fontWeight: activeTab === key ? 600 : 400,
    background: activeTab === key ? "#1D3461" : "transparent",
    border: `1px solid ${activeTab === key ? "#3B82F6" : "transparent"}`,
    color: activeTab === key ? "#93C5FD" : "#6B7280",
    whiteSpace: "nowrap" as const,
  });

  return (
    <div style={{ padding: "28px 32px" }}>
      {/* Header */}
      <div style={{ marginBottom: 24 }}>
        <div style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: 4 }}>
          <Users size={20} style={{ color: "#3B82F6" }} />
          <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
            Команда провайдера
          </h1>
        </div>
        <p style={{ color: "#4B5563", fontSize: 13, margin: 0 }}>
          Управління учасниками команди та перегляд статистики
        </p>
      </div>

      {/* Tabs */}
      <div style={{ display: "flex", gap: 4, marginBottom: 20 }}>
        <button style={tabStyle("team")} onClick={() => setActiveTab("team")}>
          <Users size={14} /> Команда
        </button>
        <button style={tabStyle("stats")} onClick={() => setActiveTab("stats")}>
          <BarChart2 size={14} /> Статистика
        </button>
        <button style={tabStyle("roles")} onClick={() => setActiveTab("roles")}>
          <Shield size={14} /> Ролі
        </button>
      </div>

      {activeTab === "team"  && <TeamTab />}
      {activeTab === "stats" && <StatsTab />}
      {activeTab === "roles" && <RolesSection standalone />}
    </div>
  );
}
