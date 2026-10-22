"use client";

import { useState, useEffect } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { Shield, Building2, CheckCircle, LayoutGrid } from "lucide-react";
import { PageContainer } from "@/components/ui/PageContainer";
import { PageHeader } from "@/components/ui/PageHeader";
import { inputStyle } from "@/components/ui/fieldStyles";
import { useMe } from "@/features/auth/hooks/useAuth";
import { useTenants } from "@/features/admin/hooks/useAdmin";
import { TenantTable } from "@/features/admin/components/TenantTable";
import { TenantDetailDrawer } from "@/features/admin/components/TenantDetailDrawer";
import type { TenantDto, TenantPlan } from "@/features/admin/types";
import { PLAN_COLORS } from "@/features/admin/types";

export default function AdminPage() {
  const t = useTranslations("Dashboard.admin.page");
  const tPlans = useTranslations("Dashboard.admin.plans");
  const router = useRouter();
  const { data: me, isLoading: meLoading } = useMe();
  const { data: tenants, isLoading: tenantsLoading } = useTenants();

  const [selectedId, setSelectedId]   = useState<string | null>(null);
  const [search,     setSearch]       = useState("");

  // Role guard
  useEffect(() => {
    if (!meLoading && me && me.role !== "provider") {
      router.replace("/");
    }
  }, [me, meLoading, router]);

  if (meLoading) {
    return (
      <div style={{ display: "flex", alignItems: "center", justifyContent: "center", height: "60vh" }}>
        <div style={{ color: "#4B5563", fontSize: 14 }}>{t("loading")}</div>
      </div>
    );
  }

  if (me?.role !== "provider") {
    return (
      <div style={{ display: "flex", alignItems: "center", justifyContent: "center", height: "60vh" }}>
        <div style={{ color: "#F87171", fontSize: 14 }}>{t("forbidden")}</div>
      </div>
    );
  }

  const allTenants = tenants ?? [];
  const activeTenants = allTenants.filter((tn) => tn.isActive);

  const planCounts = allTenants.reduce<Record<string, number>>((acc, tn) => {
    acc[tn.plan] = (acc[tn.plan] ?? 0) + 1;
    return acc;
  }, {});

  const filtered = allTenants.filter(
    (tn) =>
      tn.name.toLowerCase().includes(search.toLowerCase()) ||
      tn.slug.toLowerCase().includes(search.toLowerCase()),
  );

  const stats = [
    {
      label: t("statTotalTenants"),
      value: allTenants.length,
      icon: <Building2 size={18} />,
      color: "#60A5FA",
    },
    {
      label: t("statActive"),
      value: activeTenants.length,
      icon: <CheckCircle size={18} />,
      color: "#4ADE80",
    },
    {
      label: t("statPlans"),
      value: Object.keys(planCounts).length,
      icon: <LayoutGrid size={18} />,
      color: "#A78BFA",
    },
  ];

  function handleRowClick(tenant: TenantDto) {
    setSelectedId(tenant.id === selectedId ? null : tenant.id);
  }

  return (
    <div style={{ display: "flex", alignItems: "flex-start", minHeight: "100vh" }}>
      {/* Main content */}
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
            <Link href="/provider" style={{ color: "#60A5FA", fontSize: 13, textDecoration: "none" }}>
              {t("providerLink")}
            </Link>
          }
        />

        {/* Stats row */}
        <div style={{ display: "flex", gap: 12 }}>
          {stats.map((s) => (
            <div
              key={s.label}
              style={{
                background: "#0D1117",
                border: "1px solid #1F2937",
                borderRadius: 10,
                padding: "14px 20px",
                display: "flex",
                alignItems: "center",
                gap: 14,
                minWidth: 160,
              }}
            >
              <div style={{ color: s.color, opacity: 0.8 }}>{s.icon}</div>
              <div>
                <div style={{ color: s.color, fontSize: 24, fontWeight: 700, lineHeight: 1 }}>{s.value}</div>
                <div style={{ color: "#4B5563", fontSize: 12, marginTop: 4 }}>{s.label}</div>
              </div>
            </div>
          ))}

          {/* Plan breakdown */}
          {(Object.entries(planCounts) as [TenantPlan, number][]).map(([plan, count]) => {
            const c = PLAN_COLORS[plan];
            return (
              <div
                key={plan}
                style={{
                  background: c.bg,
                  border: `1px solid ${c.border}`,
                  borderRadius: 10,
                  padding: "14px 20px",
                  minWidth: 120,
                }}
              >
                <div style={{ color: c.text, fontSize: 22, fontWeight: 700, lineHeight: 1 }}>{count}</div>
                <div style={{ color: c.text, fontSize: 12, marginTop: 4, opacity: 0.8 }}>{tPlans(plan)}</div>
              </div>
            );
          })}
        </div>

        {/* Search */}
        <div>
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder={t("searchPlaceholder")}
            style={{ ...inputStyle, maxWidth: 360 }}
          />
        </div>

        {/* Table */}
        <TenantTable
          tenants={filtered}
          isLoading={tenantsLoading}
          onRowClick={handleRowClick}
        />
      </PageContainer>

      {/* Drawer */}
      {selectedId && (
        <TenantDetailDrawer
          tenantId={selectedId}
          onClose={() => setSelectedId(null)}
        />
      )}

    </div>
  );
}
