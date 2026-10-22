"use client";

import type { CSSProperties, ReactNode } from "react";
import { useLocale } from "next-intl";
import { filterInputStyle } from "@/components/ui/fieldStyles";
import { StatusPill } from "@/components/ui/StatusPill";
import { PageHeader as SharedPageHeader } from "@/components/ui/PageHeader";
import { useProductionLocationList } from "../hooks/useProductionInsights";

export const cardStyle: CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 12,
  padding: 16,
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

/** Thin adapter over the shared ui/PageHeader (keeps the `right` prop name). */
export function PageHeader({ title, subtitle, right }: { title: string; subtitle?: string; right?: ReactNode }) {
  return <SharedPageHeader title={title} subtitle={subtitle} actions={right} />;
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
    <select value={value} onChange={(e) => onChange(e.target.value)} style={filterInputStyle}>
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

/** Neutral-by-default status pill (delegates to the shared ui/StatusPill). */
export function Pill({ children, color = "#9CA3AF", bg = "#1F2937" }: { children: ReactNode; color?: string; bg?: string }) {
  return <StatusPill bg={bg} text={color}>{children}</StatusPill>;
}
