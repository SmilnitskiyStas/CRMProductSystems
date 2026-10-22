"use client";

import { useTranslations } from "next-intl";
import { StatusPill } from "@/components/ui/StatusPill";
import { PLAN_COLORS } from "../types";
import type { TenantSummaryDto } from "../types";

interface Props {
  tenant: TenantSummaryDto;
  onClick: () => void;
}

export function TenantCard({ tenant, onClick }: Props) {
  const t = useTranslations("Dashboard.provider.tenantCard");
  const tPlans = useTranslations("Dashboard.provider.plans");
  const tModules = useTranslations("Dashboard.provider.modules");
  const plan = PLAN_COLORS[tenant.plan] ?? PLAN_COLORS.basic;

  return (
    <div
      onClick={onClick}
      style={{
        background: "#0D1117",
        border: "1px solid #1F2937",
        borderRadius: 12,
        padding: "18px 20px",
        cursor: "pointer",
        transition: "border-color 0.15s",
        position: "relative",
      }}
      onMouseEnter={(e) => { (e.currentTarget as HTMLElement).style.borderColor = "#374151"; }}
      onMouseLeave={(e) => { (e.currentTarget as HTMLElement).style.borderColor = "#1F2937"; }}
    >
      {/* Status dot */}
      <div
        style={{
          position: "absolute",
          top: 16,
          right: 16,
          width: 8,
          height: 8,
          borderRadius: "50%",
          background: tenant.isActive ? "#4ADE80" : "#EF4444",
        }}
        title={tenant.isActive ? t("statusActive") : t("statusDeactivated")}
      />

      {/* Name + slug */}
      <div style={{ marginBottom: 12, paddingRight: 20 }}>
        <div style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 600, lineHeight: 1.3 }}>
          {tenant.name}
        </div>
        <div style={{ color: "#4B5563", fontSize: 12, marginTop: 2 }}>
          {tenant.slug}
        </div>
      </div>

      {/* Plan badge */}
      <div style={{ marginBottom: 14 }}>
        <StatusPill bg={plan.bg} text={plan.text}>
          {tPlans(tenant.plan)}
        </StatusPill>
      </div>

      {/* Stats row */}
      <div style={{ display: "flex", gap: 16, marginBottom: 14 }}>
        {[
          { label: t("statUsers"), value: tenant.userCount },
          { label: t("statStores"), value: tenant.storeCount },
          { label: t("statExpired"), value: tenant.expiredBatchCount, warn: tenant.expiredBatchCount > 0 },
        ].map((stat) => (
          <div key={stat.label}>
            <div style={{ color: stat.warn ? "#F87171" : "#E8EDF5", fontSize: 16, fontWeight: 700 }}>
              {stat.value}
            </div>
            <div style={{ color: "#4B5563", fontSize: 11 }}>{stat.label}</div>
          </div>
        ))}
      </div>

      {/* Modules */}
      {tenant.modules.length > 0 && (
        <div style={{ display: "flex", flexWrap: "wrap", gap: 4 }}>
          {tenant.modules.slice(0, 4).map((m) => (
            <StatusPill key={m} bg="#111827" text="#6B7280" style={{ fontSize: 10, padding: "2px 7px", fontWeight: 400 }}>
              {tModules.has(m) ? tModules(m) : m}
            </StatusPill>
          ))}
          {tenant.modules.length > 4 && (
            <span style={{ fontSize: 10, color: "#4B5563", padding: "2px 4px" }}>
              +{tenant.modules.length - 4}
            </span>
          )}
        </div>
      )}
    </div>
  );
}
