"use client";

import { useEffect, useRef, useState } from "react";
import { useTranslations } from "next-intl";
import { ChevronDown, Store, Check } from "lucide-react";
import { useStores } from "@/features/stores/hooks/useStores";

interface Props {
  from: string;
  to: string;
  onRangeChange: (from: string, to: string) => void;
  /** Empty = all stores. */
  storeIds: string[];
  onStoreIdsChange: (ids: string[]) => void;
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
 * Purchase-period date range + store multi-select (analysis §5/§6). Unlike Фаза 1/2's 30/60/90
 * preset buttons, `AudienceBuildRequest` takes raw From/To with no server-resolved preset concept
 * (task log 429's confirmed contract) — matches the competitor's own plain date-range picker, so
 * this is a straightforward two-date-input control rather than a preset selector.
 *
 * Not a reuse of `price-segments/components/ComparisonFilterBar.tsx` — this feature stays self-
 * contained the same way price-segments/types.ts documents staying self-contained from RFM's
 * types.ts (each phase owns its own filter bar; ComparisonFilterBar also carries a `hidePeriod`
 * prop and 30/60/90 preset shape this feature has no use for).
 */
export function AudiencePeriodBar({ from, to, onRangeChange, storeIds, onStoreIdsChange }: Props) {
  const t = useTranslations("Dashboard.audienceBuilder.periodBar");
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
        ? (stores.find((s) => s.id === storeIds[0])?.name ?? t("storesCount", { count: 1 }))
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

      <div ref={ref} style={{ position: "relative" }}>
        <label style={labelStyle}>{t("storesLabel")}</label>
        <button
          type="button"
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
                type="button"
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
    </div>
  );
}
