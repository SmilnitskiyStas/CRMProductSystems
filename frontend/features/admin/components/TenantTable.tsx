"use client";

import type { TenantDto } from "../types";
import { PLAN_LABELS, PLAN_COLORS } from "../types";
import { useActivateTenant, useDeactivateTenant } from "../hooks/useAdmin";

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
      {PLAN_LABELS[plan]}
    </span>
  );
}

function StatusBadge({ isActive }: { isActive: boolean }) {
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
      {isActive ? "Активний" : "Деактивовано"}
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
  const activate   = useActivateTenant();
  const deactivate = useDeactivateTenant();

  if (isLoading) {
    return <div style={{ color: "#4B5563", fontSize: 14, padding: "24px 0" }}>Завантаження…</div>;
  }

  if (tenants.length === 0) {
    return <div style={{ color: "#4B5563", fontSize: 14, padding: "24px 0" }}>Тенанти відсутні</div>;
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
            <th style={TH_STYLE}>Назва / Slug</th>
            <th style={TH_STYLE}>План</th>
            <th style={TH_STYLE}>Модулів</th>
            <th style={TH_STYLE}>Кор. / Маг. / Прод.</th>
            <th style={TH_STYLE}>Продажі 30д</th>
            <th style={TH_STYLE}>Статус</th>
            <th style={TH_STYLE}>Дії</th>
          </tr>
        </thead>
        <tbody>
          {tenants.map((t) => (
            <tr
              key={t.id}
              onClick={() => onRowClick(t)}
              style={{ cursor: "pointer" }}
              onMouseEnter={(e) => {
                (e.currentTarget as HTMLElement).style.background = "#0F1520";
              }}
              onMouseLeave={(e) => {
                (e.currentTarget as HTMLElement).style.background = "transparent";
              }}
            >
              <td style={TD_STYLE}>
                <div style={{ fontWeight: 600 }}>{t.name}</div>
                <div style={{ color: "#4B5563", fontSize: 11 }}>{t.slug}</div>
              </td>
              <td style={TD_STYLE}>
                <PlanBadge plan={t.plan} />
              </td>
              <td style={{ ...TD_STYLE, color: "#9CA3AF" }}>
                {t.modules.length}
              </td>
              <td style={{ ...TD_STYLE, color: "#9CA3AF" }}>
                {t.usage.usersCount} / {t.usage.storesCount} / {t.usage.productsCount}
              </td>
              <td style={{ ...TD_STYLE, color: "#E8EDF5", fontWeight: 600 }}>
                {formatNumber(t.usage.salesLast30Days)}
              </td>
              <td style={TD_STYLE}>
                <StatusBadge isActive={t.isActive} />
              </td>
              <td style={TD_STYLE}>
                <div style={{ display: "flex", gap: 6 }}>
                  <button
                    onClick={(e) => { e.stopPropagation(); onRowClick(t); }}
                    style={{
                      padding: "5px 12px",
                      borderRadius: 6,
                      fontSize: 12,
                      background: "#1D3461",
                      border: "1px solid #3B82F6",
                      color: "#93C5FD",
                      cursor: "pointer",
                    }}
                  >
                    Деталі
                  </button>
                  <button
                    onClick={(e) => handleToggle(e, t)}
                    disabled={activate.isPending || deactivate.isPending}
                    style={{
                      padding: "5px 12px",
                      borderRadius: 6,
                      fontSize: 12,
                      background: t.isActive ? "#1F1211" : "#052e16",
                      border: `1px solid ${t.isActive ? "#7F1D1D" : "#166534"}`,
                      color: t.isActive ? "#F87171" : "#4ADE80",
                      cursor: "pointer",
                    }}
                  >
                    {t.isActive ? "Деакт." : "Акт."}
                  </button>
                </div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
