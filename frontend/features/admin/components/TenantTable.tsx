"use client";

import { useTranslations } from "next-intl";
import type { TenantDto } from "../types";
import { PLAN_COLORS } from "../types";
import { useActivateTenant, useDeactivateTenant } from "../hooks/useAdmin";
import { Btn } from "@/components/ui/Btn";
import { Table, type TableColumn } from "@/components/ui/Table";

interface Props {
  tenants: TenantDto[];
  isLoading: boolean;
  onRowClick: (tenant: TenantDto) => void;
}

function formatNumber(n: number): string {
  if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(1)}M`;
  if (n >= 1_000) return `${(n / 1_000).toFixed(1)}K`;
  return String(n);
}

function PlanBadge({ plan }: { plan: TenantDto["plan"] }) {
  const tPlans = useTranslations("Dashboard.admin.plans");
  const c = PLAN_COLORS[plan];
  return (
    <span
      style={{
        display: "inline-block",
        padding: "3px 10px",
        borderRadius: 6,
        fontSize: 11,
        fontWeight: 600,
        background: c.bg,
        border: `1px solid ${c.border}`,
        color: c.text,
      }}
    >
      {tPlans(plan)}
    </span>
  );
}

function StatusBadge({ isActive }: { isActive: boolean }) {
  const t = useTranslations("Dashboard.admin.tenantTable");
  return (
    <span
      style={{
        display: "inline-block",
        padding: "3px 10px",
        borderRadius: 6,
        fontSize: 11,
        fontWeight: 600,
        background: isActive ? "#052e16" : "#1F1211",
        border: `1px solid ${isActive ? "#166534" : "#7F1D1D"}`,
        color: isActive ? "#4ADE80" : "#F87171",
      }}
    >
      {isActive ? t("statusActive") : t("statusDeactivated")}
    </span>
  );
}

export function TenantTable({ tenants, isLoading, onRowClick }: Props) {
  const t = useTranslations("Dashboard.admin.tenantTable");
  const activate   = useActivateTenant();
  const deactivate = useDeactivateTenant();

  if (isLoading) {
    return <div style={{ color: "#4B5563", fontSize: 14, padding: "24px 0" }}>{t("loading")}</div>;
  }

  if (tenants.length === 0) {
    return <div style={{ color: "#4B5563", fontSize: 14, padding: "24px 0" }}>{t("empty")}</div>;
  }

  function handleToggle(e: React.MouseEvent, tenant: TenantDto) {
    e.stopPropagation();
    if (tenant.isActive) {
      deactivate.mutate(tenant.id);
    } else {
      activate.mutate(tenant.id);
    }
  }

  const columns: TableColumn<TenantDto>[] = [
    {
      key: "name",
      header: t("headerName"),
      render: (tenant) => (
        <>
          <div style={{ fontWeight: 600, color: "#E8EDF5" }}>{tenant.name}</div>
          <div style={{ color: "#4B5563", fontSize: 11 }}>{tenant.slug}</div>
        </>
      ),
    },
    {
      key: "plan",
      header: t("headerPlan"),
      render: (tenant) => <PlanBadge plan={tenant.plan} />,
    },
    {
      key: "modules",
      header: t("headerModules"),
      cellStyle: { color: "#9CA3AF" },
      render: (tenant) => tenant.modules.length,
    },
    {
      key: "usage",
      header: t("headerUsage"),
      cellStyle: { color: "#9CA3AF" },
      render: (tenant) => `${tenant.usage.usersCount} / ${tenant.usage.storesCount} / ${tenant.usage.productsCount}`,
    },
    {
      key: "sales",
      header: t("headerSales"),
      cellStyle: { color: "#E8EDF5", fontWeight: 600 },
      render: (tenant) => formatNumber(tenant.usage.salesLast30Days),
    },
    {
      key: "status",
      header: t("headerStatus"),
      render: (tenant) => <StatusBadge isActive={tenant.isActive} />,
    },
    {
      key: "actions",
      header: t("headerActions"),
      render: (tenant) => (
        <div style={{ display: "flex", gap: 6, justifyContent: "center" }} onClick={(e) => e.stopPropagation()}>
          <Btn size="sm" onClick={() => onRowClick(tenant)}>
            {t("detailsButton")}
          </Btn>
          <Btn
            size="sm"
            variant={tenant.isActive ? "danger" : "success"}
            onClick={() => { if (tenant.isActive) { deactivate.mutate(tenant.id); } else { activate.mutate(tenant.id); } }}
            disabled={activate.isPending || deactivate.isPending}
          >
            {tenant.isActive ? t("deactivateShort") : t("activateShort")}
          </Btn>
        </div>
      ),
    },
  ];

  return (
    <Table
      columns={columns}
      rows={tenants}
      rowKey={(tenant) => tenant.id}
      onRowClick={(tenant) => onRowClick(tenant)}
    />
  );
}
