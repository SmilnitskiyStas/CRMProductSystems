"use client";

import { PLAN_COLORS, PLAN_LABELS, MODULE_LABELS } from "../types";
import type { TenantSummaryDto } from "../types";

interface Props {
  tenant: TenantSummaryDto;
  onClick: () => void;
}

export function TenantCard({ tenant, onClick }: Props) {
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
        title={tenant.isActive ? "Активний" : "Деактивовано"}
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
        <span
          style={{
            display: "inline-block",
            padding: "3px 10px",
            borderRadius: 6,
            fontSize: 11,
            fontWeight: 600,
            background: plan.bg,
            border: `1px solid ${plan.border}`,
            color: plan.text,
          }}
        >
          {PLAN_LABELS[tenant.plan]}
        </span>
      </div>

      {/* Stats row */}
      <div style={{ display: "flex", gap: 16, marginBottom: 14 }}>
        {[
          { label: "Користувачі", value: tenant.userCount },
          { label: "Магазини", value: tenant.storeCount },
          { label: "Протерм.", value: tenant.expiredBatchCount, warn: tenant.expiredBatchCount > 0 },
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
            <span
              key={m}
              style={{
                padding: "2px 7px",
                borderRadius: 4,
                fontSize: 10,
                background: "#111827",
                border: "1px solid #1F2937",
                color: "#6B7280",
              }}
            >
              {MODULE_LABELS[m] ?? m}
            </span>
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
