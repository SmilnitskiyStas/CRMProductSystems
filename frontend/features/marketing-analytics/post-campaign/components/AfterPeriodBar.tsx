"use client";

import { useTranslations } from "next-intl";
import { Loader2 } from "lucide-react";
import { Btn } from "@/components/ui/Btn";

interface BeforeWindow {
  start: string;
  end: string;
}

interface Props {
  afterStart: string;
  afterEnd: string;
  onRangeChange: (start: string, end: string) => void;
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
 * After-window date range + quick presets + the Analyze/Refresh trigger (source doc §10). No
 * longer renders its own store picker — the header's global StoreSelector (`useStoreContext`) now
 * supports picking one/several/all stores, so the page reads `storeIds` from there directly for
 * its query filters instead of this component owning a duplicate multi-select (TASK-515).
 */
export function AfterPeriodBar({
  afterStart,
  afterEnd,
  onRangeChange,
  beforeWindow,
  canAnalyze,
  isAnalyzing,
  hasReportForCurrentSegment,
  onAnalyzeClick,
}: Props) {
  const t = useTranslations("Dashboard.postCampaign.afterPeriodBar");

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
