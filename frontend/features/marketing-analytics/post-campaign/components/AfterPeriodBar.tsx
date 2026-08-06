"use client";

import { useEffect, useRef, useState } from "react";
import { useTranslations } from "next-intl";
import { ChevronDown, Store, Check, Loader2 } from "lucide-react";
import { Btn } from "@/components/ui/Btn";
import { useStores } from "@/features/stores/hooks/useStores";

interface BeforeWindow {
  start: string;
  end: string;
}

interface Props {
  afterStart: string;
  afterEnd: string;
  onRangeChange: (start: string, end: string) => void;
  storeIds: string[];
  onStoreIdsChange: (ids: string[]) => void;
  /** Auto-computed before-window, echoed back by the server on the last analyze/summary call —
   * source doc §10 explicitly wants the user to SEE the derived window, not just trust it silently. */
  beforeWindow: BeforeWindow | null;
  canAnalyze: boolean;
  isAnalyzing: boolean;
  /** true once a report already exists for the CURRENT report segment — swaps the button's label
   * from "Аналізувати сегмент" to "Оновити" (source doc §10.4), same action either way
   * (`POST .../analyze`), just relabeled. */
  hasReportForCurrentSegment: boolean;
  onAnalyzeClick: () => void;
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

function toDateStr(d: Date): string {
  return d.toISOString().slice(0, 10);
}
function daysAgo(n: number): string {
  const d = new Date();
  d.setDate(d.getDate() - n);
  return toDateStr(d);
}
function todayStr(): string {
  return toDateStr(new Date());
}
function currentMonthStart(): string {
  const d = new Date();
  return toDateStr(new Date(d.getFullYear(), d.getMonth(), 1));
}
function lastMonthRange(): [string, string] {
  const d = new Date();
  const start = new Date(d.getFullYear(), d.getMonth() - 1, 1);
  const end = new Date(d.getFullYear(), d.getMonth(), 0);
  return [toDateStr(start), toDateStr(end)];
}

/**
 * After-window date range + quick presets + store filter + the Analyze/Refresh trigger (source doc
 * §10). Not a reuse of `audience-builder/components/AudiencePeriodBar.tsx` — same "each phase
 * re-implements its own filter bar" precedent that component's own doc comment documents (it also
 * lacks quick presets and the analyze/before-window pieces this feature needs).
 */
export function AfterPeriodBar({
  afterStart,
  afterEnd,
  onRangeChange,
  storeIds,
  onStoreIdsChange,
  beforeWindow,
  canAnalyze,
  isAnalyzing,
  hasReportForCurrentSegment,
  onAnalyzeClick,
}: Props) {
  const t = useTranslations("Dashboard.postCampaign.afterPeriodBar");
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

  const storesLabel =
    storeIds.length === 0
      ? t("allStores")
      : storeIds.length === 1
        ? (stores.find((s) => s.id === storeIds[0])?.name ?? t("storesCount", { count: 1 }))
        : t("storesCount", { count: storeIds.length });

  const presets: { key: string; range: () => [string, string] }[] = [
    { key: "last7", range: () => [daysAgo(6), todayStr()] },
    { key: "last14", range: () => [daysAgo(13), todayStr()] },
    { key: "last30", range: () => [daysAgo(29), todayStr()] },
    { key: "thisMonth", range: () => [currentMonthStart(), todayStr()] },
    { key: "lastMonth", range: () => lastMonthRange() },
  ];

  return (
    <div style={{ background: "#0A0F1A", border: "1px solid #1F2937", borderRadius: 12, padding: 18, display: "flex", flexDirection: "column", gap: 14 }}>
      <div style={{ display: "flex", flexWrap: "wrap", gap: 16, alignItems: "flex-end" }}>
        <div>
          <label style={labelStyle}>{t("fromLabel")}</label>
          <input type="date" value={afterStart} max={afterEnd || undefined} onChange={(e) => e.target.value && onRangeChange(e.target.value, afterEnd)} style={inputStyle} />
        </div>
        <div>
          <label style={labelStyle}>{t("toLabel")}</label>
          <input type="date" value={afterEnd} min={afterStart || undefined} onChange={(e) => e.target.value && onRangeChange(afterStart, e.target.value)} style={inputStyle} />
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
            <span style={{ fontSize: 13, maxWidth: 180, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{storesLabel}</span>
            <ChevronDown size={13} color="#6B7280" style={{ transform: storesOpen ? "rotate(180deg)" : "none", transition: "transform 0.15s" }} />
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
                <label style={{ display: "flex", alignItems: "center", gap: 10, padding: "9px 12px", borderBottom: "1px solid #111827", cursor: "pointer" }}>
                  <input
                    type="checkbox"
                    checked={draftStoreIds.length === 0}
                    onChange={() => setDraftStoreIds([])}
                    style={{ accentColor: "#3B82F6", width: 14, height: 14, cursor: "pointer" }}
                  />
                  <span style={{ color: draftStoreIds.length === 0 ? "#E8EDF5" : "#9CA3AF", fontSize: 13, fontWeight: draftStoreIds.length === 0 ? 600 : 400 }}>
                    {t("selectAllStores")}
                  </span>
                </label>
                {stores.map((s) => {
                  const checked = draftStoreIds.includes(s.id);
                  return (
                    <label key={s.id} style={{ display: "flex", alignItems: "center", gap: 10, padding: "9px 12px", borderBottom: "1px solid #111827", cursor: "pointer" }}>
                      <input type="checkbox" checked={checked} onChange={() => toggleDraftStore(s.id)} style={{ accentColor: "#3B82F6", width: 14, height: 14, cursor: "pointer" }} />
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
                  style={{ width: "100%", background: "#1D3461", border: "1px solid #3B82F6", borderRadius: 7, color: "#93C5FD", fontSize: 12, fontWeight: 600, padding: "8px 0", cursor: "pointer" }}
                >
                  {t("doneButton")}
                </button>
              </div>
            </div>
          )}
        </div>

        <Btn
          variant="primary"
          disabled={!canAnalyze || isAnalyzing}
          icon={isAnalyzing ? <Loader2 size={13} className="animate-spin" /> : undefined}
          onClick={onAnalyzeClick}
        >
          {isAnalyzing ? t("analyzing") : hasReportForCurrentSegment ? t("refreshButton") : t("analyzeButton")}
        </Btn>
      </div>

      <div style={{ display: "flex", flexWrap: "wrap", gap: 6, alignItems: "center" }}>
        <span style={{ color: "#4B5563", fontSize: 11.5 }}>{t("presetsLabel")}</span>
        {presets.map((p) => (
          <button
            key={p.key}
            type="button"
            onClick={() => {
              const [s, e] = p.range();
              onRangeChange(s, e);
            }}
            style={{ padding: "4px 10px", fontSize: 11.5, fontWeight: 600, borderRadius: 6, border: "1px solid #1F2937", cursor: "pointer", background: "#0D1117", color: "#6B7280" }}
          >
            {t(`preset.${p.key}`)}
          </button>
        ))}
      </div>

      {beforeWindow && (
        <div style={{ color: "#6B7280", fontSize: 12 }}>
          {t("beforeWindowLabel")} <span style={{ color: "#9CA3AF", fontFamily: "monospace" }}>{beforeWindow.start} — {beforeWindow.end}</span>
          <span style={{ color: "#4B5563" }}> · {t("beforeWindowHint")}</span>
        </div>
      )}
    </div>
  );
}
