"use client";

// Supplier demand analytics dashboard (supplier-portal expansion #7, Phase 6b).
// Date-range picker + 3 KPI cards with period-over-period deltas + revenue trend
// chart + top/slow item tables + per-buyer table. Dark-theme inline styles to
// match the rest of the supplier cabinet.

import { useMemo, useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { Table, type TableColumn } from "@/components/ui/Table";
import { TrendIndicator } from "@/components/ui/TrendIndicator";
import { useSupplierAnalytics } from "../hooks/useSupplierAnalytics";
import { SupplierRevenueTrendChart } from "./SupplierRevenueTrendChart";
import type {
  SupplierAnalyticsBuyer,
  SupplierAnalyticsItem,
  SupplierPeriodMetric,
} from "../types";

const dateInputStyle: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #374151",
  borderRadius: 8,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "8px 10px",
  outline: "none",
};

const labelStyle: React.CSSProperties = {
  color: "#9CA3AF",
  fontSize: 12,
  fontWeight: 500,
  marginBottom: 6,
  display: "block",
};

function isoDaysAgo(days: number): string {
  const d = new Date();
  d.setUTCDate(d.getUTCDate() - days);
  return d.toISOString().slice(0, 10);
}

function todayIso(): string {
  return new Date().toISOString().slice(0, 10);
}

interface KpiCardProps {
  label: string;
  value: string;
  color: string;
  delta: SupplierPeriodMetric;
  format: "currency" | "number";
}

function KpiCard({ label, value, color, delta, format }: KpiCardProps) {
  const t = useTranslations("Dashboard.supplierCabinet.analytics");
  return (
    <div
      style={{
        background: "#0D1117",
        border: "1px solid #1F2937",
        borderRadius: 10,
        padding: "20px 24px",
        display: "flex",
        flexDirection: "column",
        gap: 6,
      }}
    >
      <div
        style={{
          color: "#4B5563",
          fontSize: 12,
          fontWeight: 500,
          textTransform: "uppercase",
          letterSpacing: "0.05em",
        }}
      >
        {label}
      </div>
      <div style={{ color, fontSize: 26, fontWeight: 700, fontFamily: "monospace", lineHeight: 1.1 }}>
        {value}
      </div>
      <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
        <TrendIndicator current={delta.current} previous={delta.previous} format={format} size="sm" />
        <span style={{ color: "#4B5563", fontSize: 11 }}>{t("vsPreviousPeriod")}</span>
      </div>
    </div>
  );
}

export function SupplierAnalyticsDashboard() {
  const t = useTranslations("Dashboard.supplierCabinet.analytics");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  const [from, setFrom] = useState<string>(() => isoDaysAgo(29));
  const [to, setTo] = useState<string>(() => todayIso());

  const { data, isLoading, isError } = useSupplierAnalytics(from, to);

  const money = (v: number) =>
    v.toLocaleString(intlLocale, { style: "currency", currency: "UAH", maximumFractionDigits: 0 });
  const num = (v: number) => v.toLocaleString(intlLocale, { maximumFractionDigits: 0 });

  const itemColumns: TableColumn<SupplierAnalyticsItem>[] = useMemo(
    () => [
      { key: "name", header: t("colItem"), align: "left", render: (r) => r.itemName },
      { key: "qty", header: t("colQtySold"), render: (r) => num(r.qtySold) },
      { key: "revenue", header: t("colRevenue"), render: (r) => money(r.revenue) },
      { key: "orders", header: t("colOrders"), render: (r) => num(r.orderCount) },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t, intlLocale],
  );

  const buyerColumns: TableColumn<SupplierAnalyticsBuyer>[] = useMemo(
    () => [
      { key: "name", header: t("colBuyer"), align: "left", render: (r) => r.clientName },
      { key: "orders", header: t("colOrders"), render: (r) => num(r.orderCount) },
      { key: "revenue", header: t("colRevenue"), render: (r) => money(r.revenue) },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t, intlLocale],
  );

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 20 }}>
      {/* Range picker */}
      <div style={{ display: "flex", gap: 16, flexWrap: "wrap", alignItems: "flex-end" }}>
        <div>
          <label style={labelStyle}>{t("from")}</label>
          <input
            type="date"
            value={from}
            max={to}
            onChange={(e) => e.target.value && setFrom(e.target.value)}
            style={dateInputStyle}
          />
        </div>
        <div>
          <label style={labelStyle}>{t("to")}</label>
          <input
            type="date"
            value={to}
            min={from}
            max={todayIso()}
            onChange={(e) => e.target.value && setTo(e.target.value)}
            style={dateInputStyle}
          />
        </div>
        <div style={{ display: "flex", gap: 8 }}>
          {[30, 90, 365].map((d) => (
            <button
              key={d}
              type="button"
              onClick={() => {
                setFrom(isoDaysAgo(d - 1));
                setTo(todayIso());
              }}
              style={{
                background: "transparent",
                border: "1px solid #374151",
                borderRadius: 8,
                color: "#9CA3AF",
                fontSize: 12,
                fontWeight: 600,
                padding: "8px 12px",
                cursor: "pointer",
              }}
            >
              {t("presetDays", { n: d })}
            </button>
          ))}
        </div>
      </div>

      {isError ? (
        <div style={{ color: "#F87171", fontSize: 13 }}>{t("errorLoad")}</div>
      ) : isLoading || !data ? (
        <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
          {[...Array(3)].map((_, i) => (
            <div key={i} style={{ height: 96, background: "#111827", borderRadius: 10 }} />
          ))}
        </div>
      ) : (
        <>
          {/* KPI cards */}
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))",
              gap: 12,
            }}
          >
            <KpiCard
              label={t("kpiRevenue")}
              value={money(data.totalRevenue)}
              color="#4ADE80"
              delta={data.revenueDelta}
              format="currency"
            />
            <KpiCard
              label={t("kpiOrders")}
              value={num(data.orderCount)}
              color="#60A5FA"
              delta={data.orderCountDelta}
              format="number"
            />
            <KpiCard
              label={t("kpiUnits")}
              value={num(data.itemsSold)}
              color="#FBBF24"
              delta={data.itemsSoldDelta}
              format="number"
            />
          </div>

          <SupplierRevenueTrendChart points={data.revenueTrend} />

          {/* Top / slow items */}
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fit, minmax(320px, 1fr))",
              gap: 16,
            }}
          >
            <div>
              <h3 style={sectionTitle}>{t("topItemsTitle")}</h3>
              <Table
                columns={itemColumns}
                rows={data.topItems}
                rowKey={(r) => r.supplierItemId ?? r.itemName}
                emptyMessage={t("noData")}
              />
            </div>
            <div>
              <h3 style={sectionTitle}>{t("slowItemsTitle")}</h3>
              <Table
                columns={itemColumns}
                rows={data.slowItems}
                rowKey={(r) => r.supplierItemId ?? r.itemName}
                emptyMessage={t("noData")}
              />
            </div>
          </div>

          {/* By buyer */}
          <div>
            <h3 style={sectionTitle}>{t("byBuyerTitle")}</h3>
            <Table
              columns={buyerColumns}
              rows={data.byBuyer}
              rowKey={(r) => r.clientTenantId}
              emptyMessage={t("noData")}
            />
          </div>
        </>
      )}
    </div>
  );
}

const sectionTitle: React.CSSProperties = {
  color: "#9CA3AF",
  fontSize: 13,
  fontWeight: 600,
  margin: "0 0 10px",
};
