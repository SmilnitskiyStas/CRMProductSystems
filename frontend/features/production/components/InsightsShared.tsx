"use client";

import type { CSSProperties, ReactNode } from "react";
import { useLocale } from "next-intl";
import { useProductionLocationList } from "../hooks/useProductionInsights";

export const cardStyle: CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 12,
  padding: 16,
};

export const selectStyle: CSSProperties = {
  background: "#111827",
  border: "1px solid #1F2937",
  borderRadius: 8,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "8px 12px",
  outline: "none",
};

export const mutedText: CSSProperties = { color: "#6B7280", fontSize: 12 };

export function useIntlLocale() {
  return useLocale() === "en" ? "en-US" : "uk-UA";
}

/** Accepts yyyy-MM-dd or full ISO datetime. */
export function fmtDate(d: string | null | undefined, intl: string): string {
  if (!d) return "—";
  const dt = d.length <= 10 ? new Date(`${d}T00:00:00`) : new Date(d);
  return isNaN(dt.getTime())
    ? "—"
    : dt.toLocaleDateString(intl, { day: "2-digit", month: "2-digit", year: "numeric" });
}

export function fmtQty(n: number | null | undefined, intl: string): string {
  if (n == null) return "—";
  return n.toLocaleString(intl, { maximumFractionDigits: 3 });
}

export function daysUntil(d: string | null | undefined): number | null {
  if (!d) return null;
  const dt = d.length <= 10 ? new Date(`${d}T00:00:00`) : new Date(d);
  if (isNaN(dt.getTime())) return null;
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  return Math.round((dt.getTime() - today.getTime()) / 86_400_000);
}

/** red <=3d, amber <=7d, normal otherwise. */
export function expiryColor(days: number | null | undefined): string {
  if (days == null) return "#9CA3AF";
  if (days <= 3) return "#F87171";
  if (days <= 7) return "#FBBF24";
  return "#E8EDF5";
}

export function PageHeader({ title, subtitle, right }: { title: string; subtitle?: string; right?: ReactNode }) {
  return (
    <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", gap: 16, flexWrap: "wrap", marginBottom: 20 }}>
      <div>
        <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>{title}</h1>
        {subtitle && <p style={{ color: "#6B7280", fontSize: 13, margin: "4px 0 0" }}>{subtitle}</p>}
      </div>
      <div style={{ display: "flex", gap: 10, alignItems: "center", flexWrap: "wrap" }}>{right}</div>
    </div>
  );
}

/** Location filter — renders nothing unless the tenant has more than one production location. */
export function ProductionLocationSelect({
  value,
  onChange,
  allLabel,
}: {
  value: string;
  onChange: (v: string) => void;
  allLabel: string;
}) {
  const { list } = useProductionLocationList();
  if (list.length <= 1) return null;
  return (
    <select value={value} onChange={(e) => onChange(e.target.value)} style={selectStyle}>
      <option value="">{allLabel}</option>
      {list.map((l) => (
        <option key={l.id} value={l.id}>
          {l.name}
        </option>
      ))}
    </select>
  );
}

export function StateBox({ kind, children }: { kind: "loading" | "error" | "empty"; children: ReactNode }) {
  const color = kind === "error" ? "#F87171" : "#6B7280";
  return <div style={{ ...cardStyle, color, fontSize: 13, textAlign: "center", padding: 32 }}>{children}</div>;
}

export function Pill({ children, color = "#9CA3AF", bg = "#1F2937" }: { children: ReactNode; color?: string; bg?: string }) {
  return (
    <span style={{ display: "inline-block", padding: "2px 8px", borderRadius: 999, fontSize: 11, fontWeight: 600, color, background: bg }}>
      {children}
    </span>
  );
}
