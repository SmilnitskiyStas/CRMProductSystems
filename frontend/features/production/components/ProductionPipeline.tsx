"use client";

import { useState, type ReactNode } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { useProductionPipeline } from "../hooks/useProductionInsights";
import type { PipelineByItemDto, ProductionPipelineDto } from "../insights-types";
import { ProductionRequestDetail } from "./ProductionRequestDetail";
import { Pill, StateBox, cardStyle, fmtDate, fmtQty, mutedText, useIntlLocale } from "./InsightsShared";

const MAX_CARDS = 8;

function todayYmd(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

const STAGE_COLORS: Record<string, { c: string; bg: string }> = {
  awaiting_order: { c: "#FBBF24", bg: "#3B2F0B" },
  planned: { c: "#60A5FA", bg: "#102A4C" },
  in_production: { c: "#A78BFA", bg: "#2A1F4D" },
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
  caption?: ReactNode;
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
      <div style={{ flex: 1, display: "flex", flexDirection: "column", gap: 6 }}>{children}</div>
      {footer}
      {!last && (
        <span className="pl-arrow" aria-hidden style={{ color: accent }}>
          →
        </span>
      )}
    </div>
  );
}

/** Compact request card — a real button so it is keyboard accessible. */
function ReqCard({
  store,
  line2,
  qty,
  chip,
  onOpen,
}: {
  store?: string | null;
  line2: string;
  qty: string;
  chip?: ReactNode;
  onOpen: () => void;
}) {
  return (
    <button type="button" className="pl-card" onClick={onOpen}>
      <span style={{ display: "flex", justifyContent: "space-between", gap: 8, alignItems: "center" }}>
        <span style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{store ?? "—"}</span>
        <span style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 700, whiteSpace: "nowrap" }}>{qty}</span>
      </span>
      <span style={{ display: "flex", justifyContent: "space-between", gap: 8, alignItems: "center", ...mutedText }}>
        <span>{line2}</span>
        {chip}
      </span>
    </button>
  );
}

function ShowAll({ total, href, label }: { total: number; href: string; label: string }) {
  if (total <= MAX_CARDS) return null;
  return (
    <Link href={href} style={{ color: "#60A5FA", fontSize: 12, textDecoration: "none", marginTop: 2 }}>
      {label} ({total}) →
    </Link>
  );
}

function ByItemChips({ rows, intl }: { rows: PipelineByItemDto[]; intl: string }) {
  return (
    <div style={{ display: "flex", flexWrap: "wrap", gap: 6 }}>
      {rows.map((r, i) => (
        <Pill key={r.itemId ?? i} color="#E8EDF5" bg="#1F2937">
          {r.itemName ?? "—"} · {fmtQty(r.qty, intl)} {r.unit ?? ""}
        </Pill>
      ))}
    </div>
  );
}

export function ProductionPipeline({ locationId }: { locationId?: string }) {
  const t = useTranslations("Dashboard.production.overview");
  const intl = useIntlLocale();
  const [openId, setOpenId] = useState<string | null>(null);

  const today = todayYmd();
  const { data, isLoading, isError } = useProductionPipeline({ from: today, to: today, locationId });

  const d: ProductionPipelineDto = data ?? {};
  const newReqs = d.newRequests?.requests ?? [];
  const inWork = d.inWork?.requests ?? [];
  const ready = d.ready?.requests ?? [];
  const shipped = d.shipped ?? {};
  const shippedReqs = shipped.requests ?? [];
  const stages = d.inWorkByStage ?? {};
  const asOf = t("asOfNow");

  const newCount = d.newRequests?.count ?? newReqs.length;
  const inWorkCount = d.inWork?.count ?? inWork.length;
  const readyCount = d.ready?.count ?? ready.length;
  const shippedCount = shipped.count ?? shippedReqs.length;

  const fmtTime = (v?: string | null) => {
    if (!v) return "—";
    const dt = new Date(v);
    return isNaN(dt.getTime()) ? "—" : dt.toLocaleTimeString(intl, { hour: "2-digit", minute: "2-digit" });
  };

  return (
    <section style={{ marginBottom: 24 }}>
      <style>{`
        .pl-grid{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:28px}
        .pl-arrow{position:absolute;right:-22px;top:50%;transform:translateY(-50%);font-size:20px;font-weight:700}
        .pl-card{display:flex;flex-direction:column;gap:4px;width:100%;text-align:left;cursor:pointer;background:#111827;border:1px solid #1F2937;border-radius:8px;padding:8px 10px;font-family:inherit;transition:background .12s,border-color .12s}
        .pl-card:hover{background:#162033;border-color:#2B3A55}
        .pl-card:focus-visible{outline:2px solid #3B82F6;outline-offset:1px}
        @media (max-width:1800px){.pl-grid{grid-template-columns:repeat(2,minmax(0,1fr))}.pl-win:nth-child(2) .pl-arrow{display:none}}
        @media (max-width:720px){.pl-grid{grid-template-columns:minmax(0,1fr);gap:28px}
          .pl-arrow{right:50%;top:auto;bottom:-24px;transform:translateX(50%) rotate(90deg)!important;display:block!important}}
      `}</style>

      <div style={{ marginBottom: 12 }}>
        <div style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 700 }}>{t("pipelineTitle")}</div>
        <div style={mutedText}>{t("pipelineSubtitle")}</div>
      </div>

      {isLoading ? (
        <StateBox kind="loading">{t("loading")}</StateBox>
      ) : isError ? (
        <StateBox kind="error">{t("loadError")}</StateBox>
      ) : (
        <div className="pl-grid">
          {/* 1. New requests */}
          <Win step={1} title={t("winNew")} count={newCount} accent="#60A5FA" caption={asOf}>
            {newReqs.length === 0 ? (
              <div style={mutedText}>{t("emptyNew")}</div>
            ) : (
              newReqs.slice(0, MAX_CARDS).map((r) => (
                <ReqCard
                  key={r.id}
                  store={r.storeName}
                  line2={`${t("neededBy")}: ${fmtDate(r.neededBy, intl)}`}
                  qty={fmtQty(r.totalQty, intl)}
                  onOpen={() => setOpenId(r.id)}
                />
              ))
            )}
            <ShowAll total={newCount} href="/production/requests" label={t("showAll")} />
          </Win>

          {/* 2. In work */}
          <Win
            step={2}
            title={t("winInWork")}
            count={inWorkCount}
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
              inWork.slice(0, MAX_CARDS).map((r) => {
                const sc = STAGE_COLORS[r.productionStage ?? ""];
                return (
                  <ReqCard
                    key={r.id}
                    store={r.storeName}
                    line2={`${t("neededBy")}: ${fmtDate(r.neededBy, intl)}`}
                    qty={fmtQty(r.totalQty, intl)}
                    chip={
                      r.productionStage ? (
                        <Pill color={sc?.c} bg={sc?.bg}>
                          {sc ? t(`stage_${r.productionStage}`) : r.productionStage}
                        </Pill>
                      ) : undefined
                    }
                    onOpen={() => setOpenId(r.id)}
                  />
                );
              })
            )}
            <ShowAll total={inWorkCount} href="/production/orders" label={t("showAll")} />
          </Win>

          {/* 3. Ready */}
          <Win step={3} title={t("winReady")} count={readyCount} accent="#FBBF24" caption={asOf}>
            {ready.length === 0 ? (
              <div style={mutedText}>{t("emptyReady")}</div>
            ) : (
              <>
                {ready.slice(0, MAX_CARDS).map((r) => (
                  <ReqCard
                    key={r.id}
                    store={r.storeName}
                    line2={`${t("neededBy")}: ${fmtDate(r.neededBy, intl)}`}
                    qty={fmtQty(r.totalQty, intl)}
                    onOpen={() => setOpenId(r.id)}
                  />
                ))}
                <ShowAll total={readyCount} href="/production/shipments" label={t("showAll")} />
                {!!d.ready?.byItem?.length && (
                  <div style={{ marginTop: 6 }}>
                    <div style={{ color: "#9CA3AF", fontSize: 11, fontWeight: 600, textTransform: "uppercase", marginBottom: 4 }}>{t("totalByItem")}</div>
                    <ByItemChips rows={d.ready.byItem} intl={intl} />
                  </div>
                )}
              </>
            )}
          </Win>

          {/* 4. Shipped today */}
          <Win
            step={4}
            title={t("winShipped")}
            count={shippedCount}
            accent="#4ADE80"
            caption={
              <>
                {t("forToday")} · {t("totalQty")}: {fmtQty(shipped.totalQty ?? 0, intl)}
              </>
            }
            last
          >
            {shippedReqs.length === 0 ? (
              <div style={mutedText}>{t("emptyShippedToday")}</div>
            ) : (
              shippedReqs.slice(0, MAX_CARDS).map((r) => (
                <ReqCard
                  key={r.id}
                  store={r.storeName}
                  line2={`${t("colShippedAt")}: ${fmtTime(r.shippedAt)}`}
                  qty={fmtQty(r.totalQty, intl)}
                  onOpen={() => setOpenId(r.id)}
                />
              ))
            )}
            <ShowAll total={shippedCount} href="/production/shipments" label={t("showAll")} />
          </Win>
        </div>
      )}

      {openId && <ProductionRequestDetail id={openId} mode="production" canProduce onClose={() => setOpenId(null)} />}
    </section>
  );
}
