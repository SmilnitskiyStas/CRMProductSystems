"use client";

import { useTranslations } from "next-intl";

interface Props {
  from: string;
  to: string;
  onRangeChange: (from: string, to: string) => void;
}

const labelStyle: React.CSSProperties = { color: "#4B5563", fontSize: 12, marginBottom: 4, display: "block" };
const inputStyle: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 6,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "7px 10px",
  outline: "none",
};

/**
 * Purchase-period date range (analysis §5/§6). Unlike Фаза 1/2's 30/60/90 preset buttons,
 * `AudienceBuildRequest` takes raw From/To with no server-resolved preset concept (task log 429's
 * confirmed contract) — matches the competitor's own plain date-range picker, so this is a
 * straightforward two-date-input control rather than a preset selector. No longer renders its own
 * store picker — the header's global StoreSelector (`useStoreContext`) now supports picking
 * one/several/all stores, so the page reads `storeIds` from there directly for its query filters
 * instead of this component owning a duplicate multi-select (TASK-515).
 */
export function AudiencePeriodBar({ from, to, onRangeChange }: Props) {
  const t = useTranslations("Dashboard.audienceBuilder.periodBar");

  return (
    <div
      style={{
        background: "#0D1117",
        border: "1px solid #1F2937",
        borderRadius: 10,
        padding: "16px 20px",
        display: "flex",
        flexWrap: "wrap",
        gap: 16,
        alignItems: "flex-end",
      }}
    >
      <div>
        <label style={labelStyle}>{t("fromLabel")}</label>
        <input
          type="date"
          value={from}
          max={to || undefined}
          onChange={(e) => e.target.value && onRangeChange(e.target.value, to)}
          style={inputStyle}
        />
      </div>
      <div>
        <label style={labelStyle}>{t("toLabel")}</label>
        <input
          type="date"
          value={to}
          min={from || undefined}
          onChange={(e) => e.target.value && onRangeChange(from, e.target.value)}
          style={inputStyle}
        />
      </div>
    </div>
  );
}
