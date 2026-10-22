"use client";

import { useState, type CSSProperties, type ReactNode } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { useProductionPipeline } from "../hooks/useProductionInsights";
import type { PipelineByItemDto, PipelineLineDto, ProductionPipelineDto } from "../insights-types";
import { Pill, StateBox, cardStyle, fmtDate, fmtQty, mutedText, selectStyle, useIntlLocale } from "./InsightsShared";

type Preset = "today" | "week" | "month" | "custom";

function ymd(d: Date): string {
  const m = String(d.getMonth() + 1).padStart(2, "0");
  const day = String(d.getDate()).padStart(2, "0");
  return `${d.getFullYear()}-${m}-${day}`;
}

function rangeFor(preset: Exclude<Preset, "custom">): { from: string; to: string } {
  const now = new Date();
  const to = ymd(now);
  if (preset === "today") return { from: to, to };
  const back = preset === "week" ? 6 : 29;
  const f = new Date(now.getFullYear(), now.getMonth(), now.getDate() - back);
  return { from: ymd(f), to };
}

function diffDays(from: string, to: string): number {
  return Math.round((new Date(`${to}T00:00:00`).getTime() - new Date(`${from}T00:00:00`).getTime()) / 86_400_000);
}

const th: CSSProperties = {
  textAlign: "left",
  color: "#6B7280",
  fontSize: 11,
  fontWeight: 600,
  textTransform: "uppercase",
  letterSpacing: "0.04em",
  padding: "6px 8px",
  borderBottom: "1px solid #1F2937",
  whiteSpace: "nowrap",
};
const td: CSSProperties = { padding: "7px 8px", fontSize: 12.5, color: "#E8EDF5", borderBottom: "1px solid #161D29", verticalAlign: "top" };
const numTd: CSSProperties = { ...td, textAlign: "right", fontWeight: 600, whiteSpace: "nowrap" };

function Tbl({ head, children, rightCols = [] }: { head: string[]; children: ReactNode; rightCols?: number[] }) {
  return (
    <div style={{ overflowX: "auto" }}>
      <table style={{ width: "100%", borderCollapse: "collapse" }}>
        <thead>
          <tr>
            {head.map((h, i) => (
              <th key={i} style={{ ...th, textAlign: rightCols.includes(i) ? "right" : "left" }}>
                {h}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>{children}</tbody>
      </table>
    </div>
  );
}

function Items({ lines }: { lines?: PipelineLineDto[] }) {
  const intl = useIntlLocale();
  if (!lines?.length) return <span style={mutedText}>—</span>;
  const shown = lines.slice(0, 2);
  const rest = lines.length - shown.length;
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 2 }}>
      {shown.map((l, i) => (
        <span key={i}>
          {l.itemName ?? "—"} <span style={mutedText}>× {fmtQty(l.qty, intl)} {l.unit ?? ""}</span>
        </span>
      ))}
      {rest > 0 && <span style={mutedText}>+{rest}</span>}
    </div>
  );
}

const btnStyle: CSSProperties = {
  display: "inline-block",
  background: "#1D4ED8",
  color: "#fff",
  borderRadius: 8,
  padding: "6px 12px",
  fontSize: 12,
  fontWeight: 600,
  textDecoration: "none",
};

function Win({
  step,
  title,
  count,
  accent,
  caption,
  headRight,
  children,
  footer,
  last,
}: {
  step: number;
  title: string;
  count: number;
  accent: string;
  caption?: string;
  headRight?: ReactNode;
  children: ReactNode;
  footer?: ReactNode;
  last?: boolean;
}) {
  return (
    <div className="pl-win" style={{ ...cardStyle, borderTop: `3px solid ${accent}`, position: "relative", display: "flex", flexDirection: "column", gap: 10, minWidth: 0 }}>
      <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
        <span style={{ width: 22, height: 22, borderRadius: 999, background: accent, color: "#0B0F14", fontSize: 12, fontWeight: 700, display: "inline-flex", alignItems: "center", justifyContent: "center", flexShrink: 0 }}>
          {step}
        </span>
        <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 700, flex: 1 }}>{title}</div>
        <span style={{ color: accent, fontSize: 18, fontWeight: 700 }}>{count}</span>
      </div>
      {caption && <div style={mutedText}>{caption}</div>}
      {headRight}
      <div style={{ flex: 1 }}>{children}</div>
      {footer}
      {!last && (
        <span className="pl-arrow" aria-hidden style={{ color: accent }}>
          →
        </span>
      )}
    </div>
  );
}

const STAGE_COLORS: Record<string, { c: string; bg: string }> = {
  awaiting_order: { c: "#FBBF24", bg: "#3B2F0B" },
  planned: { c: "#60A5FA", bg: "#102A4C" },
  in_production: { c: "#A78BFA", bg: "#2A1F4D" },
};

function Stat({ label, value, color }: { label: string; value: number; color: string }) {
  return (
    <div style={{ ...cardStyle, padding: 12 }}>
      <div style={{ color: "#6B7280", fontSize: 11, fontWeight: 600, textTransform: "uppercase", letterSpacing: "0.05em" }}>{label}</div>
      <div style={{ color, fontSize: 24, fontWeight: 700, marginTop: 4 }}>{value}</div>
    </div>
  );
}

function ByItemTable({ rows, intl, itemLabel, qtyLabel }: { rows: PipelineByItemDto[]; intl: string; itemLabel: string; qtyLabel: string }) {
  return (
    <Tbl head={[itemLabel, qtyLabel]} rightCols={[1]}>
      {rows.map((r, i) => (
        <tr key={r.itemId ?? i}>
          <td style={td}>{r.itemName ?? "—"}</td>
          <td style={numTd}>
            {fmtQty(r.qty, intl)} {r.unit ?? ""}
          </td>
        </tr>
      ))}
    </Tbl>
  );
}

function PeriodStats({ shipped, intl, t }: { shipped: NonNullable<ProductionPipelineDto["shipped"]>; intl: string; t: ReturnType<typeof useTranslations> }) {
  const perDay = (shipped.perDay ?? []).map((p) => ({
    date: fmtDate(p.date, intl).slice(0, 5),
    qty: p.qty ?? 0,
    requests: p.requests ?? 0,
  }));
  const byItem = shipped.byItem ?? [];
  const byStore = shipped.byStore ?? [];
  return (
    <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))", gap: 12, marginTop: 12 }}>
      <div style={cardStyle}>
        <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 700, marginBottom: 8 }}>{t("statsByItem")}</div>
        {byItem.length ? <ByItemTable rows={byItem} intl={intl} itemLabel={t("colItem")} qtyLabel={t("colQty")} /> : <div style={mutedText}>{t("listEmpty")}</div>}
      </div>
      <div style={cardStyle}>
        <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 700, marginBottom: 8 }}>{t("statsByStore")}</div>
        {byStore.length ? (
          <Tbl head={[t("colStore"), t("colRequests"), t("colQty")]} rightCols={[1, 2]}>
            {byStore.map((s, i) => (
              <tr key={s.storeLocationId ?? i}>
                <td style={td}>{s.storeName ?? "—"}</td>
                <td style={numTd}>{s.requests ?? 0}</td>
                <td style={numTd}>{fmtQty(s.qty, intl)}</td>
              </tr>
            ))}
          </Tbl>
        ) : (
          <div style={mutedText}>{t("listEmpty")}</div>
        )}
      </div>
      <div style={cardStyle}>
        <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 700, marginBottom: 8 }}>{t("statsPerDay")}</div>
        {perDay.length ? (
          <div style={{ width: "100%", height: 180 }}>
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={perDay} margin={{ top: 4, right: 4, left: -16, bottom: 0 }}>
                <CartesianGrid stroke="#1F2937" vertical={false} />
                <XAxis dataKey="date" tick={{ fill: "#6B7280", fontSize: 11 }} stroke="#1F2937" />
                <YAxis tick={{ fill: "#6B7280", fontSize: 11 }} stroke="#1F2937" allowDecimals={false} />
                <Tooltip
                  contentStyle={{ background: "#111827", border: "1px solid #1F2937", borderRadius: 8, fontSize: 12 }}
                  labelStyle={{ color: "#E8EDF5" }}
                  cursor={{ fill: "#1F293766" }}
                />
                <Bar dataKey="qty" name={t("colQty")} fill="#4ADE80" radius={[3, 3, 0, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </div>
        ) : (
          <div style={mutedText}>{t("listEmpty")}</div>
        )}
      </div>
    </div>
  );
}

export function ProductionPipeline({ locationId }: { locationId?: string }) {
  const t = useTranslations("Dashboard.production.overview");
  const intl = useIntlLocale();
  const [preset, setPreset] = useState<Preset>("today");
  const [customFrom, setCustomFrom] = useState(() => rangeFor("week").from);
  const [customTo, setCustomTo] = useState(() => rangeFor("today").to);
  const [statsOpen, setStatsOpen] = useState(false);

  const range = preset === "custom" ? { from: customFrom, to: customTo } : rangeFor(preset);
  let rangeError: string | null = null;
  if (preset === "custom") {
    if (!customFrom || !customTo) rangeError = t("periodInvalid");
    else if (customFrom > customTo) rangeError = t("periodFromAfterTo");
    else if (diffDays(customFrom, customTo) > 365) rangeError = t("periodTooLong");
  }

  const { data, isLoading, isError } = useProductionPipeline({
    from: rangeError ? "" : range.from,
    to: rangeError ? "" : range.to,
    locationId,
  });

  const d: ProductionPipelineDto = data ?? {};
  const newReqs = d.newRequests?.requests ?? [];
  const inWork = d.inWork?.requests ?? [];
  const ready = d.ready?.requests ?? [];
  const shipped = d.shipped ?? {};
  const shippedReqs = shipped.requests ?? [];
  const stages = d.inWorkByStage ?? {};
  const stats = d.stats ?? {};
  const asOf = t("asOfNow");

  const presets: { key: Preset; label: string }[] = [
    { key: "today", label: t("periodToday") },
    { key: "week", label: t("periodWeek") },
    { key: "month", label: t("periodMonth") },
    { key: "custom", label: t("periodCustom") },
  ];

  const fmtDateTime = (v?: string | null) => {
    if (!v) return "—";
    const dt = new Date(v);
    return isNaN(dt.getTime()) ? "—" : dt.toLocaleString(intl, { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" });
  };

  return (
    <section style={{ marginBottom: 24 }}>
      <style>{`
        .pl-grid{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:28px}
        .pl-arrow{position:absolute;right:-22px;top:50%;transform:translateY(-50%);font-size:20px;font-weight:700}
        @media (max-width:1800px){.pl-grid{grid-template-columns:repeat(2,minmax(0,1fr))}.pl-win:nth-child(2) .pl-arrow{display:none}}
        @media (max-width:720px){.pl-grid{grid-template-columns:minmax(0,1fr);gap:28px}
          .pl-arrow{right:50%;top:auto;bottom:-24px;transform:translateX(50%) rotate(90deg)!important;display:block!important}}
      `}</style>

      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: 10, marginBottom: 12 }}>
        <div>
          <div style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 700 }}>{t("pipelineTitle")}</div>
          <div style={mutedText}>{t("pipelineSubtitle")}</div>
        </div>
        <div style={{ display: "flex", gap: 6, alignItems: "center", flexWrap: "wrap" }}>
          <span style={mutedText}>{t("period")}:</span>
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
              <input type="date" aria-label={t("periodFrom")} value={customFrom} max={customTo || undefined} onChange={(e) => setCustomFrom(e.target.value)} style={selectStyle} />
              <span style={mutedText}>—</span>
              <input type="date" aria-label={t("periodTo")} value={customTo} min={customFrom || undefined} onChange={(e) => setCustomTo(e.target.value)} style={selectStyle} />
            </>
          )}
        </div>
      </div>
      {rangeError && <div style={{ color: "#F87171", fontSize: 12, marginBottom: 10 }}>{rangeError}</div>}

      {isLoading && !rangeError ? (
        <StateBox kind="loading">{t("loading")}</StateBox>
      ) : isError ? (
        <StateBox kind="error">{t("loadError")}</StateBox>
      ) : (
        <>
          <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(160px, 1fr))", gap: 12, marginBottom: 16 }}>
            <Stat label={t("statReceived")} value={stats.received ?? 0} color="#60A5FA" />
            <Stat label={t("statAccepted")} value={stats.accepted ?? 0} color="#A78BFA" />
            <Stat label={t("statRejected")} value={stats.rejected ?? 0} color="#F87171" />
            <Stat label={t("statShipped")} value={stats.shipped ?? 0} color="#4ADE80" />
          </div>

          <div className="pl-grid">
            {/* 1. New requests */}
            <Win
              step={1}
              title={t("winNew")}
              count={d.newRequests?.count ?? newReqs.length}
              accent="#60A5FA"
              caption={asOf}
              footer={
                <Link href="/production/requests" style={btnStyle}>
                  {t("openRequests")}
                </Link>
              }
            >
              {newReqs.length === 0 ? (
                <div style={mutedText}>{t("emptyNew")}</div>
              ) : (
                <Tbl head={[t("colStore"), t("colNeededBy"), t("colItems"), t("colQty")]} rightCols={[3]}>
                  {newReqs.map((r) => (
                    <tr key={r.id}>
                      <td style={td}>{r.storeName ?? "—"}</td>
                      <td style={{ ...td, whiteSpace: "nowrap" }}>{fmtDate(r.neededBy, intl)}</td>
                      <td style={td}>
                        <Items lines={r.lines} />
                      </td>
                      <td style={numTd}>{fmtQty(r.totalQty, intl)}</td>
                    </tr>
                  ))}
                </Tbl>
              )}
            </Win>

            {/* 2. In work */}
            <Win
              step={2}
              title={t("winInWork")}
              count={d.inWork?.count ?? inWork.length}
              accent="#A78BFA"
              caption={asOf}
              headRight={
                <div style={{ display: "flex", gap: 6, flexWrap: "wrap" }}>
                  <Pill color={STAGE_COLORS.awaiting_order.c} bg={STAGE_COLORS.awaiting_order.bg}>
                    {t("stage_awaiting_order")}: {stages.awaitingOrder ?? 0}
                  </Pill>
                  <Pill color={STAGE_COLORS.planned.c} bg={STAGE_COLORS.planned.bg}>
                    {t("stage_planned")}: {stages.planned ?? 0}
                  </Pill>
                  <Pill color={STAGE_COLORS.in_production.c} bg={STAGE_COLORS.in_production.bg}>
                    {t("stage_in_production")}: {stages.inProduction ?? 0}
                  </Pill>
                </div>
              }
            >
              {inWork.length === 0 ? (
                <div style={mutedText}>{t("emptyInWork")}</div>
              ) : (
                <Tbl head={[t("colStore"), t("colNeededBy"), t("colStage"), t("colItems"), t("colQty"), t("colOrders")]} rightCols={[4]}>
                  {inWork.map((r) => {
                    const sc = STAGE_COLORS[r.productionStage ?? ""];
                    return (
                      <tr key={r.id}>
                        <td style={td}>{r.storeName ?? "—"}</td>
                        <td style={{ ...td, whiteSpace: "nowrap" }}>{fmtDate(r.neededBy, intl)}</td>
                        <td style={td}>
                          <Pill color={sc?.c} bg={sc?.bg}>
                            {r.productionStage && sc ? t(`stage_${r.productionStage}`) : (r.productionStage ?? "—")}
                          </Pill>
                        </td>
                        <td style={td}>
                          <Items lines={r.lines} />
                        </td>
                        <td style={numTd}>{fmtQty(r.totalQty, intl)}</td>
                        <td style={td}>
                          {r.orders?.length ? (
                            <div style={{ display: "flex", flexDirection: "column", gap: 2 }}>
                              {r.orders.map((o) => (
                                <Link key={o.orderId} href={`/production/orders/${o.orderId}`} style={{ color: "#60A5FA", textDecoration: "none", whiteSpace: "nowrap" }}>
                                  {t("order")} →
                                </Link>
                              ))}
                            </div>
                          ) : (
                            <span style={mutedText}>—</span>
                          )}
                        </td>
                      </tr>
                    );
                  })}
                </Tbl>
              )}
            </Win>

            {/* 3. Ready */}
            <Win
              step={3}
              title={t("winReady")}
              count={d.ready?.count ?? ready.length}
              accent="#FBBF24"
              caption={asOf}
              footer={
                <Link href="/production/shipments" style={btnStyle}>
                  {t("toShipments")}
                </Link>
              }
            >
              {ready.length === 0 ? (
                <div style={mutedText}>{t("emptyReady")}</div>
              ) : (
                <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
                  <Tbl head={[t("colStore"), t("colNeededBy"), t("colItems"), t("colQty")]} rightCols={[3]}>
                    {ready.map((r) => (
                      <tr key={r.id}>
                        <td style={td}>{r.storeName ?? "—"}</td>
                        <td style={{ ...td, whiteSpace: "nowrap" }}>{fmtDate(r.neededBy, intl)}</td>
                        <td style={td}>
                          <Items lines={r.lines} />
                        </td>
                        <td style={numTd}>{fmtQty(r.totalQty, intl)}</td>
                      </tr>
                    ))}
                  </Tbl>
                  {!!d.ready?.byItem?.length && (
                    <div>
                      <div style={{ color: "#9CA3AF", fontSize: 11, fontWeight: 600, textTransform: "uppercase", marginBottom: 4 }}>{t("totalByItem")}</div>
                      <ByItemTable rows={d.ready.byItem} intl={intl} itemLabel={t("colItem")} qtyLabel={t("colQty")} />
                    </div>
                  )}
                </div>
              )}
            </Win>

            {/* 4. Shipped */}
            <Win
              step={4}
              title={t("winShipped")}
              count={shipped.count ?? shippedReqs.length}
              accent="#4ADE80"
              caption={`${t("forPeriod")}: ${fmtDate(range.from, intl)}${range.from !== range.to ? ` — ${fmtDate(range.to, intl)}` : ""} · ${t("totalQty")}: ${fmtQty(shipped.totalQty ?? 0, intl)}`}
              last
            >
              {shippedReqs.length === 0 ? (
                <div style={mutedText}>{t("emptyShipped")}</div>
              ) : (
                <Tbl head={[t("colStore"), t("colShippedAt"), t("colItems"), t("colQty"), t("colTransfer")]} rightCols={[3]}>
                  {shippedReqs.map((r) => (
                    <tr key={r.id}>
                      <td style={td}>{r.storeName ?? "—"}</td>
                      <td style={{ ...td, whiteSpace: "nowrap" }}>{fmtDateTime(r.shippedAt)}</td>
                      <td style={td}>
                        <Items lines={r.lines} />
                      </td>
                      <td style={numTd}>{fmtQty(r.totalQty, intl)}</td>
                      <td style={td}>
                        {r.transferId || r.transferStatus ? (
                          <Link href="/transfers" style={{ color: "#60A5FA", textDecoration: "none", whiteSpace: "nowrap" }}>
                            {r.transferStatus ?? t("transfer")} →
                          </Link>
                        ) : (
                          <span style={mutedText}>—</span>
                        )}
                      </td>
                    </tr>
                  ))}
                </Tbl>
              )}
            </Win>
          </div>

          <div style={{ marginTop: 14 }}>
            <button
              type="button"
              onClick={() => setStatsOpen((v) => !v)}
              aria-expanded={statsOpen}
              style={{ ...selectStyle, cursor: "pointer", fontWeight: 600 }}
            >
              {statsOpen ? "▾" : "▸"} {t("periodStats")}
            </button>
            {statsOpen && <PeriodStats shipped={shipped} intl={intl} t={t} />}
          </div>
        </>
      )}
    </section>
  );
}
