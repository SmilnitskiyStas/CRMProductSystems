"use client";

import { useTranslations } from "next-intl";
import { RefreshCw } from "lucide-react";
import type { MarketingAnalyticsPeriodPreset } from "../types";

interface Props {
  period: MarketingAnalyticsPeriodPreset;
  onPeriodChange: (p: MarketingAnalyticsPeriodPreset) => void;
  customFrom: string;
  customTo: string;
  onCustomRangeChange: (from: string, to: string) => void;
  onRefresh: () => void;
  isRefreshing?: boolean;
}

const PRESETS: MarketingAnalyticsPeriodPreset[] = ["3m", "6m", "12m", "all", "custom"];

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

function presetBtnStyle(active: boolean): React.CSSProperties {
  return {
    padding: "7px 14px",
    fontSize: 12,
    fontWeight: 600,
    borderRadius: 6,
    border: "1px solid #1F2937",
    cursor: "pointer",
    background: active ? "#1D3461" : "#0D1117",
    color: active ? "#93C5FD" : "#6B7280",
    transition: "background 0.1s, color 0.1s",
    whiteSpace: "nowrap",
  };
}

/**
 * Presets 3/6/12 months + all-time + custom range (RFM_ANALYSIS.md §4). No longer renders its own
 * store picker — the header's global StoreSelector (`useStoreContext`) now supports picking
 * one/several/all stores, so the page reads `storeIds` from there directly for its query filters
 * instead of this component owning a duplicate multi-select (TASK-515).
 */
export function PeriodStoreFilterBar({
  period,
  onPeriodChange,
  customFrom,
  customTo,
  onCustomRangeChange,
  onRefresh,
  isRefreshing,
}: Props) {
  const t = useTranslations("Dashboard.marketingAnalytics.filterBar");

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
      {/* Period presets */}
      <div>
        <label style={labelStyle}>{t("periodLabel")}</label>
        <div style={{ display: "flex", gap: 4, flexWrap: "wrap" }}>
          {PRESETS.map((p) => (
            <button key={p} style={presetBtnStyle(period === p)} onClick={() => onPeriodChange(p)}>
              {t(`preset.${p}`)}
            </button>
          ))}
        </div>
      </div>

      {/* Custom range */}
      {period === "custom" && (
        <>
          <div>
            <label style={labelStyle}>{t("fromLabel")}</label>
            <input
              type="date"
              value={customFrom}
              max={customTo || undefined}
              onChange={(e) => e.target.value && onCustomRangeChange(e.target.value, customTo)}
              style={inputStyle}
            />
          </div>
          <div>
            <label style={labelStyle}>{t("toLabel")}</label>
            <input
              type="date"
              value={customTo}
              min={customFrom || undefined}
              onChange={(e) => e.target.value && onCustomRangeChange(customFrom, e.target.value)}
              style={inputStyle}
            />
          </div>
        </>
      )}

      {/* Refresh */}
      <button
        onClick={onRefresh}
        disabled={isRefreshing}
        style={{
          display: "flex",
          alignItems: "center",
          gap: 6,
          padding: "7px 14px",
          borderRadius: 6,
          border: "1px solid #1F2937",
          background: "#0D1117",
          color: "#9CA3AF",
          fontSize: 12,
          fontWeight: 600,
          cursor: isRefreshing ? "not-allowed" : "pointer",
          opacity: isRefreshing ? 0.6 : 1,
        }}
      >
        <RefreshCw size={13} className={isRefreshing ? "animate-spin" : undefined} />
        {t("refreshButton")}
      </button>
    </div>
  );
}
