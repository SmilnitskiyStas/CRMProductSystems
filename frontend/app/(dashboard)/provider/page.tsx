"use client";

import { useState, useEffect } from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { Shield, Building2, AlertTriangle, Activity, RefreshCw, Users, Plus, Truck } from "lucide-react";
import { useMe } from "@/features/auth/hooks/useAuth";
import { Btn } from "@/components/ui/Btn";
import { PageContainer } from "@/components/ui/PageContainer";
import { PageHeader } from "@/components/ui/PageHeader";
import { Tabs } from "@/components/ui/Tabs";
import { inputStyle } from "@/components/ui/fieldStyles";
import { useTenants, useProviderHealth } from "@/features/provider/hooks/useProvider";
import { TenantCard } from "@/features/provider/components/TenantCard";
import { TenantDetailPanel } from "@/features/provider/components/TenantDetailPanel";
import { ProviderLogsPanel } from "@/features/provider/components/ProviderLogsPanel";
import { CreateTenantWizard } from "@/features/provider/components/CreateTenantWizard";

const PROVIDER_ROLES = ["provider", "provider_admin", "provider_agent"];

export default function ProviderPage() {
  const t = useTranslations("Dashboard.provider.page");
  const router = useRouter();
  const { data: me, isLoading: meLoading } = useMe();

  const { data: tenants, isLoading: tenantsLoading, refetch: refetchTenants } = useTenants();
  const { data: health }  = useProviderHealth();

  const [search,         setSearch]         = useState("");
  const [activeTab,      setActiveTab]      = useState<"clients" | "suppliers" | "logs">("clients");
  const [selectedId,     setSelectedId]     = useState<string | null>(null);
  const [logsForTenant,  setLogsForTenant]  = useState<string | undefined>(undefined);
  const [showWizard,     setShowWizard]     = useState(false);

  // Role guard — redirect non-provider users
  useEffect(() => {
    if (!meLoading && me && !PROVIDER_ROLES.includes(me.role)) {
      router.replace("/dashboard");
    }
  }, [me, meLoading, router]);

  if (meLoading) {
    return (
      <div style={{ display: "flex", alignItems: "center", justifyContent: "center", height: "60vh" }}>
        <div style={{ color: "#4B5563", fontSize: 14 }}>{t("loading")}</div>
      </div>
    );
  }

  if (!me || !PROVIDER_ROLES.includes(me.role)) return null;

  const clientTenants = (tenants ?? []).filter((tn) => tn.businessType !== "supplier");
  const supplierTenants = (tenants ?? []).filter((tn) => tn.businessType === "supplier");

  const bySearch = (tn: { name: string; slug: string }) =>
    tn.name.toLowerCase().includes(search.toLowerCase()) ||
    tn.slug.toLowerCase().includes(search.toLowerCase());

  const filtered =
    activeTab === "suppliers"
      ? supplierTenants.filter(bySearch)
      : activeTab === "clients"
        ? clientTenants.filter(bySearch)
        : [];

  const stats = [
    {
      label: t("statTotalClients"),
      value: health?.totalTenants   ?? (tenants?.length ?? 0),
      icon: <Building2 size={18} />,
      color: "#60A5FA",
    },
    {
      label: t("statActive"),
      value: health?.activeTenants  ?? (tenants?.filter((tn) => tn.isActive).length ?? 0),
      icon: <Shield size={18} />,
      color: "#4ADE80",
    },
    {
      label: t("statTotalUsers"),
      value: health?.totalUsers     ?? "—",
      icon: <Users size={18} />,
      color: "#A78BFA",
    },
    {
      label: t("statExpiredBatches"),
      value: health?.totalExpiredBatches ?? "—",
      icon: <AlertTriangle size={18} />,
      color: (health?.totalExpiredBatches ?? 0) > 0 ? "#F87171" : "#4ADE80",
    },
  ];

  return (
    <>
      {showWizard && (
        <CreateTenantWizard
          onClose={() => setShowWizard(false)}
          onCreated={(id) => {
            setShowWizard(false);
            setSelectedId(id);
          }}
        />
      )}

      {/* Flex row: main content + inline detail panel */}
      <div style={{ display: "flex", alignItems: "flex-start", minHeight: "100vh" }}>

      <PageContainer style={{ flex: 1, minWidth: 0 }}>
        <PageHeader
          title={
            <span style={{ display: "inline-flex", alignItems: "center", gap: 10 }}>
              <span
                style={{
                  width: 34,
                  height: 34,
                  borderRadius: 9,
                  background: "linear-gradient(135deg, #7C3AED, #3B82F6)",
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "center",
                  flexShrink: 0,
                }}
              >
                <Shield size={17} color="#fff" />
              </span>
              {t("title")}
            </span>
          }
          subtitle={t("subtitle")}
          actions={
            <>
              <Btn icon={<Plus size={15} />} onClick={() => setShowWizard(true)}>
                {t("newClientButton")}
              </Btn>
              <Btn variant="ghost" icon={<RefreshCw size={14} />} onClick={() => refetchTenants()}>
                {t("refreshButton")}
              </Btn>
            </>
          }
        />

        {/* Stats row */}
        <div style={{ display: "grid", gridTemplateColumns: "repeat(4, 1fr)", gap: 12 }}>
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

        <Tabs
          items={[
            { key: "clients", label: <><Building2 size={14} /> {t("tabClients", { count: clientTenants.length })}</> },
            { key: "suppliers", label: <><Truck size={14} /> {t("tabSuppliers", { count: supplierTenants.length })}</> },
            { key: "logs", label: <><Activity size={14} /> {t("tabLogs")}</> },
          ]}
          value={activeTab}
          onChange={setActiveTab}
        />

        {/* Clients / Suppliers tabs (share the same tenant grid) */}
        {(activeTab === "clients" || activeTab === "suppliers") && (
          <>
            {/* Search */}
            <div style={{ marginBottom: 20 }}>
              <input
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder={t("searchPlaceholder")}
                style={{ ...inputStyle, maxWidth: 380 }}
              />
            </div>

            {/* Grid */}
            {tenantsLoading ? (
              <div style={{ color: "#4B5563", fontSize: 14 }}>{t("loadingClients")}</div>
            ) : filtered.length === 0 ? (
              <div style={{ color: "#4B5563", fontSize: 14 }}>
                {search
                  ? t("noResults")
                  : activeTab === "suppliers"
                    ? t("noSuppliers")
                    : t("noClients")}
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

      </PageContainer>{/* end main content */}

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
