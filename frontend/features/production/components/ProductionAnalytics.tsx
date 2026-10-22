"use client";

import { useMemo, useState, type ReactNode } from "react";
import { useTranslations } from "next-intl";
import { Bar, BarChart, CartesianGrid, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { Btn } from "@/components/ui/Btn";
import { Table, type TableColumn } from "@/components/ui/Table";
import { useProductionAnalytics } from "../hooks/useProductionInsights";
import type { ProductionAnalyticsDto } from "../insights-types";
import {
  PageHeader,
  ProductionLocationSelect,
  StateBox,
  cardStyle,
  fmtDate,
  fmtQty,
  mutedText,
  selectStyle,
  useIntlLocale,
} from "./InsightsShared";

type Preset = "today" | "week" | "month" | "quarter" | "custom";
type ByItem = NonNullable<ProductionAnalyticsDto["byItem"]>[number];
type ByStore = NonNullable<ProductionAnalyticsDto["byStore"]>[number];

function ymd(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

function rangeFor(preset: Exclude<Preset, "custom">): { from: string; to: string } {
  const now = new Date();
  const back = { today: 0, week: 6, month: 29, quarter: 89 }[preset];
  return { from: ymd(new Date(now.getFullYear(), now.getMonth(), now.getDate() - back)), to: ymd(now) };
}

function diffDays(from: string, to: string): number {
  return Math.round((new Date(`${to}T00:00:00`).getTime() - new Date(`${from}T00:00:00`).getTime()) / 86_400_000);
}

function csvCell(v: unknown): string {
  const s = v == null ? "" : String(v);
  return /[";\n\r]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
}

function Delta({ cur, prev, inverse }: { cur: number; prev?: number; inverse?: boolean }) {
  if (prev == null) return null;
  if (prev === 0) return cur === 0 ? null : <span style={{ ...mutedText, fontSize: 11 }}>new</span>;
  const pct = ((cur - prev) / prev) * 100;
  if (Math.abs(pct) < 0.05) return <span style={{ ...mutedText, fontSize: 11 }}>0%</span>;
  const up = pct > 0;
  const good = inverse ? !up : up;
  return (
    <span style={{ color: good ? "#4ADE80" : "#F87171", fontSize: 11, fontWeight: 600 }}>
      {up ? "↑" : "↓"} {Math.abs(pct).toFixed(1)}%
    </span>
  );
}

function Kpi({ label, value, sub, color = "#E8EDF5" }: { label: string; value: ReactNode; sub?: ReactNode; color?: string }) {
  return (
    <div style={{ ...cardStyle, padding: 14 }}>
      <div style={{ color: "#6B7280", fontSize: 11, fontWeight: 600, textTransform: "uppercase", letterSpacing: "0.05em" }}>{label}</div>
      <div style={{ color, fontSize: 24, fontWeight: 700, marginTop: 4 }}>{value}</div>
      {sub && <div style={{ marginTop: 2, minHeight: 16 }}>{sub}</div>}
    </div>
  );
}

function FillBar({ rate }: { rate?: number | null }) {
  if (rate == null) return <span style={mutedText}>—</span>;
  const pct = Math.max(0, Math.min(100, rate * 100));
  const color = pct >= 90 ? "#4ADE80" : pct >= 60 ? "#FBBF24" : "#F87171";
  return (
    <div style={{ display: "flex", alignItems: "center", gap: 8, justifyContent: "center" }}>
      <div style={{ width: 60, height: 6, background: "#1F2937", borderRadius: 3, overflow: "hidden" }}>
        <div style={{ width: `${pct}%`, height: "100%", background: color }} />
      </div>
      <span style={{ fontSize: 12, minWidth: 38, textAlign: "right" }}>{pct.toFixed(0)}%</span>
    </div>
  );
}

function useSort<T>(rows: T[], init: string, getters: Record<string, (r: T) => number | string>) {
  const [sortBy, setSortBy] = useState(init);
  const [desc, setDesc] = useState(true);
  const sorted = useMemo(() => {
    const g = getters[sortBy];
    if (!g) return rows;
    return [...rows].sort((a, b) => {
      const x = g(a);
      const y = g(b);
      const c = typeof x === "string" || typeof y === "string" ? String(x).localeCompare(String(y)) : x - y;
      return desc ? -c : c;
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [rows, sortBy, desc]);
  const onSort = (k: string) => {
    if (k === sortBy) setDesc((v) => !v);
    else {
      setSortBy(k);
      setDesc(true);
    }
  };
  return { sorted, sortBy, desc, onSort };
}

export function ProductionAnalytics() {
  const t = useTranslations("Dashboard.production.analytics");
  const intl = useIntlLocale();
  const [preset, setPreset] = useState<Preset>("month");
  const [customFrom, setCustomFrom] = useState(() => rangeFor("month").from);
  const [customTo, setCustomTo] = useState(() => rangeFor("today").to);
  const [locationId, setLocationId] = useState("");
  const [mode, setMode] = useState<"counts" | "volumes">("counts");

  const range = preset === "custom" ? { from: customFrom, to: customTo } : rangeFor(preset);
  let rangeError: string | null = null;
  if (preset === "custom") {
    if (!customFrom || !customTo) rangeError = t("invalidRange");
    else if (customFrom > customTo) rangeError = t("fromAfterTo");
    else if (diffDays(customFrom, customTo) > 365) rangeError = t("tooLong");
  }

  const { data, isLoading, isError } = useProductionAnalytics({
    from: rangeError ? "" : range.from,
    to: rangeError ? "" : range.to,
    locationId: locationId || undefined,
  });

  const d: ProductionAnalyticsDto = data ?? {};
  const tot = d.totals ?? {};
  const prev = d.previous;
  const done = d.ordersDone ?? {};

  const items = useSort<ByItem>(d.byItem ?? [], "requestedQty", {
    itemName: (r) => r.itemName ?? "",
    requestedQty: (r) => r.requestedQty ?? 0,
    acceptedQty: (r) => r.acceptedQty ?? 0,
    shippedQty: (r) => r.shippedQty ?? 0,
    fillRate: (r) => r.fillRate ?? -1,
  });
  const stores = useSort<ByStore>(d.byStore ?? [], "requests", {
    storeName: (r) => r.storeName ?? "",
    requests: (r) => r.requests ?? 0,
    requestedQty: (r) => r.requestedQty ?? 0,
    shippedQty: (r) => r.shippedQty ?? 0,
    rejected: (r) => r.rejected ?? 0,
    avgLeadHours: (r) => r.avgLeadHours ?? -1,
  });

  const fmtHours = (h?: number | null) => {
    if (h == null) return "—";
    const mins = Math.round(h * 60);
    if (mins < 60) return t("durMin", { m: mins });
    if (mins < 24 * 60) return t("durHourMin", { h: Math.floor(mins / 60), m: mins % 60 });
    return t("durDayHour", { d: Math.floor(mins / 1440), h: Math.floor((mins % 1440) / 60) });
  };
  const pctFmt = (r?: number | null) => (r == null ? "—" : `${(r * 100).toFixed(0)}%`);

  const chartData = (d.perDay ?? []).map((p) => ({
    date: fmtDate(p.date, intl).slice(0, 5),
    received: p.received ?? 0,
    accepted: p.accepted ?? 0,
    shipped: p.shipped ?? 0,
    shippedQty: p.shippedQty ?? 0,
    producedQty: p.producedQty ?? 0,
  }));

  const presets: { key: Preset; label: string }[] = [
    { key: "today", label: t("presetToday") },
    { key: "week", label: t("presetWeek") },
    { key: "month", label: t("presetMonth") },
    { key: "quarter", label: t("presetQuarter") },
    { key: "custom", label: t("presetCustom") },
  ];

  const itemCols: TableColumn<ByItem>[] = [
    { key: "item", header: t("colItem"), sortKey: "itemName", render: (r) => `${r.itemName ?? "—"}${r.unit ? ` (${r.unit})` : ""}`, cellStyle: { color: "#E8EDF5" } },
    { key: "req", header: t("colRequestedQty"), sortKey: "requestedQty", render: (r) => fmtQty(r.requestedQty, intl) },
    { key: "acc", header: t("colAcceptedQty"), sortKey: "acceptedQty", render: (r) => fmtQty(r.acceptedQty, intl) },
    { key: "shp", header: t("colShippedQty"), sortKey: "shippedQty", render: (r) => fmtQty(r.shippedQty, intl) },
    { key: "fill", header: t("colFillRate"), sortKey: "fillRate", render: (r) => <FillBar rate={r.fillRate} /> },
  ];
  const storeCols: TableColumn<ByStore>[] = [
    { key: "store", header: t("colStore"), sortKey: "storeName", render: (r) => r.storeName ?? "—", cellStyle: { color: "#E8EDF5" } },
    { key: "reqs", header: t("colRequests"), sortKey: "requests", render: (r) => r.requests ?? 0 },
    { key: "req", header: t("colRequestedQty"), sortKey: "requestedQty", render: (r) => fmtQty(r.requestedQty, intl) },
    { key: "shp", header: t("colShippedQty"), sortKey: "shippedQty", render: (r) => fmtQty(r.shippedQty, intl) },
    { key: "rej", header: t("colRejected"), sortKey: "rejected", render: (r) => r.rejected ?? 0 },
    { key: "lead", header: t("colAvgLead"), sortKey: "avgLeadHours", render: (r) => fmtHours(r.avgLeadHours) },
  ];

  function exportCsv() {
    const lines: string[] = [];
    const push = (...c: unknown[]) => lines.push(c.map(csvCell).join(";"));
    push(t("byItemTitle"));
    push(t("colItem"), t("colRequestedQty"), t("colAcceptedQty"), t("colShippedQty"), t("colFillRate"));
    for (const r of d.byItem ?? []) push(r.itemName, r.requestedQty, r.acceptedQty, r.shippedQty, r.fillRate == null ? "" : (r.fillRate * 100).toFixed(1));
    push("");
    push(t("byStoreTitle"));
    push(t("colStore"), t("colRequests"), t("colRequestedQty"), t("colShippedQty"), t("colRejected"), t("colAvgLead"));
    for (const r of d.byStore ?? []) push(r.storeName, r.requests, r.requestedQty, r.shippedQty, r.rejected, r.avgLeadHours == null ? "" : r.avgLeadHours.toFixed(2));
    push("");
    push(t("chartTitle"));
    push(t("colDate"), t("serReceived"), t("serAccepted"), t("serShipped"), t("serShippedQty"), t("serProducedQty"));
    for (const p of d.perDay ?? []) push(p.date, p.received, p.accepted, p.shipped, p.shippedQty, p.producedQty);
    const blob = new Blob(["﻿" + lines.join("\r\n")], { type: "text/csv;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `production-analytics_${range.from}_${range.to}.csv`;
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
  }

  const sectionTitle = { color: "#E8EDF5", fontSize: 15, fontWeight: 700, marginBottom: 10 } as const;
  const hasData = !!data && ((tot.received ?? 0) > 0 || (d.perDay?.length ?? 0) > 0 || (d.byItem?.length ?? 0) > 0);

  return (
    <div>
      <PageHeader
        title={t("title")}
        subtitle={t("subtitle")}
        right={
          <>
            <ProductionLocationSelect value={locationId} onChange={setLocationId} allLabel={t("allLocations")} />
            <Btn variant="ghost" onClick={exportCsv} disabled={!hasData}>
              {t("exportCsv")}
            </Btn>
          </>
        }
      />

      <div style={{ display: "flex", gap: 6, alignItems: "center", flexWrap: "wrap", marginBottom: 16 }}>
        {presets.map((p) => (
          <button
            key={p.key}
            type="button"
            onClick={() => setPreset(p.key)}
            style={{
              ...selectStyle,
              cursor: "pointer",
              padding: "6px 12px",
              background: preset === p.key ? "#1D4ED8" : "#111827",
              borderColor: preset === p.key ? "#1D4ED8" : "#1F2937",
              color: preset === p.key ? "#fff" : "#E8EDF5",
              fontWeight: preset === p.key ? 600 : 400,
            }}
          >
            {p.label}
          </button>
        ))}
        {preset === "custom" && (
          <>
            <input type="date" aria-label={t("from")} value={customFrom} max={customTo || undefined} onChange={(e) => setCustomFrom(e.target.value)} style={selectStyle} />
            <span style={mutedText}>—</span>
            <input type="date" aria-label={t("to")} value={customTo} min={customFrom || undefined} onChange={(e) => setCustomTo(e.target.value)} style={selectStyle} />
          </>
        )}
      </div>
      {rangeError && <div style={{ color: "#F87171", fontSize: 12, marginBottom: 10 }}>{rangeError}</div>}

      {rangeError ? null : isLoading ? (
        <StateBox kind="loading">{t("loading")}</StateBox>
      ) : isError ? (
        <StateBox kind="error">{t("loadError")}</StateBox>
      ) : !hasData ? (
        <StateBox kind="empty">{t("empty")}</StateBox>
      ) : (
        <div style={{ display: "flex", flexDirection: "column", gap: 24 }}>
          <div>
            <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(190px, 1fr))", gap: 12 }}>
              <Kpi label={t("kpiReceived")} value={tot.received ?? 0} color="#60A5FA" sub={<Delta cur={tot.received ?? 0} prev={prev?.received} />} />
              <Kpi label={t("kpiAccepted")} value={tot.accepted ?? 0} color="#A78BFA" sub={<Delta cur={tot.accepted ?? 0} prev={prev?.accepted} />} />
              <Kpi label={t("kpiRejected")} value={tot.rejected ?? 0} color="#F87171" sub={<Delta cur={tot.rejected ?? 0} prev={prev?.rejected} inverse />} />
              <Kpi label={t("kpiShipped")} value={tot.shipped ?? 0} color="#4ADE80" sub={<Delta cur={tot.shipped ?? 0} prev={prev?.shipped} />} />
              <Kpi label={t("kpiShippedQty")} value={fmtQty(tot.shippedQty ?? 0, intl)} color="#4ADE80" sub={<Delta cur={tot.shippedQty ?? 0} prev={prev?.shippedQty} />} />
            </div>
            <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(190px, 1fr))", gap: 12, marginTop: 12 }}>
              <Kpi label={t("kpiFillRate")} value={pctFmt(tot.fillRate)} sub={<span style={mutedText}>{t("fillRateHint")}</span>} />
              <Kpi label={t("kpiAvgReview")} value={fmtHours(tot.avgReviewHours)} />
              <Kpi label={t("kpiAvgLead")} value={fmtHours(tot.avgLeadHours)} />
              <Kpi
                label={t("kpiOrdersDone")}
                value={done.count ?? 0}
                sub={
                  <span style={mutedText}>
                    {fmtQty(done.qty ?? 0, intl)} · {t("avgShort")} {fmtHours(done.avgHours)}
                  </span>
                }
              />
            </div>
            {prev?.from && (
              <div style={{ ...mutedText, marginTop: 8 }}>
                {t("vsPrev")}: {fmtDate(prev.from, intl)} — {fmtDate(prev.to, intl)}
              </div>
            )}
          </div>

          <div style={cardStyle}>
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: 8, marginBottom: 10 }}>
              <div style={{ ...sectionTitle, marginBottom: 0 }}>{t("chartTitle")}</div>
              <div style={{ display: "flex", gap: 6 }}>
                {(["counts", "volumes"] as const).map((m) => (
                  <button
                    key={m}
                    type="button"
                    onClick={() => setMode(m)}
                    style={{
                      ...selectStyle,
                      cursor: "pointer",
                      padding: "4px 10px",
                      background: mode === m ? "#1D4ED8" : "#111827",
                      borderColor: mode === m ? "#1D4ED8" : "#1F2937",
                      color: mode === m ? "#fff" : "#E8EDF5",
                    }}
                  >
                    {t(m === "counts" ? "modeCounts" : "modeVolumes")}
                  </button>
                ))}
              </div>
            </div>
            {chartData.length === 0 ? (
              <div style={mutedText}>{t("empty")}</div>
            ) : (
              <div style={{ width: "100%", height: 260 }}>
                <ResponsiveContainer width="100%" height="100%">
                  <BarChart data={chartData} margin={{ top: 4, right: 8, left: -12, bottom: 0 }}>
                    <CartesianGrid stroke="#1F2937" vertical={false} />
                    <XAxis dataKey="date" tick={{ fill: "#6B7280", fontSize: 11 }} stroke="#1F2937" />
                    <YAxis tick={{ fill: "#6B7280", fontSize: 11 }} stroke="#1F2937" allowDecimals={mode === "volumes"} />
                    <Tooltip
                      contentStyle={{ background: "#111827", border: "1px solid #1F2937", borderRadius: 8, fontSize: 12 }}
                      labelStyle={{ color: "#E8EDF5" }}
                      cursor={{ fill: "#1F293766" }}
                    />
                    <Legend wrapperStyle={{ fontSize: 12, color: "#9CA3AF" }} />
                    {mode === "counts" ? (
                      <>
                        <Bar dataKey="received" name={t("serReceived")} fill="#60A5FA" radius={[3, 3, 0, 0]} />
                        <Bar dataKey="accepted" name={t("serAccepted")} fill="#A78BFA" radius={[3, 3, 0, 0]} />
                        <Bar dataKey="shipped" name={t("serShipped")} fill="#4ADE80" radius={[3, 3, 0, 0]} />
                      </>
                    ) : (
                      <>
                        <Bar dataKey="shippedQty" name={t("serShippedQty")} fill="#4ADE80" radius={[3, 3, 0, 0]} />
                        <Bar dataKey="producedQty" name={t("serProducedQty")} fill="#FBBF24" radius={[3, 3, 0, 0]} />
                      </>
                    )}
                  </BarChart>
                </ResponsiveContainer>
              </div>
            )}
          </div>

          <div>
            <div style={sectionTitle}>{t("byItemTitle")}</div>
            <Table
              columns={itemCols}
              rows={items.sorted}
              rowKey={(r) => r.itemId ?? r.itemName ?? ""}
              sortBy={items.sortBy}
              sortDescending={items.desc}
              onSort={items.onSort}
              emptyMessage={t("empty")}
              density="compact"
            />
          </div>

          <div>
            <div style={sectionTitle}>{t("byStoreTitle")}</div>
            <Table
              columns={storeCols}
              rows={stores.sorted}
              rowKey={(r) => r.storeLocationId ?? r.storeName ?? ""}
              sortBy={stores.sortBy}
              sortDescending={stores.desc}
              onSort={stores.onSort}
              emptyMessage={t("empty")}
              density="compact"
            />
          </div>

          <div style={cardStyle}>
            <div style={sectionTitle}>{t("reasonsTitle")}</div>
            {(d.rejectionReasons ?? []).length === 0 ? (
              <div style={mutedText}>{t("noReasons")}</div>
            ) : (
              <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
                {(d.rejectionReasons ?? []).map((r, i) => (
                  <div key={i} style={{ display: "flex", justifyContent: "space-between", gap: 12, fontSize: 13, color: "#E8EDF5" }}>
                    <span>{r.reason || "—"}</span>
                    <span style={{ fontWeight: 700 }}>{r.count ?? 0}</span>
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
