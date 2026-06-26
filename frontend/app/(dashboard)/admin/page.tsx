"use client";

import { useState, useEffect } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { Shield, Building2, CheckCircle, LayoutGrid } from "lucide-react";
import { useMe } from "@/features/auth/hooks/useAuth";
import { useTenants } from "@/features/admin/hooks/useAdmin";
import { TenantTable } from "@/features/admin/components/TenantTable";
import { TenantDetailDrawer } from "@/features/admin/components/TenantDetailDrawer";
import type { TenantDto, TenantPlan } from "@/features/admin/types";
import { PLAN_LABELS, PLAN_COLORS } from "@/features/admin/types";

export default function AdminPage() {
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
        <div style={{ color: "#4B5563", fontSize: 14 }}>Завантаження…</div>
      </div>
    );
  }

  if (me?.role !== "provider") {
    return (
      <div style={{ display: "flex", alignItems: "center", justifyContent: "center", height: "60vh" }}>
        <div style={{ color: "#F87171", fontSize: 14 }}>403 — Доступ заборонено</div>
      </div>
    );
  }

  const allTenants = tenants ?? [];
  const activeTenants = allTenants.filter((t) => t.isActive);

  const planCounts = allTenants.reduce<Record<string, number>>((acc, t) => {
    acc[t.plan] = (acc[t.plan] ?? 0) + 1;
    return acc;
  }, {});

  const filtered = allTenants.filter(
    (t) =>
      t.name.toLowerCase().includes(search.toLowerCase()) ||
      t.slug.toLowerCase().includes(search.toLowerCase()),
  );

  const stats = [
    {
      label: "Всього тенантів",
      value: allTenants.length,
      icon: <Building2 size={18} />,
      color: "#60A5FA",
    },
    {
      label: "Активних",
      value: activeTenants.length,
      icon: <CheckCircle size={18} />,
      color: "#4ADE80",
    },
    {
      label: "Планів",
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
      <div style={{ flex: 1, minWidth: 0, padding: "28px 32px" }}>
        {/* Header */}
        <div
          style={{
            display: "flex",
            alignItems: "flex-start",
            justifyContent: "space-between",
            marginBottom: 28,
          }}
        >
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
                Адмін панель
              </h1>
            </div>
            <p style={{ color: "#4B5563", fontSize: 14, margin: 0 }}>
              Управління тенантами платформи ShelfGuard
            </p>
          </div>

          <Link
            href="/provider"
            style={{
              color: "#60A5FA",
              fontSize: 13,
              textDecoration: "none",
            }}
          >
            → Провайдер
          </Link>
        </div>

        {/* Stats row */}
        <div style={{ display: "flex", gap: 12, marginBottom: 24 }}>
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
                <div style={{ color: c.text, fontSize: 12, marginTop: 4, opacity: 0.8 }}>{PLAN_LABELS[plan]}</div>
              </div>
            );
          })}
        </div>

        {/* Search */}
        <div style={{ marginBottom: 16 }}>
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Пошук за назвою або slug…"
            style={{
              width: "100%",
              maxWidth: 360,
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

        {/* Table */}
        <TenantTable
          tenants={filtered}
          isLoading={tenantsLoading}
          onRowClick={handleRowClick}
        />
      </div>

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
