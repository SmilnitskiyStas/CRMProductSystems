"use client";

import { useState, type ReactNode } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { useProductionOverview } from "../hooks/useProductionInsights";
import {
  PageHeader,
  ProductionLocationSelect,
  StateBox,
  cardStyle,
  mutedText,
  fmtDate,
  fmtQty,
  useIntlLocale,
  expiryColor,
  daysUntil,
} from "./InsightsShared";

function Kpi({ href, label, value, accent }: { href: string; label: string; value: number; accent: string }) {
  return (
    <Link href={href} style={{ ...cardStyle, textDecoration: "none", display: "block" }}>
      <div style={{ color: "#6B7280", fontSize: 11, fontWeight: 600, textTransform: "uppercase", letterSpacing: "0.05em" }}>{label}</div>
      <div style={{ color: value > 0 ? accent : "#E8EDF5", fontSize: 28, fontWeight: 700, marginTop: 6 }}>{value}</div>
    </Link>
  );
}

function ListCard({
  title,
  href,
  linkLabel,
  empty,
  children,
  isEmpty,
}: {
  title: string;
  href: string;
  linkLabel: string;
  empty: string;
  children: ReactNode;
  isEmpty: boolean;
}) {
  return (
    <div style={cardStyle}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 10 }}>
        <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 700 }}>{title}</div>
        <Link href={href} style={{ color: "#60A5FA", fontSize: 12, textDecoration: "none" }}>
          {linkLabel} →
        </Link>
      </div>
      {isEmpty ? (
        <div style={mutedText}>{empty}</div>
      ) : (
        <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>{children}</div>
      )}
    </div>
  );
}

const rowStyle = { display: "flex", justifyContent: "space-between", gap: 12, fontSize: 13, color: "#E8EDF5" } as const;

export function ProductionOverview() {
  const t = useTranslations("Dashboard.production.overview");
  const intl = useIntlLocale();
  const [locationId, setLocationId] = useState("");
  const { data, isLoading, isError } = useProductionOverview(locationId || undefined);

  const d = data ?? {};
  const kpiSum =
    (d.incomingRequests ?? 0) +
    (d.awaitingOrder ?? 0) +
    (d.ordersPlanned ?? 0) +
    (d.ordersInProgress ?? 0) +
    (d.readyToShip ?? 0) +
    (d.shortages ?? 0) +
    (d.expiringSoon ?? 0);

  return (
    <div>
      <PageHeader
        title={t("title")}
        subtitle={t("subtitle")}
        right={<ProductionLocationSelect value={locationId} onChange={setLocationId} allLabel={t("allLocations")} />}
      />
      {isLoading ? (
        <StateBox kind="loading">{t("loading")}</StateBox>
      ) : isError ? (
        <StateBox kind="error">{t("loadError")}</StateBox>
      ) : (
        <>
          <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(190px, 1fr))", gap: 12, marginBottom: 16 }}>
            <Kpi href="/production/requests" label={t("kpiNewRequests")} value={d.incomingRequests ?? 0} accent="#60A5FA" />
            <Kpi href="/production/requests" label={t("kpiAwaitingOrder")} value={d.awaitingOrder ?? 0} accent="#FBBF24" />
            <Kpi href="/production/orders" label={t("kpiPlanned")} value={d.ordersPlanned ?? 0} accent="#60A5FA" />
            <Kpi href="/production/orders" label={t("kpiInProgress")} value={d.ordersInProgress ?? 0} accent="#A78BFA" />
            <Kpi href="/production/shipments" label={t("kpiReadyToShip")} value={d.readyToShip ?? 0} accent="#4ADE80" />
            <Kpi href="/production/plan" label={t("kpiShortages")} value={d.shortages ?? 0} accent="#F87171" />
            <Kpi href="/production/warehouse" label={t("kpiExpiring")} value={d.expiringSoon ?? 0} accent="#FBBF24" />
          </div>

          {kpiSum === 0 && (
            <div style={{ ...cardStyle, color: "#4ADE80", fontSize: 14, fontWeight: 600, marginBottom: 16 }}>{t("allClear")}</div>
          )}

          <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(320px, 1fr))", gap: 12 }}>
            <ListCard title={t("listNewRequests")} href="/production/requests" linkLabel={t("open")} empty={t("listEmpty")} isEmpty={!d.newRequests?.length}>
              {d.newRequests?.map((r) => (
                <Link key={r.id} href="/production/requests" style={{ ...rowStyle, textDecoration: "none" }}>
                  <span>
                    {r.storeName ?? "—"} <span style={mutedText}>· {t("neededBy")} {fmtDate(r.neededBy, intl)}</span>
                  </span>
                  <span style={{ fontWeight: 600 }}>{fmtQty(r.totalQty, intl)}</span>
                </Link>
              ))}
            </ListCard>

            <ListCard title={t("listInProgress")} href="/production/orders" linkLabel={t("open")} empty={t("listEmpty")} isEmpty={!d.inProgressOrders?.length}>
              {d.inProgressOrders?.map((o) => (
                <Link key={o.id} href={`/production/orders/${o.id}`} style={{ ...rowStyle, textDecoration: "none" }}>
                  <span>
                    {o.recipeName ?? "—"} <span style={mutedText}>· {fmtDate(o.startedAt, intl)}</span>
                  </span>
                  <span style={{ fontWeight: 600 }}>{fmtQty(o.plannedQty, intl)}</span>
                </Link>
              ))}
            </ListCard>

            <ListCard title={t("listReady")} href="/production/shipments" linkLabel={t("open")} empty={t("listEmpty")} isEmpty={!d.readyToShipList?.length}>
              {d.readyToShipList?.map((r) => (
                <Link key={r.requestId} href="/production/shipments" style={{ ...rowStyle, textDecoration: "none" }}>
                  <span>{r.storeName ?? "—"}</span>
                  <span style={{ fontWeight: 600 }}>{fmtQty(r.qty, intl)}</span>
                </Link>
              ))}
            </ListCard>

            <ListCard title={t("listShortages")} href="/production/plan" linkLabel={t("open")} empty={t("listEmpty")} isEmpty={!d.shortageList?.length}>
              {d.shortageList?.map((s) => (
                <div key={s.itemId} style={rowStyle}>
                  <span>
                    {s.itemName ?? "—"}{" "}
                    <span style={mutedText}>
                      · {fmtQty(s.available, intl)} / {fmtQty(s.required, intl)} {s.unit ?? ""}
                    </span>
                  </span>
                  <span style={{ fontWeight: 600, color: "#F87171" }}>
                    −{fmtQty(s.missing, intl)} {s.unit ?? ""}
                  </span>
                </div>
              ))}
            </ListCard>

            <ListCard title={t("listExpiring")} href="/production/warehouse" linkLabel={t("open")} empty={t("listEmpty")} isEmpty={!d.expiring?.length}>
              {d.expiring?.map((e, i) => (
                <div key={i} style={rowStyle}>
                  <span>
                    {e.itemName ?? "—"}{" "}
                    <span style={mutedText}>
                      · {fmtQty(e.quantity, intl)} {e.unit ?? ""}
                      {e.locationName ? ` · ${e.locationName}` : ""}
                    </span>
                  </span>
                  <span style={{ fontWeight: 600, color: expiryColor(daysUntil(e.expiryDate)) }}>{fmtDate(e.expiryDate, intl)}</span>
                </div>
              ))}
            </ListCard>
          </div>
        </>
      )}
    </div>
  );
}
