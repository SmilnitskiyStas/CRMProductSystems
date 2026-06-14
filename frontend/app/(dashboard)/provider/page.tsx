"use client";

import { useState, useEffect } from "react";
import { useRouter } from "next/navigation";
import { Shield, Users, Building2, AlertTriangle, Activity, RefreshCw } from "lucide-react";
import { useMe } from "@/features/auth/hooks/useAuth";
import { useTenants, useProviderHealth } from "@/features/provider/hooks/useProvider";
import { TenantCard } from "@/features/provider/components/TenantCard";
import { TenantDetailPanel } from "@/features/provider/components/TenantDetailPanel";
import { ProviderLogsPanel } from "@/features/provider/components/ProviderLogsPanel";

export default function ProviderPage() {
  const router = useRouter();
  const { data: me, isLoading: meLoading } = useMe();

  const { data: tenants, isLoading: tenantsLoading, refetch: refetchTenants } = useTenants();
  const { data: health }  = useProviderHealth();

  const [search,         setSearch]         = useState("");
  const [activeTab,      setActiveTab]      = useState<"tenants" | "logs">("tenants");
  const [selectedId,     setSelectedId]     = useState<string | null>(null);
  const [logsForTenant,  setLogsForTenant]  = useState<string | undefined>(undefined);

  // Role guard — redirect non-provider users
  useEffect(() => {
    if (!meLoading && me && me.role !== "provider") {
      router.replace("/dashboard");
    }
  }, [me, meLoading, router]);

  if (meLoading) {
    return (
      <div style={{ display: "flex", alignItems: "center", justifyContent: "center", height: "60vh" }}>
        <div style={{ color: "#4B5563", fontSize: 14 }}>Завантаження…</div>
      </div>
    );
  }

  if (me?.role !== "provider") return null;

  const filtered = (tenants ?? []).filter((t) =>
    t.name.toLowerCase().includes(search.toLowerCase()) ||
    t.slug.toLowerCase().includes(search.toLowerCase()),
  );

  const stats = [
    {
      label: "Всього клієнтів",
      value: health?.totalTenants   ?? (tenants?.length ?? 0),
      icon: <Building2 size={18} />,
      color: "#60A5FA",
    },
    {
      label: "Активних",
      value: health?.activeTenants  ?? (tenants?.filter((t) => t.isActive).length ?? 0),
      icon: <Shield size={18} />,
      color: "#4ADE80",
    },
    {
      label: "Всього користувачів",
      value: health?.totalUsers     ?? "—",
      icon: <Users size={18} />,
      color: "#A78BFA",
    },
    {
      label: "Протерм. партій",
      value: health?.totalExpiredBatches ?? "—",
      icon: <AlertTriangle size={18} />,
      color: (health?.totalExpiredBatches ?? 0) > 0 ? "#F87171" : "#4ADE80",
    },
  ];

  return (
    <>
      {/* Flex row: main content + inline detail panel */}
      <div style={{ display: "flex", alignItems: "flex-start", minHeight: "100vh" }}>

      <div
        style={{
          flex: 1,
          minWidth: 0,
          padding: "28px 32px",
        }}
      >
        {/* Header */}
        <div style={{ display: "flex", alignItems: "flex-start", justifyContent: "space-between", marginBottom: 28 }}>
          <div>
            <div style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: 4 }}>
              <div
                style={{
                  width: 34, height: 34,
                  borderRadius: 9,
                  background: "linear-gradient(135deg, #7C3AED, #3B82F6)",
                  display: "flex", alignItems: "center", justifyContent: "center",
                  flexShrink: 0,
                }}
              >
                <Shield size={17} color="#fff" />
              </div>
              <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
                Панель провайдера
              </h1>
            </div>
            <p style={{ color: "#4B5563", fontSize: 14, margin: 0 }}>
              Управління клієнтами платформи ShelfGuard
            </p>
          </div>

          <button
            onClick={() => refetchTenants()}
            style={{
              display: "flex", alignItems: "center", gap: 6,
              padding: "8px 14px", borderRadius: 8,
              background: "transparent", border: "1px solid #1F2937",
              color: "#6B7280", fontSize: 13, cursor: "pointer",
            }}
          >
            <RefreshCw size={14} />
            Оновити
          </button>
        </div>

        {/* Stats row */}
        <div style={{ display: "grid", gridTemplateColumns: "repeat(4, 1fr)", gap: 12, marginBottom: 28 }}>
          {stats.map((stat) => (
            <div
              key={stat.label}
              style={{
                background: "#0D1117",
                border: "1px solid #1F2937",
                borderRadius: 10,
                padding: "14px 18px",
                display: "flex",
                alignItems: "center",
                gap: 14,
              }}
            >
              <div style={{ color: stat.color, opacity: 0.8 }}>{stat.icon}</div>
              <div>
                <div style={{ color: stat.color, fontSize: 22, fontWeight: 700, lineHeight: 1 }}>
                  {stat.value}
                </div>
                <div style={{ color: "#4B5563", fontSize: 12, marginTop: 4 }}>{stat.label}</div>
              </div>
            </div>
          ))}
        </div>

        {/* Tabs */}
        <div style={{ display: "flex", gap: 4, marginBottom: 20 }}>
          {(["tenants", "logs"] as const).map((tab) => (
            <button
              key={tab}
              onClick={() => setActiveTab(tab)}
              style={{
                padding: "8px 16px",
                borderRadius: 8,
                fontSize: 13,
                fontWeight: activeTab === tab ? 600 : 400,
                cursor: "pointer",
                background: activeTab === tab ? "#1D3461" : "transparent",
                border: `1px solid ${activeTab === tab ? "#3B82F6" : "transparent"}`,
                color: activeTab === tab ? "#93C5FD" : "#6B7280",
              }}
            >
              {tab === "tenants" ? (
                <span style={{ display: "flex", alignItems: "center", gap: 6 }}>
                  <Building2 size={14} />
                  Клієнти ({tenants?.length ?? 0})
                </span>
              ) : (
                <span style={{ display: "flex", alignItems: "center", gap: 6 }}>
                  <Activity size={14} />
                  Логи
                </span>
              )}
            </button>
          ))}
        </div>

        {/* Tenants tab */}
        {activeTab === "tenants" && (
          <>
            {/* Search */}
            <div style={{ marginBottom: 20 }}>
              <input
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Пошук за назвою або ідентифікатором…"
                style={{
                  width: "100%",
                  maxWidth: 380,
                  background: "#0D1117",
                  border: "1px solid #1F2937",
                  borderRadius: 8,
                  padding: "9px 14px",
                  color: "#E8EDF5",
                  fontSize: 13,
                  outline: "none",
                  boxSizing: "border-box",
                }}
              />
            </div>

            {/* Grid */}
            {tenantsLoading ? (
              <div style={{ color: "#4B5563", fontSize: 14 }}>Завантаження клієнтів…</div>
            ) : filtered.length === 0 ? (
              <div style={{ color: "#4B5563", fontSize: 14 }}>
                {search ? "Нічого не знайдено" : "Клієнтів немає"}
              </div>
            ) : (
              <div
                style={{
                  display: "grid",
                  gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
                  gap: 14,
                }}
              >
                {filtered.map((tenant) => (
                  <TenantCard
                    key={tenant.id}
                    tenant={tenant}
                    onClick={() => setSelectedId(tenant.id === selectedId ? null : tenant.id)}
                  />
                ))}
              </div>
            )}
          </>
        )}

        {/* Logs tab */}
        {activeTab === "logs" && (
          <div
            style={{
              background: "#0D1117",
              border: "1px solid #1F2937",
              borderRadius: 12,
              padding: "16px 20px",
            }}
          >
            <ProviderLogsPanel initialTenantId={logsForTenant} />
          </div>
        )}
      </div>{/* end main content */}

      {/* Inline detail panel — takes its own column in the flex row */}
      {selectedId && (
        <TenantDetailPanel
          tenantId={selectedId}
          onClose={() => setSelectedId(null)}
          onImpersonated={() => setSelectedId(null)}
          onViewLogs={(tid) => {
            setLogsForTenant(tid);
            setActiveTab("logs");
            setSelectedId(null);
          }}
        />
      )}

      </div>{/* end flex row */}
    </>
  );
}
