"use client";

import { useEffect, useRef, useState } from "react";
import { useTranslations } from "next-intl";
import { ChevronDown, Store, Check, RefreshCw } from "lucide-react";
import { useStores } from "@/features/stores/hooks/useStores";
import type { PriceSegmentsPeriodPreset } from "../types";

interface Props {
  period: PriceSegmentsPeriodPreset;
  onPeriodChange: (p: PriceSegmentsPeriodPreset) => void;
  customFrom: string;
  customTo: string;
  onCustomRangeChange: (from: string, to: string) => void;
  /** Empty = all stores. */
  storeIds: string[];
  onStoreIdsChange: (ids: string[]) => void;
  onRefresh: () => void;
  isRefreshing?: boolean;
  /** All-time mode has no period concept at all — this bar is reused there too (design doc §6
   * lists only one ComparisonFilterBar.tsx), just with the period row hidden so only the store
   * multi-select + refresh remain. */
  hidePeriod?: boolean;
}

const PRESETS: PriceSegmentsPeriodPreset[] = ["30", "60", "90", "custom"];

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
 * 30/60/90-day presets + custom range + multi-store selector — deliberately NO "all time" preset
 * here (design doc: "Весь час" is its own MODE TAB, not a period option — see ModeTabs.tsx).
 * Shared by BOTH the "Суми покупок" and "Частота та реактивація" tabs (analysis doc §4.1: the
 * competitor itself uses one shared top period+store control for both of its tabs); the "Весь
 * час" tab reuses this same component with `hidePeriod` so only the store filter shows.
 */
export function ComparisonFilterBar({
  period,
  onPeriodChange,
  customFrom,
  customTo,
  onCustomRangeChange,
  storeIds,
  onStoreIdsChange,
  onRefresh,
  isRefreshing,
  hidePeriod,
}: Props) {
  const t = useTranslations("Dashboard.priceSegments.filterBar");
  const { data: stores = [] } = useStores();

  const [storesOpen, setStoresOpen] = useState(false);
  const [draftStoreIds, setDraftStoreIds] = useState<string[]>(storeIds);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (storesOpen) setDraftStoreIds(storeIds);
  }, [storesOpen, storeIds]);

  useEffect(() => {
    function handleClick(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) setStoresOpen(false);
    }
    document.addEventListener("mousedown", handleClick);
    return () => document.removeEventListener("mousedown", handleClick);
  }, []);

  function toggleDraftStore(id: string) {
    setDraftStoreIds((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
  }

  function applyStores() {
    onStoreIdsChange([...draftStoreIds].sort());
    setStoresOpen(false);
  }

  function selectAllStores() {
    setDraftStoreIds([]);
  }

  const storesLabel =
    storeIds.length === 0
      ? t("allStores")
      : storeIds.length === 1
      ? stores.find((s) => s.id === storeIds[0])?.name ?? t("storesCount", { count: 1 })
      : t("storesCount", { count: storeIds.length });

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
      {!hidePeriod && (
        <>
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
        </>
      )}

      {/* Store multi-select */}
      <div ref={ref} style={{ position: "relative" }}>
        <label style={labelStyle}>{t("storesLabel")}</label>
        <button
          onClick={() => setStoresOpen((v) => !v)}
          style={{
            display: "flex",
            alignItems: "center",
            gap: 8,
            background: storesOpen ? "#161B26" : "#0D1117",
            border: `1px solid ${storesOpen ? "#374151" : "#1F2937"}`,
            borderRadius: 6,
            padding: "7px 10px",
            cursor: "pointer",
            color: "#E8EDF5",
          }}
        >
          <Store size={13} color="#6B7280" />
          <span style={{ fontSize: 13, maxWidth: 180, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
            {storesLabel}
          </span>
          <ChevronDown
            size={13}
            color="#6B7280"
            style={{ transform: storesOpen ? "rotate(180deg)" : "none", transition: "transform 0.15s" }}
          />
        </button>

        {storesOpen && (
          <div
            style={{
              position: "absolute",
              top: "calc(100% + 6px)",
              left: 0,
              minWidth: 260,
              background: "#0D1117",
              border: "1px solid #1F2937",
              borderRadius: 10,
              boxShadow: "0 8px 24px rgba(0,0,0,0.5)",
              zIndex: 200,
              overflow: "hidden",
            }}
          >
            <div style={{ padding: "6px 12px 4px", borderBottom: "1px solid #1F2937" }}>
              <span style={{ color: "#4B5563", fontSize: 11, fontWeight: 600, textTransform: "uppercase", letterSpacing: "0.06em" }}>
                {t("storesPopoverTitle")}
              </span>
            </div>
            <div style={{ maxHeight: 260, overflowY: "auto" }}>
              <label
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: 10,
                  padding: "9px 12px",
                  borderBottom: "1px solid #111827",
                  cursor: "pointer",
                }}
              >
                <input
                  type="checkbox"
                  checked={draftStoreIds.length === 0}
                  onChange={selectAllStores}
                  style={{ accentColor: "#3B82F6", width: 14, height: 14, cursor: "pointer" }}
                />
                <span
                  style={{
                    color: draftStoreIds.length === 0 ? "#E8EDF5" : "#9CA3AF",
                    fontSize: 13,
                    fontWeight: draftStoreIds.length === 0 ? 600 : 400,
                  }}
                >
                  {t("selectAllStores")}
                </span>
              </label>
              {stores.map((s) => {
                const checked = draftStoreIds.includes(s.id);
                return (
                  <label
                    key={s.id}
                    style={{
                      display: "flex",
                      alignItems: "center",
                      gap: 10,
                      padding: "9px 12px",
                      borderBottom: "1px solid #111827",
                      cursor: "pointer",
                    }}
                  >
                    <input
                      type="checkbox"
                      checked={checked}
                      onChange={() => toggleDraftStore(s.id)}
                      style={{ accentColor: "#3B82F6", width: 14, height: 14, cursor: "pointer" }}
                    />
                    <span
                      style={{
                        color: checked ? "#E8EDF5" : "#9CA3AF",
                        fontSize: 13,
                        fontWeight: checked ? 600 : 400,
                        overflow: "hidden",
                        textOverflow: "ellipsis",
                        whiteSpace: "nowrap",
                      }}
                    >
                      {s.name}
                    </span>
                    {checked && <Check size={13} color="#3B82F6" style={{ marginLeft: "auto", flexShrink: 0 }} />}
                  </label>
                );
              })}
            </div>
            <div style={{ padding: 10, borderTop: "1px solid #1F2937" }}>
              <button
                onClick={applyStores}
                style={{
                  width: "100%",
                  background: "#1D3461",
                  border: "1px solid #3B82F6",
                  borderRadius: 7,
                  color: "#93C5FD",
                  fontSize: 12,
                  fontWeight: 600,
                  padding: "8px 0",
                  cursor: "pointer",
                }}
              >
                {t("doneButton")}
              </button>
            </div>
          </div>
        )}
      </div>

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
