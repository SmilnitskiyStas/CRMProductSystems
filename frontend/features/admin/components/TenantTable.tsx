"use client";

import { useTranslations } from "next-intl";
import type { TenantDto } from "../types";
import { PLAN_COLORS } from "../types";
import { useActivateTenant, useDeactivateTenant } from "../hooks/useAdmin";
import { Btn } from "@/components/ui/Btn";

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

const TH_STYLE: React.CSSProperties = {
  padding: "10px 14px",
  textAlign: "left",
  color: "#4B5563",
  fontSize: 11,
  fontWeight: 600,
  borderBottom: "1px solid #1F2937",
  whiteSpace: "nowrap",
};

const TD_STYLE: React.CSSProperties = {
  padding: "12px 14px",
  color: "#E8EDF5",
  fontSize: 13,
  borderBottom: "1px solid #1F2937",
  verticalAlign: "middle",
};

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

  return (
    <div
      style={{
        background: "#0D1117",
        border: "1px solid #1F2937",
        borderRadius: 10,
        overflow: "hidden",
      }}
    >
      <table style={{ width: "100%", borderCollapse: "collapse" }}>
        <thead>
          <tr style={{ background: "#080D14" }}>
            <th style={TH_STYLE}>{t("headerName")}</th>
            <th style={TH_STYLE}>{t("headerPlan")}</th>
            <th style={TH_STYLE}>{t("headerModules")}</th>
            <th style={TH_STYLE}>{t("headerUsage")}</th>
            <th style={TH_STYLE}>{t("headerSales")}</th>
            <th style={TH_STYLE}>{t("headerStatus")}</th>
            <th style={TH_STYLE}>{t("headerActions")}</th>
          </tr>
        </thead>
        <tbody>
          {tenants.map((tenant) => (
            <tr
              key={tenant.id}
              onClick={() => onRowClick(tenant)}
              style={{ cursor: "pointer" }}
              onMouseEnter={(e) => {
                (e.currentTarget as HTMLElement).style.background = "#0F1520";
              }}
              onMouseLeave={(e) => {
                (e.currentTarget as HTMLElement).style.background = "transparent";
              }}
            >
              <td style={TD_STYLE}>
                <div style={{ fontWeight: 600 }}>{tenant.name}</div>
                <div style={{ color: "#4B5563", fontSize: 11 }}>{tenant.slug}</div>
              </td>
              <td style={TD_STYLE}>
                <PlanBadge plan={tenant.plan} />
              </td>
              <td style={{ ...TD_STYLE, color: "#9CA3AF" }}>
                {tenant.modules.length}
              </td>
              <td style={{ ...TD_STYLE, color: "#9CA3AF" }}>
                {tenant.usage.usersCount} / {tenant.usage.storesCount} / {tenant.usage.productsCount}
              </td>
              <td style={{ ...TD_STYLE, color: "#E8EDF5", fontWeight: 600 }}>
                {formatNumber(tenant.usage.salesLast30Days)}
              </td>
              <td style={TD_STYLE}>
                <StatusBadge isActive={tenant.isActive} />
              </td>
              <td style={TD_STYLE}>
                <div style={{ display: "flex", gap: 6 }} onClick={(e) => e.stopPropagation()}>
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
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
